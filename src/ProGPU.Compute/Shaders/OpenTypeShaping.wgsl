// Algorithm: initialize a shaping run by binary-searching a compressed nominal cmap, then load direction-aware design-unit metrics by glyph ID.
// Time complexity: O(N log R) initialization and O(N) metric loading for N input scalars and R sorted cmap ranges; each invocation has bounded log2(R) search.
// Space complexity: O(N + R + G) storage for glyph records, cmap ranges, and G glyph metrics; each invocation uses O(1) private storage and no textures.
// Workgroups contain 64 independent glyph invocations. All arithmetic is exact 32-bit integer design-unit arithmetic; no quality approximation is used.

struct Params {
    glyph_count: u32,
    cmap_count: u32,
    metric_count: u32,
    direction: u32,
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

@group(0) @binding(0) var<uniform> params: Params;
@group(0) @binding(1) var<storage, read> input_scalars: array<InputScalar>;
@group(0) @binding(2) var<storage, read> cmap_ranges: array<CmapRange>;
@group(0) @binding(3) var<storage, read> glyph_metrics: array<GlyphMetric>;
@group(0) @binding(4) var<storage, read_write> glyphs: array<ShapingGlyph>;
@group(0) @binding(5) var<uniform> table_directory: TableDirectory;
@group(0) @binding(6) var<storage, read> table_words: array<u32>;

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
    if (index >= params.glyph_count) { return; }
    let input = input_scalars[index];
    glyphs[index] = ShapingGlyph(
        nominal_glyph(input.codepoint), input.codepoint, input.cluster, input.flags,
        0, 0, 0, 0);
}

@compute @workgroup_size(64)
fn load_metrics(@builtin(global_invocation_id) id: vec3<u32>) {
    let index = id.x;
    if (index >= params.glyph_count) { return; }
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
