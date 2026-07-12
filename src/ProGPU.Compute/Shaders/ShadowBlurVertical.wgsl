// Algorithm: Vertically blur an offset source alpha mask for the drop-shadow pipeline.
// Time complexity: O(R) per output texel for blur radius R.
// Space complexity: O(1) local storage with O(R) texture reads.
struct Params {
    offset: vec2<f32>,
    color: vec4<f32>,
    blurRadius: f32,
    padding: f32,
    pad0: f32,
    pad1: f32,
    pad2: f32,
    pad3: f32,
    pad4: f32,
    pad5: f32,
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

    let sigma = max(params.blurRadius, 0.5);
    let radius = min(i32(ceil(sigma * 3.0)), 64);
    var colorSum = vec4<f32>(0.0);
    var weightSum: f32 = 0.0;
    for (var dy = -radius; dy <= radius; dy = dy + 1) {
        let sampleY = clamp(y + dy, 0, i32(size.y) - 1);
        let distance = f32(dy);
        let weight = exp(-0.5 * distance * distance / (sigma * sigma));
        colorSum += textureLoad(inputTex, vec2<i32>(x, sampleY), 0) * weight;
        weightSum += weight;
    }

    textureStore(outputTex, vec2<i32>(x, y), colorSum / max(weightSum, 0.0001));
}
