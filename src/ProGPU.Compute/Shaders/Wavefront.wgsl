// Algorithm: Retain flattened vector curves and transform-indexed shape instances, patch stable spatial transforms independently, consume deterministic GPU coverage-bitmaps, GPU-emitted overlap pairs grouped by four stable 8-bit radix passes, or compact stable CPU sparse bins, conservatively classify solid/outside cell-shape pairs, build one bounded tile-local edge command plus 16 exact row backdrops per edge pair in workgroup memory, and indirectly dispatch only active 16x16 cells to a sparse output texture.
// Time complexity: O(dC*S) for newly appended curves (O(C*S) only after an arena grow replay), O(dT) CPU upload for changed retained transforms, O(O + G*ceil(I/32) + G + O*log L) for bitmap binning, O(I + O + 4*(O*256 + H)) for the portable stable-radix pair route with H=256*ceil(O/256) histogram counters, or O(O*log L) after compact CPU bins are uploaded, plus O(Ke*L + Pa*Ke*min(Lt,256)) for ordinary coarse/fine work and O(Pa*Ke*log L) only for overflowing commands; fixed 256-way local rank preserves painter order without subgroup requirements.
// Space complexity: O(C*S + I + T + min(G*ceil(I/32), B) + A + O + H + G + W*Hpx) device storage plus 3,144 bytes of fixed module workgroup storage (1,096 bytes for edge commands), where A is active cells, B is the admitted bitmap-word cap, H is radix histogram storage, Lt is the tile-local line count, and W*Hpx is one sparse ping-pong texture; edge commands cap at 256 local line indices and fall back without dropping coverage, the radix route is capped at 2,097,152 pairs, and there is no full-window texture copy.
struct BvhNode {
    min_bounds: vec2<f32>,
    max_bounds: vec2<f32>,
    left_child_or_first_line: u32,
    primitive_count: u32,
    right_child: u32,
    pad1: u32,
};

struct ShapeInstance {
    transform: mat4x4<f32>,
    inv_transform: mat4x4<f32>,
    min_bounds: vec2<f32>,
    max_bounds: vec2<f32>,
    bvh_root_idx: u32,
    shape_id: u32,
    transform_index: u32,
    transform_pad: u32,
    color: vec4<f32>,
    is_text: u32,
    pad0: u32,
    pad1: u32,
    pad2: u32,
};

struct ShapeTransform {
    transform: mat4x4<f32>,
    inv_transform: mat4x4<f32>,
};

struct GridCell {
    shape_start_offset: u32,
    shape_count: u32,
};

struct Uniforms {
    screenWidth: u32,
    screenHeight: u32,
    gridStride: u32,
    instanceCount: u32,
    maxQueueSize: u32,
    currentFrameIndex: u32,
    fontWeightOffset: f32,
    dpiScale: f32,
    curveCount: u32,
    coverageWordCount: u32,
    wordsPerCell: u32,
    cellCount: u32,
    pairCount: u32,
    curveStart: u32,
    binningMode: u32,
    pad2: u32,
};

struct LineSegment {
    start: vec2<f32>,
    end: vec2<f32>,
};

struct BezierCurve {
    p0: vec2<f32>,
    p1: vec2<f32>,
    p2: vec2<f32>,
    p3: vec2<f32>,
    curve_type: u32,
    subdivisions: u32,
    line_offset: u32,
    pad: u32,
};

struct DispatchIndirectArgs {
    x: u32,
    y: u32,
    z: u32,
};

struct DrawIndirectArgs {
    vertex_count: u32,
    instance_count: u32,
    first_vertex: u32,
    first_instance: u32,
};

