// Algorithm: Draw one two-triangle quad per compacted active 16x16 cell and replace the matching pixels in the already rendered base target with the sparse compute result.
// Time complexity: O(Pa) fragment work and O(A) vertex setup for Pa pixels across A active cells; the draw count is supplied indirectly without CPU readback.
// Space complexity: O(1) private shader storage and O(Pa) texture-read/render-target bandwidth; inactive pixels and cells are not sampled or written.
// Fixed topology: six generated vertices form two triangles per instance. Pixel coordinates are
// reconstructed from the compacted row-major cell index. The final partial row/column is clamped
// to the physical target dimensions, and textureLoad preserves exact one-to-one pixel values.
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
    pad1: u32,
    pad2: u32,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> active_cell_indices: array<u32>;
@group(0) @binding(2) var sparse_texture: texture_2d<f32>;

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) pixel_position: vec2<f32>,
};

@vertex
fn vs_sparse_cell(
    @builtin(vertex_index) vertex_index: u32,
    @builtin(instance_index) instance_index: u32) -> VertexOutput {
    // Express the generated-quad lookup as scalar selects for compatibility with WebGPU
    // implementations that conservatively reject dynamic indexing into function arrays.
    let corner = vec2<f32>(
        select(0.0, 1.0, vertex_index == 1u || vertex_index == 2u || vertex_index == 4u),
        select(0.0, 1.0, vertex_index == 2u || vertex_index == 4u || vertex_index == 5u));
    let cell_idx = active_cell_indices[instance_index];
    let cell_coord = vec2<u32>(cell_idx % uniforms.gridStride, cell_idx / uniforms.gridStride);
    let cell_min = vec2<f32>(cell_coord * 16u);
    let cell_max = min(
        cell_min + vec2<f32>(16.0),
        vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight)));
    let pixel_position = mix(cell_min, cell_max, corner);
    let target_size = vec2<f32>(f32(uniforms.screenWidth), f32(uniforms.screenHeight));
    let ndc = vec2<f32>(
        pixel_position.x / target_size.x * 2.0 - 1.0,
        1.0 - pixel_position.y / target_size.y * 2.0);
    return VertexOutput(vec4<f32>(ndc, 0.0, 1.0), pixel_position);
}

@fragment
fn fs_sparse_cell(input: VertexOutput) -> @location(0) vec4<f32> {
    let pixel = min(
        vec2<u32>(input.pixel_position),
        vec2<u32>(uniforms.screenWidth - 1u, uniforms.screenHeight - 1u));
    return textureLoad(sparse_texture, vec2<i32>(pixel), 0);
}
