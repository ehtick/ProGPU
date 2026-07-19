// Algorithm: Finish equal-radius Gaussian color and drop-shadow blurs in one vertical pass,
// sharing Gaussian evaluation while retaining transparent color and clamp-to-edge shadow borders.
// Time complexity: O(R) per output texel for radius R (fixed maximum 64), versus two O(R) passes;
// each loop evaluates one coefficient shared by both effect accumulators.
// Space complexity: O(1) private storage, 2(2R+1) texture loads, and two writes.
struct Params {
    offset: vec2<f32>,
    padding0: vec2<f32>,
    color: vec4<f32>,
    sigma: f32,
    radius: u32,
    padding1: vec2<u32>,
};

@group(0) @binding(0) var blurInputTex: texture_2d<f32>;
@group(0) @binding(1) var shadowInputTex: texture_2d<f32>;
@group(0) @binding(2) var blurOutputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(3) var shadowOutputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(4) var<uniform> params: Params;

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(blurInputTex);
    let x = i32(id.x);
    let y = i32(id.y);
    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    let radius = i32(min(params.radius, 64u));
    let inverseVariance = 0.5 / (params.sigma * params.sigma);
    var blurSum = textureLoad(blurInputTex, vec2<i32>(x, y), 0);
    var shadowSum = textureLoad(shadowInputTex, vec2<i32>(x, y), 0);
    var weightSum = 1.0;

    for (var offset = 1; offset <= radius; offset = offset + 1) {
        let distance = f32(offset);
        let weight = exp(-(distance * distance) * inverseVariance);
        let negativeY = y - offset;
        let positiveY = y + offset;
        let clampedNegativeY = clamp(negativeY, 0, i32(size.y) - 1);
        let clampedPositiveY = clamp(positiveY, 0, i32(size.y) - 1);
        let negativeBlur = select(
            vec4<f32>(0.0),
            textureLoad(blurInputTex, vec2<i32>(x, clampedNegativeY), 0),
            negativeY >= 0);
        let positiveBlur = select(
            vec4<f32>(0.0),
            textureLoad(blurInputTex, vec2<i32>(x, clampedPositiveY), 0),
            positiveY < i32(size.y));
        blurSum += (negativeBlur + positiveBlur) * weight;
        shadowSum += (textureLoad(shadowInputTex, vec2<i32>(x, clampedNegativeY), 0) +
            textureLoad(shadowInputTex, vec2<i32>(x, clampedPositiveY), 0)) * weight;
        weightSum += 2.0 * weight;
    }

    textureStore(blurOutputTex, vec2<i32>(x, y), blurSum / weightSum);
    textureStore(shadowOutputTex, vec2<i32>(x, y), shadowSum / max(weightSum, 0.0001));
}
