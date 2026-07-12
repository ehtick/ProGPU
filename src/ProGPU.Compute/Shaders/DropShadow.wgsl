// Algorithm: Offset a blurred alpha mask, tint it, and composite the original source over the shadow.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage with a fixed texture-read footprint.
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

    var alphaSum: f32 = 0.0;
    let r = i32(params.blurRadius);
    var count: f32 = 0.0;

    for (var dy = -r; dy <= r; dy++) {
        for (var dx = -r; dx <= r; dx++) {
            let srcX = clamp(x - dx, 0, i32(size.x) - 1);
            let srcY = clamp(y - dy, 0, i32(size.y) - 1);

            let pixel = textureLoad(inputTex, vec2<i32>(srcX, srcY), 0);
            alphaSum += pixel.a;
            count += 1.0;
        }
    }

    let avgAlpha = alphaSum / count;
    let shadowAlpha = params.color.a * avgAlpha;
    let shadowColor = vec4<f32>(params.color.rgb * shadowAlpha, shadowAlpha);

    textureStore(outputTex, vec2<i32>(x, y), shadowColor);
}