struct RadixParams {
    shift: u32,
    source_index: u32,
    pair_count: u32,
    block_count: u32,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(3) var<storage, read> bvh_nodes: array<BvhNode>;
@group(0) @binding(4) var<storage, read> shape_instances: array<ShapeInstance>;
@group(0) @binding(5) var<storage, read_write> grid_cells: array<GridCell>;
@group(0) @binding(6) var<storage, read_write> cell_shape_indices: array<u32>;
@group(0) @binding(7) var<storage, read_write> cell_coverage_words: array<atomic<u32>>;
@group(0) @binding(8) var screen_texture: texture_2d<f32>;
@group(0) @binding(9) var<storage, read_write> output_lines: array<LineSegment>;
@group(0) @binding(11) var screen_texture_write: texture_storage_2d<bgra8unorm, write>;
@group(0) @binding(12) var<storage, read> raw_curves: array<BezierCurve>;
@group(0) @binding(13) var<storage, read_write> bin_word_counts: array<u32>;
@group(0) @binding(14) var<storage, read> bin_word_offsets: array<u32>;
@group(0) @binding(15) var<storage, read_write> active_cell_flags: array<u32>;
@group(0) @binding(16) var<storage, read> active_cell_offsets: array<u32>;
@group(0) @binding(17) var<storage, read_write> active_cell_indices: array<u32>;
@group(0) @binding(18) var<storage, read_write> active_dispatch_args: array<DispatchIndirectArgs>;
@group(0) @binding(19) var<storage, read_write> cell_shape_classes: array<u32>;
@group(0) @binding(20) var<storage, read_write> active_draw_args: array<DrawIndirectArgs>;
@group(0) @binding(21) var<storage, read> shape_transforms: array<ShapeTransform>;
@group(0) @binding(22) var<storage, read_write> pair_cell_keys_a: array<u32>;
@group(0) @binding(23) var<storage, read_write> pair_cell_keys_b: array<u32>;
@group(0) @binding(24) var<storage, read_write> pair_instance_indices_b: array<u32>;
@group(0) @binding(25) var<uniform> radix_params: RadixParams;
@group(0) @binding(26) var<storage, read_write> instance_pair_counts: array<u32>;
@group(0) @binding(27) var<storage, read> instance_pair_offsets: array<u32>;
@group(0) @binding(28) var<storage, read_write> radix_histogram_counts: array<u32>;
@group(0) @binding(29) var<storage, read> radix_histogram_offsets: array<u32>;

var<workgroup> radix_workgroup_histogram: array<atomic<u32>, 256>;
var<workgroup> radix_workgroup_digits: array<u32, 256>;
// Fine rasterization has one 16x16 workgroup per active cell. Lane zero builds this Vello-style
// tile command once for each painter-ordered edge pair. Sixteen row backdrops retain exact winding
// contributions from lines wholly to the right; the local list contains only lines that can affect
// winding or half-pixel signed-distance coverage inside the tile. Commands exceeding the fixed
// line capacity set overflow and use the existing per-pixel BVH evaluator for that pair.
const EDGE_COMMAND_LINE_CAPACITY = 256u;
var<workgroup> edge_command_line_count: u32;
var<workgroup> edge_command_overflow: u32;
var<workgroup> edge_command_line_indices: array<u32, 256>;
var<workgroup> edge_command_row_backdrops: array<i32, 16>;


fn evaluate_curve(curve: BezierCurve, t: f32) -> vec2<f32> {
    if (curve.curve_type == 0u) {
        return (1.0 - t) * curve.p0 + t * curve.p1;
    } else if (curve.curve_type == 1u) {
        let oneMinusT = 1.0 - t;
        return oneMinusT * oneMinusT * curve.p0 + 2.0 * oneMinusT * t * curve.p1 + t * t * curve.p2;
    } else {
        let oneMinusT = 1.0 - t;
        return oneMinusT * oneMinusT * oneMinusT * curve.p0 
             + 3.0 * oneMinusT * oneMinusT * t * curve.p1 
             + 3.0 * oneMinusT * t * t * curve.p2 
             + t * t * t * curve.p3;
    }
}

@compute @workgroup_size(64)
fn flatten_curves(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let local_curve_idx = global_id.x;
    if (local_curve_idx >= uniforms.curveCount) {
        return;
    }
    let curve_idx = uniforms.curveStart + local_curve_idx;
    
    let curve = raw_curves[curve_idx];
    let count = curve.subdivisions;
    let start_offset = curve.line_offset;
    
    // CPU preparation proves the max(|B''|) * h^2 / 8 chord-error bound at the
    // quantized upper device scale and rejects curves requiring more than 256 iterations.
    for (var i = 0u; i < count; i = i + 1u) {
        let t0 = f32(i) / f32(count);
        let t1 = f32(i + 1u) / f32(count);
        
        let p0 = evaluate_curve(curve, t0);
        let p1 = evaluate_curve(curve, t1);
        
        output_lines[start_offset + i] = LineSegment(p0, p1);
    }
}


fn ray_intersects_aabb(p: vec2<f32>, min_b: vec2<f32>, max_b: vec2<f32>) -> bool {
    return p.y >= min_b.y && p.y <= max_b.y && p.x <= max_b.x;
}

fn point_in_aabb(p: vec2<f32>, min_b: vec2<f32>, max_b: vec2<f32>) -> bool {
    return p.x >= min_b.x && p.x <= max_b.x && p.y >= min_b.y && p.y <= max_b.y;
}

fn bvh_node_overlap(p: vec2<f32>, min_b: vec2<f32>, max_b: vec2<f32>, radius: f32) -> bool {
    let ray_overlap = p.y >= min_b.y && p.y <= max_b.y && p.x <= max_b.x;
    if (ray_overlap) {
        return true;
    }
    let dx = max(0.0, max(min_b.x - p.x, p.x - max_b.x));
    let dy = max(0.0, max(min_b.y - p.y, p.y - max_b.y));
    return (dx * dx + dy * dy) <= (radius * radius);
}

fn check_line_intersection(pixel_pos: vec2<f32>, line: LineSegment) -> i32 {
    let A = line.start;
    let B = line.end;
    let deriv_y = B.y - A.y;

    if (deriv_y == 0.0) {
        return 0;
    }

    let spans_y = (A.y <= pixel_pos.y && B.y > pixel_pos.y) || 
                  (B.y <= pixel_pos.y && A.y > pixel_pos.y);

    if (!spans_y) {
        return 0;
    }

    let t = (pixel_pos.y - A.y) / deriv_y;
    var is_valid = false;
    if (deriv_y > 0.0) {
        is_valid = (t >= 0.0 && t < 1.0);
    } else {
        is_valid = (t > 0.0 && t <= 1.0);
    }

    if (is_valid) {
        let intersect_x = A.x + t * (B.x - A.x);
        if (pixel_pos.x < intersect_x) {
            if (deriv_y > 0.0) {
                return 1;
            } else {
                return -1;
            }
        }
    }

    return 0;
}

fn transformed_device_line(line: LineSegment, transform: mat4x4<f32>) -> LineSegment {
    return LineSegment(
        (transform * vec4<f32>(line.start, 0.0, 1.0)).xy * uniforms.dpiScale,
        (transform * vec4<f32>(line.end, 0.0, 1.0)).xy * uniforms.dpiScale);
}

fn line_distance(point: vec2<f32>, line: LineSegment) -> f32 {
    let ab = line.end - line.start;
    let length_squared = dot(ab, ab);
    if (length_squared <= 1e-12) {
        return distance(point, line.start);
    }
    let t = clamp(dot(point - line.start, ab) / length_squared, 0.0, 1.0);
    return distance(point, line.start + t * ab);
}

struct ShapeEvaluation {
    winding: i32,
    min_distance: f32,
};

fn evaluate_shape(local_pos: vec2<f32>, root_node: u32) -> ShapeEvaluation {
    var winding = 0;
    var min_dist = 99999.0;
    var stack: array<u32, 16>;
    var stack_ptr = 0u;
    var current_node = root_node;

    while (true) {
        let node = bvh_nodes[current_node];
        if (bvh_node_overlap(local_pos, node.min_bounds, node.max_bounds, min_dist)) {
            if (node.primitive_count > 0u) {
                let start_line = node.left_child_or_first_line;
                let end_line = start_line + node.primitive_count;
                for (var line_idx = start_line; line_idx < end_line; line_idx = line_idx + 1u) {
                    let line = output_lines[line_idx];
                    winding += check_line_intersection(local_pos, line);

                    let ab = line.end - line.start;
                    let ap = local_pos - line.start;
                    let t = clamp(dot(ap, ab) / dot(ab, ab), 0.0, 1.0);
                    let closest_point = line.start + t * ab;
                    min_dist = min(min_dist, distance(local_pos, closest_point));
                }
            } else {
                if (stack_ptr < 16u) {
                    stack[stack_ptr] = node.right_child;
                    stack_ptr = stack_ptr + 1u;
                }
                current_node = node.left_child_or_first_line;
                continue;
            }
        }

        if (stack_ptr == 0u) {
            break;
        }
        stack_ptr = stack_ptr - 1u;
        current_node = stack[stack_ptr];
    }

    return ShapeEvaluation(winding, min_dist);
}

fn minimum_device_scale(transform: mat4x4<f32>) -> f32 {
    let x_basis = transform[0].xy;
    let y_basis = transform[1].xy;
    let a = dot(x_basis, x_basis);
    let b = dot(x_basis, y_basis);
    let d = dot(y_basis, y_basis);
    let discriminant = sqrt(max(0.0, (a - d) * (a - d) + 4.0 * b * b));
    return sqrt(max(0.0, 0.5 * (a + d - discriminant)));
}

fn instance_transform(inst: ShapeInstance) -> mat4x4<f32> {
    // C# composes row-vector transforms as local * retained. The uploaded matrices are consumed
    // as WGSL column-vector matrices, so the equivalent order is retained * local here.
    return shape_transforms[inst.transform_index].transform * inst.transform;
}

fn instance_inverse_transform(inst: ShapeInstance) -> mat4x4<f32> {
    return inst.inv_transform * shape_transforms[inst.transform_index].inv_transform;
}

fn build_edge_command(
    root_node: u32,
    transform: mat4x4<f32>,
    cell_origin: vec2<f32>,
    cell_size: vec2<f32>) {
    edge_command_line_count = 0u;
    edge_command_overflow = 0u;
    for (var row = 0u; row < 16u; row = row + 1u) {
        edge_command_row_backdrops[row] = 0;
    }

    let cell_end = cell_origin + cell_size;
    var stack: array<u32, 16>;
    var stack_ptr = 0u;
    var current_node = root_node;
    while (true) {
        let node = bvh_nodes[current_node];
        if (node.primitive_count > 0u) {
            let end_line = node.left_child_or_first_line + node.primitive_count;
            for (var line_idx = node.left_child_or_first_line; line_idx < end_line; line_idx = line_idx + 1u) {
                let device_line = transformed_device_line(output_lines[line_idx], transform);
                let line_min = min(device_line.start, device_line.end);
                let line_max = max(device_line.start, device_line.end);

                // Pixel centers span [origin + 0.5, end - 0.5]. Half-pixel AA support therefore
                // expands that domain exactly to the closed cell bounds used here.
                if (line_max.y < cell_origin.y || line_min.y > cell_end.y || line_max.x < cell_origin.x) {
                    continue;
                }

                if (line_min.x > cell_end.x) {
                    // This line is to the right of every pixel in the tile. Preserve its exact
                    // center-sample winding contribution per row instead of putting it in the
                    // local edge list.
                    for (var row = 0u; row < 16u; row = row + 1u) {
                        if (f32(row) < cell_size.y) {
                            let row_point = vec2<f32>(cell_end.x, cell_origin.y + f32(row) + 0.5);
                            edge_command_row_backdrops[row] += check_line_intersection(row_point, device_line);
                        }
                    }
                } else if (edge_command_line_count < EDGE_COMMAND_LINE_CAPACITY) {
                    edge_command_line_indices[edge_command_line_count] = line_idx;
                    edge_command_line_count = edge_command_line_count + 1u;
                } else {
                    edge_command_overflow = 1u;
                }
            }
        } else {
            if (stack_ptr >= 16u) {
                edge_command_overflow = 1u;
                break;
            }
            stack[stack_ptr] = node.right_child;
            stack_ptr = stack_ptr + 1u;
            current_node = node.left_child_or_first_line;
            continue;
        }

        if (stack_ptr == 0u) {
            break;
        }
        stack_ptr = stack_ptr - 1u;
        current_node = stack[stack_ptr];
    }
}

fn evaluate_edge_command(
    device_pos: vec2<f32>,
    row: u32,
    transform: mat4x4<f32>) -> ShapeEvaluation {
    var winding = edge_command_row_backdrops[row];
    var min_dist = 99999.0;
    for (var command_idx = 0u; command_idx < edge_command_line_count; command_idx = command_idx + 1u) {
        let line = transformed_device_line(
            output_lines[edge_command_line_indices[command_idx]],
            transform);
        winding += check_line_intersection(device_pos, line);
        min_dist = min(min_dist, line_distance(device_pos, line));
    }
    return ShapeEvaluation(winding, min_dist);
}

fn active_grid_cell(active_idx: u32, cell_idx: u32) -> GridCell {
    // GPU bitmap scatter stores cells by screen-cell index. The bounded CPU sparse route uploads
    // only compact active records, avoiding O(G) grid-buffer traffic when occupancy is low.
    if (uniforms.binningMode == 1u) {
        return grid_cells[active_idx];
    }
    return grid_cells[cell_idx];
}

// Fixed workgroup size: 256. One invocation clears one 32-instance coverage word.
// Bandwidth: one 32-bit store per coverage word; no scene reads.
@compute @workgroup_size(256)
fn clear_bin_words(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let word_idx = global_id.x;
    if (word_idx >= uniforms.coverageWordCount) {
        return;
    }
    atomicStore(&cell_coverage_words[word_idx], 0u);
}

fn transformed_device_bounds(inst: ShapeInstance) -> vec4<f32> {
    let transform = instance_transform(inst);
    let p0 = (transform * vec4<f32>(inst.min_bounds, 0.0, 1.0)).xy * uniforms.dpiScale;
    let p1 = (transform * vec4<f32>(inst.max_bounds.x, inst.min_bounds.y, 0.0, 1.0)).xy * uniforms.dpiScale;
    let p2 = (transform * vec4<f32>(inst.min_bounds.x, inst.max_bounds.y, 0.0, 1.0)).xy * uniforms.dpiScale;
    let p3 = (transform * vec4<f32>(inst.max_bounds, 0.0, 1.0)).xy * uniforms.dpiScale;
    let bounds_min = min(min(p0, p1), min(p2, p3));
    let bounds_max = max(max(p0, p1), max(p2, p3));
    return vec4<f32>(bounds_min, bounds_max);
}

fn covered_cell_range(inst: ShapeInstance) -> vec4<u32> {
    let bounds = transformed_device_bounds(inst);
    let screen_max = vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight));
    if (any(bounds.zw < vec2<f32>(0.0)) || any(bounds.xy > screen_max)) {
        return vec4<u32>(1u, 1u, 0u, 0u);
    }
    let clipped_min = clamp(bounds.xy, vec2<f32>(0.0), max(screen_max - vec2<f32>(1.0), vec2<f32>(0.0)));
    let clipped_max = clamp(bounds.zw, vec2<f32>(0.0), max(screen_max - vec2<f32>(1.0), vec2<f32>(0.0)));
    let min_cell = vec2<u32>(clipped_min) / 16u;
    let max_cell = vec2<u32>(clipped_max) / 16u;
    return vec4<u32>(min_cell, max_cell);
}

