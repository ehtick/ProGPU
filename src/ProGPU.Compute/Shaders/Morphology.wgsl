// Algorithm: Compute directional dilation or erosion over a configurable one-dimensional radius.
// Time complexity: O(R) per output texel for morphology radius R.
// Space complexity: O(1) local storage with O(R) texture reads.
struct Params {
    directionX: i32,
    directionY: i32,
    radius: u32,
    dilate: u32,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> params: Params;

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    let x = i32(id.x);
    let y = i32(id.y);
    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    var result = select(vec4<f32>(1.0), vec4<f32>(0.0), params.dilate != 0u);
    let radius = i32(min(params.radius, 128u));
    for (var offset = -radius; offset <= radius; offset = offset + 1) {
        let sampleX = clamp(x + offset * params.directionX, 0, i32(size.x) - 1);
        let sampleY = clamp(y + offset * params.directionY, 0, i32(size.y) - 1);
        let sampleColor = textureLoad(inputTex, vec2<i32>(sampleX, sampleY), 0);
        if (params.dilate != 0u) {
            result = max(result, sampleColor);
        } else {
            result = min(result, sampleColor);
        }
    }

    textureStore(outputTex, vec2<i32>(x, y), result);
}
