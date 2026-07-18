// Algorithm: initialize a shaping run by binary-searching a compressed nominal cmap, then load direction-aware design-unit metrics by glyph ID.
// Time complexity: O(N log R + L*N*log C) for N scalars, R cmap ranges, L ordered ranged lookups, and coverage size C; initialization/metrics are parallel and lookup mutation is serial.
// Space complexity: O(N + R + G + L) storage for glyphs, cmap ranges, G metrics, and lookup commands; each invocation uses O(1) private storage and no textures.
// Workgroups contain 64 independent glyph invocations; the ordered lookup VM uses one invocation because substitutions mutate shared order. Runtime loops are bounded by uploaded counts/capacity and OpenType table counts. Lookup flags use GDEF glyph/mark classes and mark-set coverage without auxiliary allocations. All arithmetic is exact 32-bit integer design-unit arithmetic.

struct Params {
    input_count: u32,
    capacity: u32,
    cmap_count: u32,
    metric_count: u32,
    direction: u32,
    lookup_count: u32,
    reserved0: u32,
    reserved1: u32,
};

struct InputScalar {
    codepoint: u32,
    cluster: i32,
    flags: u32,
    reserved: u32,
};

struct CmapRange {
    start: u32,
    end: u32,
    glyph: u32,
    kind: u32,
};

struct GlyphMetric {
    advance_x: i32,
    advance_y: i32,
    origin_x: i32,
    origin_y: i32,
};

struct ShapingGlyph {
    glyph_id: u32,
    codepoint: u32,
    cluster: i32,
    flags: u32,
    advance_x: i32,
    advance_y: i32,
    offset_x: i32,
    offset_y: i32,
};

struct TableDirectory {
    gdef_offset: u32,
    gdef_length: u32,
    gsub_offset: u32,
    gsub_length: u32,
    gpos_offset: u32,
    gpos_length: u32,
    kern_offset: u32,
    kern_length: u32,
};

struct RunState {
    glyph_count: u32,
    status: u32,
    reserved0: u32,
    reserved1: u32,
};

struct LookupCommand {
    table_kind: u32,
    lookup_offset: u32,
    lookup_type: u32,
    lookup_flags: u32,
    feature_tag: u32,
    feature_value: u32,
    range_start: u32,
    range_end: u32,
};

@group(0) @binding(0) var<uniform> params: Params;
@group(0) @binding(1) var<storage, read> input_scalars: array<InputScalar>;
@group(0) @binding(2) var<storage, read> cmap_ranges: array<CmapRange>;
@group(0) @binding(3) var<storage, read> glyph_metrics: array<GlyphMetric>;
@group(0) @binding(4) var<storage, read_write> glyphs: array<ShapingGlyph>;
@group(0) @binding(5) var<uniform> table_directory: TableDirectory;
@group(0) @binding(6) var<storage, read> table_words: array<u32>;
@group(0) @binding(7) var<storage, read_write> run_state: RunState;
@group(0) @binding(8) var<storage, read> lookup_commands: array<LookupCommand>;

fn table_u8(offset: u32) -> u32 {
    let word = table_words[offset >> 2u];
    return (word >> ((offset & 3u) * 8u)) & 0xffu;
}

fn table_u16(offset: u32) -> u32 {
    return (table_u8(offset) << 8u) | table_u8(offset + 1u);
}

fn table_u32(offset: u32) -> u32 {
    return (table_u8(offset) << 24u) | (table_u8(offset + 1u) << 16u) |
        (table_u8(offset + 2u) << 8u) | table_u8(offset + 3u);
}