// One invocation computes the exact rectangle-overlap count for one painter-ordered instance.
// The reusable hierarchical scan turns these counts into race-free, painter-ordered pair ranges.
@compute @workgroup_size(64)
fn count_instance_pairs(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let instance_idx = global_id.x;
    if (instance_idx >= uniforms.instanceCount) {
        return;
    }
    let cell_range = covered_cell_range(shape_instances[instance_idx]);
    if (cell_range.x > cell_range.z || cell_range.y > cell_range.w) {
        instance_pair_counts[instance_idx] = 0u;
        return;
    }
    instance_pair_counts[instance_idx] =
        (cell_range.z - cell_range.x + 1u) * (cell_range.w - cell_range.y + 1u);
}

// Emit one (cell, instance) record per exact overlap. Instance scan offsets preserve the input
// painter order before sorting; row-major enumeration makes the output deterministic as well.
@compute @workgroup_size(64)
fn emit_overlap_pairs(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let instance_idx = global_id.x;
    if (instance_idx >= uniforms.instanceCount) {
        return;
    }
    let cell_range = covered_cell_range(shape_instances[instance_idx]);
    if (cell_range.x > cell_range.z || cell_range.y > cell_range.w) {
        return;
    }
    var output_idx = instance_pair_offsets[instance_idx];
    for (var cell_y = cell_range.y; cell_y <= cell_range.w; cell_y = cell_y + 1u) {
        for (var cell_x = cell_range.x; cell_x <= cell_range.z; cell_x = cell_x + 1u) {
            if (output_idx < uniforms.pairCount) {
                pair_cell_keys_a[output_idx] = cell_y * uniforms.gridStride + cell_x;
                cell_shape_indices[output_idx] = instance_idx;
            }
            output_idx = output_idx + 1u;
        }
    }
}

