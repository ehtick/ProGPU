// Algorithm: Draw a fullscreen triangle and linearly downsample the previous mip level into the next level.
// Time complexity: O(1) per vertex and fragment, O(P) over P destination texels.
// Space complexity: O(1) local storage with one filtered texture sample per fragment.
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) vertexIndex: u32) -> VertexOutput {
    var positions = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>(3.0, -1.0),
        vec2<f32>(-1.0, 3.0));
    var output: VertexOutput;
    output.position = vec4<f32>(positions[vertexIndex], 0.0, 1.0);
    return output;
}

@group(0) @binding(0) var mipSampler: sampler;
@group(0) @binding(1) var sourceTexture: texture_2d<f32>;

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let sourceSize = textureDimensions(sourceTexture);
    let targetSize = vec2<f32>(
        f32(max(1u, sourceSize.x / 2u)),
        f32(max(1u, sourceSize.y / 2u)));
    let uv = input.position.xy / targetSize;
    return textureSampleLevel(sourceTexture, mipSampler, uv, 0.0);
}