fn coverage_index(offset: u32, glyph: u32) -> i32 {
    let format = table_u16(offset);
    if (format == 1u) {
        let count = table_u16(offset + 2u);
        var low = 0u;
        var high = count;
        loop {
            if (low >= high) { break; }
            let middle = low + ((high - low) >> 1u);
            let value = table_u16(offset + 4u + middle * 2u);
            if (glyph < value) { high = middle; }
            else if (glyph > value) { low = middle + 1u; }
            else { return i32(middle); }
        }
    } else if (format == 2u) {
        let count = table_u16(offset + 2u);
        for (var index = 0u; index < count; index++) {
            let record = offset + 4u + index * 6u;
            let start = table_u16(record);
            let end = table_u16(record + 2u);
            if (glyph >= start && glyph <= end) {
                return i32(table_u16(record + 4u) + glyph - start);
            }
        }
    }
    return -1;
}

fn class_value(offset: u32, glyph: u32) -> u32 {
    if (offset == 0u) { return 0u; }
    let format = table_u16(offset);
    if (format == 1u) {
        let start = table_u16(offset + 2u);
        let count = table_u16(offset + 4u);
        if (glyph >= start && glyph - start < count) {
            return table_u16(offset + 6u + (glyph - start) * 2u);
        }
    } else if (format == 2u) {
        let count = table_u16(offset + 2u);
        for (var index = 0u; index < count; index++) {
            let record = offset + 4u + index * 6u;
            let start = table_u16(record);
            let end = table_u16(record + 2u);
            if (glyph >= start && glyph <= end) { return table_u16(record + 4u); }
        }
    }
    return 0u;
}

fn gdef_class(relative_field: u32, glyph: u32) -> u32 {
    if (table_directory.gdef_length < relative_field + 2u) { return 0u; }
    let relative = table_u16(table_directory.gdef_offset + relative_field);
    if (relative == 0u) { return 0u; }
    return class_value(table_directory.gdef_offset + relative, glyph);
}

fn in_mark_filtering_set(lookup_offset: u32, glyph: u32) -> bool {
    if (table_directory.gdef_length < 14u) { return false; }
    let minor = table_u16(table_directory.gdef_offset + 2u);
    if (minor < 2u) { return false; }
    let sets_relative = table_u16(table_directory.gdef_offset + 12u);
    if (sets_relative == 0u) { return false; }
    let sets = table_directory.gdef_offset + sets_relative;
    if (table_u16(sets) != 1u) { return false; }
    let subtable_count = table_u16(lookup_offset + 4u);
    let set_index = table_u16(lookup_offset + 6u + subtable_count * 2u);
    let set_count = table_u16(sets + 2u);
    if (set_index >= set_count) { return false; }
    let coverage = sets + table_u32(sets + 4u + set_index * 4u);
    return coverage_index(coverage, glyph) >= 0;
}

fn lookup_ignored(position: u32, lookup_offset: u32, lookup_flags: u32) -> bool {
    let glyph = glyphs[position].glyph_id;
    let glyph_class = gdef_class(4u, glyph);
    if ((lookup_flags & 2u) != 0u && glyph_class == 1u) { return true; }
    if ((lookup_flags & 4u) != 0u && glyph_class == 2u) { return true; }
    if ((lookup_flags & 8u) != 0u && glyph_class == 3u) { return true; }
    if (glyph_class == 3u) {
        let attachment_type = lookup_flags >> 8u;
        if (attachment_type != 0u && gdef_class(10u, glyph) != attachment_type) { return true; }
        if ((lookup_flags & 16u) != 0u && !in_mark_filtering_set(lookup_offset, glyph)) { return true; }
    }
    return false;
}

fn next_eligible(start: u32, lookup_offset: u32, lookup_flags: u32) -> i32 {
    for (var index = start; index < run_state.glyph_count; index++) {
        if (!lookup_ignored(index, lookup_offset, lookup_flags)) { return i32(index); }
    }
    return -1;
}

fn apply_single_substitution(subtable: u32, position: u32) -> bool {
    let format = table_u16(subtable);
    let coverage = subtable + table_u16(subtable + 2u);
    let covered = coverage_index(coverage, glyphs[position].glyph_id);
    if (covered < 0) { return false; }
    if (format == 1u) {
        let delta = i32(table_u16(subtable + 4u) << 16u) >> 16;
        glyphs[position].glyph_id = u32(i32(glyphs[position].glyph_id) + delta) & 0xffffu;
        return true;
    }
    if (format == 2u && u32(covered) < table_u16(subtable + 4u)) {
        glyphs[position].glyph_id = table_u16(subtable + 6u + u32(covered) * 2u);
        return true;
    }
    return false;
}