fn radix_source_key(pair_idx: u32) -> u32 {
    if (radix_params.source_index == 0u) {
        return pair_cell_keys_a[pair_idx];
    }
    return pair_cell_keys_b[pair_idx];
}

// One workgroup builds all 256 digit counts for one consecutive 256-pair block. Counts are
// written digit-major, so one ordinary global exclusive scan produces both each digit's base and
// every prior block's contribution for that digit.
@compute @workgroup_size(256)
fn radix_histogram(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) workgroup_id: vec3<u32>) {
    atomicStore(&radix_workgroup_histogram[local_id.x], 0u);
    workgroupBarrier();

    let pair_idx = global_id.x;
    if (pair_idx < radix_params.pair_count) {
        let digit = (radix_source_key(pair_idx) >> radix_params.shift) & 255u;
        atomicAdd(&radix_workgroup_histogram[digit], 1u);
    }
    workgroupBarrier();

    let digit = local_id.x;
    let histogram_idx = digit * radix_params.block_count + workgroup_id.x;
    radix_histogram_counts[histogram_idx] = atomicLoad(&radix_workgroup_histogram[digit]);
}

// Stable scatter uses the scanned digit/block base plus a fixed local painter-order rank. The
// rank loop has a constant upper bound of 255 comparisons, avoiding subgroup feature dependence.
@compute @workgroup_size(256)
fn radix_scatter(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) workgroup_id: vec3<u32>) {
    let pair_idx = global_id.x;
    var digit = 256u;
    if (pair_idx < radix_params.pair_count) {
        digit = (radix_source_key(pair_idx) >> radix_params.shift) & 255u;
    }
    radix_workgroup_digits[local_id.x] = digit;
    workgroupBarrier();

    if (pair_idx >= radix_params.pair_count) {
        return;
    }
    var local_rank = 0u;
    for (var prior = 0u; prior < local_id.x; prior = prior + 1u) {
        local_rank = local_rank + select(0u, 1u, radix_workgroup_digits[prior] == digit);
    }
    let histogram_idx = digit * radix_params.block_count + workgroup_id.x;
    let output_idx = radix_histogram_offsets[histogram_idx] + local_rank;
    let key = radix_source_key(pair_idx);
    if (radix_params.source_index == 0u) {
        pair_cell_keys_b[output_idx] = key;
        pair_instance_indices_b[output_idx] = cell_shape_indices[pair_idx];
    } else {
        pair_cell_keys_a[output_idx] = key;
        cell_shape_indices[output_idx] = pair_instance_indices_b[pair_idx];
    }
}

