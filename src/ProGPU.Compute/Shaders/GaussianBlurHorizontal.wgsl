// Algorithm: Convolve each row with a truncated normalized Gaussian kernel.
// Time complexity: O(R) per output texel for blur radius R.
// Space complexity: O(1) local storage with O(R) texture reads.
struct Params {
    sigma: f32,
    radius: u32,
    padding0: u32,
    padding1: u32,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> blurParams: Params;

fn sample_input(x: i32, y: i32, size: vec2<u32>) -> vec4<f32> {
    if (x < 0 || y < 0 || x >= i32(size.x) || y >= i32(size.y)) {
        return vec4<f32>(0.0);
    }
    return textureLoad(inputTex, vec2<i32>(x, y), 0);
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    let x = i32(id.x);
    let y = i32(id.y);

    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    if (blurParams.sigma <= 0.0001 || blurParams.radius == 0u) {
        textureStore(outputTex, vec2<i32>(x, y), textureLoad(inputTex, vec2<i32>(x, y), 0));
        return;
    }

    var color = sample_input(x, y, size);
    var weightSum = 1.0;
    let inverseVariance = 0.5 / (blurParams.sigma * blurParams.sigma);
    let radius = i32(min(blurParams.radius, 128u));
    for (var offset = 1; offset <= radius; offset = offset + 1) {
        let distance = f32(offset);
        let weight = exp(-(distance * distance) * inverseVariance);
        color = color +
            (sample_input(x - offset, y, size) + sample_input(x + offset, y, size)) * weight;
        weightSum = weightSum + 2.0 * weight;
    }

    textureStore(outputTex, vec2<i32>(x, y), color / weightSum);
}