fn replace_multiple(subtable: u32, position: u32) -> bool {
    let covered = coverage_index(subtable + table_u16(subtable + 2u), glyphs[position].glyph_id);
    let sequence_count = table_u16(subtable + 4u);
    if (covered < 0 || u32(covered) >= sequence_count) { return false; }
    let sequence = subtable + table_u16(subtable + 6u + u32(covered) * 2u);
    let replacement_count = table_u16(sequence);
    if (replacement_count == 0u) {
        for (var cursor = position + 1u; cursor < run_state.glyph_count; cursor++) {
            glyphs[cursor - 1u] = glyphs[cursor];
        }
        run_state.glyph_count -= 1u;
        return true;
    }
    let extra = replacement_count - 1u;
    if (run_state.glyph_count + extra > params.capacity) {
        run_state.status = 1u;
        return false;
    }
    var cursor = run_state.glyph_count;
    loop {
        if (cursor <= position + 1u) { break; }
        cursor -= 1u;
        glyphs[cursor + extra] = glyphs[cursor];
    }
    let source = glyphs[position];
    for (var replacement = 0u; replacement < replacement_count; replacement++) {
        glyphs[position + replacement] = source;
        glyphs[position + replacement].glyph_id = table_u16(sequence + 2u + replacement * 2u);
    }
    run_state.glyph_count += extra;
    return true;
}

fn apply_alternate(subtable: u32, position: u32, feature_value: u32) -> bool {
    let covered = coverage_index(subtable + table_u16(subtable + 2u), glyphs[position].glyph_id);
    let set_count = table_u16(subtable + 4u);
    if (covered < 0 || u32(covered) >= set_count) { return false; }
    let alternate_set = subtable + table_u16(subtable + 6u + u32(covered) * 2u);
    let count = table_u16(alternate_set);
    if (count == 0u) { return false; }
    let selected = min(max(feature_value, 1u), count) - 1u;
    glyphs[position].glyph_id = table_u16(alternate_set + 2u + selected * 2u);
    return true;
}

fn apply_ligature(subtable: u32, position: u32, lookup_offset: u32, lookup_flags: u32) -> bool {
    let covered = coverage_index(subtable + table_u16(subtable + 2u), glyphs[position].glyph_id);
    let set_count = table_u16(subtable + 4u);
    if (covered < 0 || u32(covered) >= set_count) { return false; }
    let ligature_set = subtable + table_u16(subtable + 6u + u32(covered) * 2u);
    let ligature_count = table_u16(ligature_set);
    for (var ligature_index = 0u; ligature_index < ligature_count; ligature_index++) {
        let ligature = ligature_set + table_u16(ligature_set + 2u + ligature_index * 2u);
        let component_count = table_u16(ligature + 2u);
        if (component_count == 0u) { continue; }
        var matched = true;
        var match_position = position;
        for (var component = 1u; component < component_count; component++) {
            let next = next_eligible(match_position + 1u, lookup_offset, lookup_flags);
            if (next < 0 || glyphs[u32(next)].glyph_id != table_u16(ligature + 2u + component * 2u)) {
                matched = false;
                break;
            }
            match_position = u32(next);
        }
        if (!matched) { continue; }
        var cluster = glyphs[position].cluster;
        for (var cursor = position + 1u; cursor <= match_position; cursor++) {
            cluster = min(cluster, glyphs[cursor].cluster);
        }
        glyphs[position].glyph_id = table_u16(ligature);
        glyphs[position].cluster = cluster;
        for (var component = 1u; component < component_count; component++) {
            let remove_at = next_eligible(position + 1u, lookup_offset, lookup_flags);
            if (remove_at < 0) { break; }
            for (var cursor = u32(remove_at) + 1u; cursor < run_state.glyph_count; cursor++) {
                glyphs[cursor - 1u] = glyphs[cursor];
            }
            run_state.glyph_count -= 1u;
        }
        return true;
    }
    return false;
}