// Grid initialization is a separate bounded dispatch so pair-run endpoints can write exact cell
// ranges without atomics. Dispatch ordering provides the storage dependency.
@compute @workgroup_size(256)
fn clear_grid_cells(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let cell_idx = global_id.x;
    if (cell_idx < uniforms.cellCount) {
        grid_cells[cell_idx] = GridCell(0u, 0u);
    }
}

// The fourth radix pass leaves cell keys in A and instance indices in the common painter list.
// One invocation per run end finds its matching start by lower-bound and writes the complete cell
// record exactly once, avoiding cross-invocation start/count races in a single dispatch.
@compute @workgroup_size(256)
fn build_grid_cells_from_pairs(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let pair_idx = global_id.x;
    if (pair_idx >= uniforms.pairCount) {
        return;
    }
    let cell_idx = pair_cell_keys_a[pair_idx];
    if (pair_idx + 1u < uniforms.pairCount && pair_cell_keys_a[pair_idx + 1u] == cell_idx) {
        return;
    }
    var low = 0u;
    var high = pair_idx + 1u;
    while (low < high) {
        let middle = low + (high - low) / 2u;
        if (pair_cell_keys_a[middle] < cell_idx) {
            low = middle + 1u;
        } else {
            high = middle;
        }
    }
    grid_cells[cell_idx] = GridCell(low, pair_idx + 1u - low);
}

