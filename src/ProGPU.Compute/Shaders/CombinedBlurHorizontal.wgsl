// Algorithm: Evaluate equal-radius Gaussian color blur and drop-shadow alpha blur together,
// sharing source loads and Gaussian evaluation while preserving each effect's border convention.
// Time complexity: O(R) per output texel for radius R (fixed maximum 64), versus two O(R) passes;
// each loop evaluates one coefficient shared by both effect accumulators.
// Space complexity: O(1) private storage, 2R+1 source loads, and two writes.
struct Params {
    offset: vec2<f32>,
    padding0: vec2<f32>,
    color: vec4<f32>,
    sigma: f32,
    radius: u32,
    padding1: vec2<u32>,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var blurOutputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var shadowOutputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(3) var<uniform> params: Params;

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    let x = i32(id.x);
    let y = i32(id.y);
    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    let radius = i32(min(params.radius, 64u));
    let inverseVariance = 0.5 / (params.sigma * params.sigma);
    let center = textureLoad(inputTex, vec2<i32>(x, y), 0);
    var colorSum = center;
    var alphaSum = center.a;
    var weightSum = 1.0;

    for (var offset = 1; offset <= radius; offset = offset + 1) {
        let distance = f32(offset);
        let weight = exp(-(distance * distance) * inverseVariance);
        let negativeX = x - offset;
        let positiveX = x + offset;
        let negative = textureLoad(inputTex, vec2<i32>(clamp(negativeX, 0, i32(size.x) - 1), y), 0);
        let positive = textureLoad(inputTex, vec2<i32>(clamp(positiveX, 0, i32(size.x) - 1), y), 0);
        let negativeColor = select(vec4<f32>(0.0), negative, negativeX >= 0);
        let positiveColor = select(vec4<f32>(0.0), positive, positiveX < i32(size.x));
        colorSum += (negativeColor + positiveColor) * weight;
        alphaSum += (negative.a + positive.a) * weight;
        weightSum += 2.0 * weight;
    }

    let blurredColor = colorSum / weightSum;
    let shadowAlpha = params.color.a * alphaSum / max(weightSum, 0.0001);
    textureStore(blurOutputTex, vec2<i32>(x, y), blurredColor);
    textureStore(
        shadowOutputTex,
        vec2<i32>(x, y),
        vec4<f32>(params.color.rgb * shadowAlpha, shadowAlpha));
}