fn apply_gsub_subtable(lookup_type: u32, subtable: u32, position: u32, value: u32,
    lookup_offset: u32, lookup_flags: u32) -> bool {
    if (lookup_type == 1u) { return apply_single_substitution(subtable, position); }
    if (lookup_type == 2u && table_u16(subtable) == 1u) { return replace_multiple(subtable, position); }
    if (lookup_type == 3u && table_u16(subtable) == 1u) { return apply_alternate(subtable, position, value); }
    if (lookup_type == 4u && table_u16(subtable) == 1u) {
        return apply_ligature(subtable, position, lookup_offset, lookup_flags);
    }
    return false;
}

@compute @workgroup_size(1)
fn execute_lookups(@builtin(global_invocation_id) id: vec3<u32>) {
    if (id.x != 0u) { return; }
    for (var command_index = 0u; command_index < params.lookup_count; command_index++) {
        let command = lookup_commands[command_index];
        if (command.table_kind != 1u || command.feature_value == 0u) { continue; }
        let subtable_count = table_u16(command.lookup_offset + 4u);
        for (var position = 0u; position < run_state.glyph_count; position++) {
            let cluster = u32(max(glyphs[position].cluster, 0));
            if (cluster < command.range_start || cluster >= command.range_end) { continue; }
            if (lookup_ignored(position, command.lookup_offset, command.lookup_flags)) { continue; }
            for (var subtable_index = 0u; subtable_index < subtable_count; subtable_index++) {
                let subtable = command.lookup_offset + table_u16(command.lookup_offset + 6u + subtable_index * 2u);
                var effective_type = command.lookup_type;
                var effective_subtable = subtable;
                if (effective_type == 7u && table_u16(subtable) == 1u) {
                    effective_type = table_u16(subtable + 2u);
                    effective_subtable = subtable + table_u32(subtable + 4u);
                }
                if (apply_gsub_subtable(effective_type, effective_subtable, position, command.feature_value,
                        command.lookup_offset, command.lookup_flags)) { break; }
            }
            if (run_state.status != 0u) { return; }
        }
    }
}

fn nominal_glyph(codepoint: u32) -> u32 {
    var low = 0u;
    var high = params.cmap_count;
    loop {
        if (low >= high) { break; }
        let middle = low + ((high - low) >> 1u);
        let range = cmap_ranges[middle];
        if (codepoint < range.start) {
            high = middle;
        } else if (codepoint > range.end) {
            low = middle + 1u;
        } else if (range.kind == 1u) {
            return range.glyph;
        } else {
            return range.glyph + codepoint - range.start;
        }
    }
    return 0u;
}

@compute @workgroup_size(64)
fn initialize_glyphs(@builtin(global_invocation_id) id: vec3<u32>) {
    let index = id.x;
    if (index >= params.input_count) { return; }
    if (index == 0u) { run_state = RunState(params.input_count, 0u, 0u, 0u); }
    let input = input_scalars[index];
    glyphs[index] = ShapingGlyph(
        nominal_glyph(input.codepoint), input.codepoint, input.cluster, input.flags,
        0, 0, 0, 0);
}

@compute @workgroup_size(64)
fn load_metrics(@builtin(global_invocation_id) id: vec3<u32>) {
    let index = id.x;
    if (index >= run_state.glyph_count) { return; }
    let glyph_id = glyphs[index].glyph_id;
    if (glyph_id >= params.metric_count) { return; }
    let metric = glyph_metrics[glyph_id];
    if (params.direction == 3u || params.direction == 4u) {
        glyphs[index].advance_y = -metric.advance_y;
        glyphs[index].offset_x = -metric.origin_x;
        glyphs[index].offset_y = -metric.origin_y;
    } else {
        glyphs[index].advance_x = metric.advance_x;
    }
}