// Fixed workgroup size: 64. One invocation transforms one instance and atomically sets its
// painter-order bit in every covered cell. Work is O(I + O), not O(G*I). Atomic OR is used only
// to build a set: the later word/bit traversal defines deterministic painter order.
@compute @workgroup_size(64)
fn build_bin_coverage(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let instance_idx = global_id.x;
    if (instance_idx >= uniforms.instanceCount) {
        return;
    }

    let bounds = transformed_device_bounds(shape_instances[instance_idx]);
    let screen_max = vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight));
    if (any(bounds.zw < vec2<f32>(0.0)) || any(bounds.xy > screen_max)) {
        return;
    }

    let grid_rows = (uniforms.screenHeight + 15u) / 16u;
    let clipped_min = clamp(bounds.xy, vec2<f32>(0.0), max(screen_max - vec2<f32>(1.0), vec2<f32>(0.0)));
    let clipped_max = clamp(bounds.zw, vec2<f32>(0.0), max(screen_max - vec2<f32>(1.0), vec2<f32>(0.0)));
    let min_cell = vec2<u32>(clipped_min) / 16u;
    let max_cell = vec2<u32>(clipped_max) / 16u;
    let instance_word = instance_idx / 32u;
    let instance_bit = 1u << (instance_idx & 31u);

    for (var cell_y = min_cell.y; cell_y <= max_cell.y && cell_y < grid_rows; cell_y = cell_y + 1u) {
        for (var cell_x = min_cell.x; cell_x <= max_cell.x && cell_x < uniforms.gridStride; cell_x = cell_x + 1u) {
            let cell_idx = cell_y * uniforms.gridStride + cell_x;
            let word_idx = cell_idx * uniforms.wordsPerCell + instance_word;
            atomicOr(&cell_coverage_words[word_idx], instance_bit);
        }
    }
}

// Fixed workgroup size: 256. Each invocation popcounts one 32-instance word and writes the
// reusable exclusive-scan input. The following scan reserves exact output without a hard cap.
@compute @workgroup_size(256)
fn count_bin_words(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let word_idx = global_id.x;
    if (word_idx >= uniforms.coverageWordCount) {
        return;
    }
    bin_word_counts[word_idx] = countOneBits(atomicLoad(&cell_coverage_words[word_idx]));
}

// Fixed workgroup size: 256. Words are laid out cell-major and instance-word-minor. The scan
// offset plus ascending bit enumeration therefore produces exact, stable painter-order lists.
// Bandwidth is O(W + O): one word/count/offset read and one index write per overlap.
@compute @workgroup_size(256)
fn scatter_bin_words(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let word_idx = global_id.x;
    if (word_idx >= uniforms.coverageWordCount) {
        return;
    }

    var bits = atomicLoad(&cell_coverage_words[word_idx]);
    var output_idx = bin_word_offsets[word_idx];
    let instance_base = (word_idx % uniforms.wordsPerCell) * 32u;
    while (bits != 0u) {
        let bit = firstTrailingBit(bits);
        let instance_idx = instance_base + bit;
        if (instance_idx < uniforms.instanceCount && output_idx < uniforms.pairCount) {
            cell_shape_indices[output_idx] = instance_idx;
        }
        output_idx = output_idx + 1u;
        bits = bits & (bits - 1u);
    }

    if ((word_idx % uniforms.wordsPerCell) == 0u) {
        let cell_idx = word_idx / uniforms.wordsPerCell;
        let cell_start = bin_word_offsets[word_idx];
        var cell_end = uniforms.pairCount;
        if (cell_idx + 1u < uniforms.cellCount) {
            cell_end = bin_word_offsets[word_idx + uniforms.wordsPerCell];
        }
        grid_cells[cell_idx].shape_start_offset = cell_start;
        grid_cells[cell_idx].shape_count = cell_end - cell_start;
    }
}

// Fixed workgroup size: 256. One invocation classifies one cell after exact bin scatter.
// Bandwidth: one GridCell read and one u32 flag write per screen cell.
@compute @workgroup_size(256)
fn mark_active_cells(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let cell_idx = global_id.x;
    if (cell_idx >= uniforms.cellCount) {
        return;
    }
    active_cell_flags[cell_idx] = select(0u, 1u, grid_cells[cell_idx].shape_count != 0u);
}

// Fixed workgroup size: 256. The exclusive scan offset preserves row-major cell order.
// Bandwidth: one flag/offset read and one active-index write per non-empty cell.
@compute @workgroup_size(256)
fn scatter_active_cells(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let cell_idx = global_id.x;
    if (cell_idx >= uniforms.cellCount || active_cell_flags[cell_idx] == 0u) {
        return;
    }
    active_cell_indices[active_cell_offsets[cell_idx]] = cell_idx;
}

// Fixed workgroup size: 1. Derive the exact active count from the final flag/scan pair and
// split it over the WebGPU-guaranteed 65,535 workgroups-per-dimension limit. The count is stored
// in the sentinel slot active_cell_indices[cellCount] for final-row bounds checks.
@compute @workgroup_size(1)
fn finalize_active_dispatch(@builtin(global_invocation_id) global_id: vec3<u32>) {
    if (global_id.x != 0u) {
        return;
    }
    let last_cell = uniforms.cellCount - 1u;
    let active_count = active_cell_offsets[last_cell] + active_cell_flags[last_cell];
    active_cell_indices[uniforms.cellCount] = active_count;
    active_draw_args[0] = DrawIndirectArgs(6u, active_count, 0u, 0u);
    if (active_count == 0u) {
        active_dispatch_args[0] = DispatchIndirectArgs(0u, 1u, 1u);
        return;
    }
    let dispatch_width = min(active_count, 65535u);
    let dispatch_height = ((active_count - 1u) / dispatch_width) + 1u;
    active_dispatch_args[0] = DispatchIndirectArgs(dispatch_width, dispatch_height, 1u);
}

// One 64-lane workgroup classifies the painter-ordered candidates of one active cell. A candidate
// is solid or outside only when its center-to-outline lower bound in device pixels exceeds the
// farthest cell corner plus the half-pixel AA support. All uncertain pairs remain edge work, so the
// optimization cannot lower coverage quality or change painter order.
@compute @workgroup_size(64)
fn classify_cell_shapes(
    @builtin(workgroup_id) workgroup_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>) {
    let active_count = active_cell_indices[uniforms.cellCount];
    let dispatch_width = min(active_count, 65535u);
    let active_idx = workgroup_id.y * dispatch_width + workgroup_id.x;
    if (active_idx >= active_count) {
        return;
    }

    let cell_idx = active_cell_indices[active_idx];
    let cell_coord = vec2<u32>(cell_idx % uniforms.gridStride, cell_idx / uniforms.gridStride);
    let cell_origin = vec2<f32>(cell_coord * 16u);
    let screen_size = vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight));
    let cell_size = min(vec2<f32>(16.0), screen_size - cell_origin);
    let logical_center = (cell_origin + cell_size * 0.5) / uniforms.dpiScale;
    let safe_radius = length(cell_size * 0.5) + 0.5;
    let cell = active_grid_cell(active_idx, cell_idx);

    for (var candidate = local_id.x; candidate < cell.shape_count; candidate = candidate + 64u) {
        let pair_idx = cell.shape_start_offset + candidate;
        let instance = shape_instances[cell_shape_indices[pair_idx]];
        let inverse_transform = instance_inverse_transform(instance);
        let transform = instance_transform(instance);
        let local_center = (inverse_transform * vec4<f32>(logical_center, 0.0, 1.0)).xy;
        let evaluation = evaluate_shape(local_center, instance.bvh_root_idx);
        let lower_device_distance = evaluation.min_distance *
            minimum_device_scale(transform) * uniforms.dpiScale;

        var cell_class = 0u; // edge/uncertain
        if (lower_device_distance > safe_radius) {
            cell_class = select(1u, 2u, evaluation.winding != 0); // outside / solid
        }
        cell_shape_classes[pair_idx] = cell_class;
    }
}

@compute @workgroup_size(16, 16)
fn wavefront_render(
    @builtin(workgroup_id) workgroup_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>) {
    let active_count = active_cell_indices[uniforms.cellCount];
    let dispatch_width = min(active_count, 65535u);
    let active_idx = workgroup_id.y * dispatch_width + workgroup_id.x;
    if (active_idx >= active_count) {
        return;
    }
    let cell_idx = active_cell_indices[active_idx];
    let cell_coord = vec2<u32>(cell_idx % uniforms.gridStride, cell_idx / uniforms.gridStride);
    let pixel_coord = cell_coord * 16u + local_id.xy;
    let pixel_visible = pixel_coord.x < uniforms.screenWidth && pixel_coord.y < uniforms.screenHeight;
    var current_color = vec4<f32>(0.0);
    if (pixel_visible) {
        current_color = textureLoad(screen_texture, vec2<i32>(pixel_coord), 0);
    }

    let cell = active_grid_cell(active_idx, cell_idx);
    let logical_pos = (vec2<f32>(pixel_coord) + vec2<f32>(0.5)) / uniforms.dpiScale;
    let device_pos = vec2<f32>(pixel_coord) + vec2<f32>(0.5);
    let cell_origin = vec2<f32>(cell_coord * 16u);
    let screen_size = vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight));
    let cell_size = min(vec2<f32>(16.0), screen_size - cell_origin);
    let lane_index = local_id.y * 16u + local_id.x;

    for (var i = 0u; i < cell.shape_count; i = i + 1u) {
        let pair_idx = cell.shape_start_offset + i;
        let cell_class = cell_shape_classes[pair_idx];
        if (cell_class == 1u) {
            continue;
        }
        let instance_idx = cell_shape_indices[pair_idx];
        let instance = shape_instances[instance_idx];
        let transform = instance_transform(instance);

        if (cell_class == 2u) {
            if (pixel_visible) {
                let linear_text = pow(instance.color.rgb, vec3<f32>(2.2));
                let srgb_output = pow(linear_text, vec3<f32>(1.0 / 2.2));
                let blended_alpha = current_color.a + (1.0 - current_color.a);
                current_color = vec4<f32>(srgb_output, blended_alpha);
            }
            continue;
        }

        // Every lane reaches both barriers for an uncertain edge pair, including lanes outside a
        // partial edge tile. Solid/outside commands never enter this branch, so their established
        // constant paths do not pay workgroup synchronization cost.
        if (lane_index == 0u) {
            build_edge_command(instance.bvh_root_idx, transform, cell_origin, cell_size);
        }
        workgroupBarrier();

        if (pixel_visible) {
            let inverse_transform = instance_inverse_transform(instance);
            let local_pos_3d = inverse_transform * vec4<f32>(logical_pos, 0.0, 1.0);
            let local_pos = local_pos_3d.xy;

            var evaluation: ShapeEvaluation;
            if (edge_command_overflow == 0u) {
                evaluation = evaluate_edge_command(device_pos, local_id.y, transform);
            } else {
                evaluation = evaluate_shape(local_pos, instance.bvh_root_idx);
                evaluation.min_distance *= length(transform[0].xy) * uniforms.dpiScale;
            }
            var sd = evaluation.min_distance;
            if (evaluation.winding != 0) {
                sd = -evaluation.min_distance;
            }
            let adjusted_dist = sd - uniforms.fontWeightOffset;
            let coverage = 1.0 - smoothstep(-0.5, 0.5, adjusted_dist);

            if (coverage > 0.0) {
                let text_color = instance.color;
                let bg_color = current_color;

                let linear_text = pow(text_color.rgb, vec3<f32>(2.2));
                let linear_bg = pow(bg_color.rgb, vec3<f32>(2.2));

                let linear_blend = mix(linear_bg, linear_text, coverage);
                let srgb_output = pow(linear_blend, vec3<f32>(1.0 / 2.2));

                let blended_alpha = bg_color.a + coverage * (1.0 - bg_color.a);
                current_color = vec4<f32>(srgb_output, blended_alpha);
            }
        }
        workgroupBarrier();
    }

    if (pixel_visible) {
        textureStore(screen_texture_write, vec2<i32>(pixel_coord), current_color);
    }
}
