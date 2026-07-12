// Algorithm: Combine two premultiplied inputs with the four-coefficient SVG arithmetic-composite equation.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage with two texture reads and one write.
struct Params {
    coefficients: vec4<f32>,
    enforcePremultipliedColor: u32,
    padding0: u32,
    padding1: u32,
    padding2: u32,
};

@group(0) @binding(0) var backgroundTex: texture_2d<f32>;
@group(0) @binding(1) var foregroundTex: texture_2d<f32>;
@group(0) @binding(2) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(3) var<uniform> params: Params;

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let outputSize = textureDimensions(outputTex);
    if (id.x >= outputSize.x || id.y >= outputSize.y) {
        return;
    }

    let pixel = vec2<i32>(id.xy);
    let backgroundSize = textureDimensions(backgroundTex);
    let foregroundSize = textureDimensions(foregroundTex);
    var background = vec4<f32>(0.0);
    var foreground = vec4<f32>(0.0);
    if (id.x < backgroundSize.x && id.y < backgroundSize.y) {
        background = textureLoad(backgroundTex, pixel, 0);
    }
    if (id.x < foregroundSize.x && id.y < foregroundSize.y) {
        foreground = textureLoad(foregroundTex, pixel, 0);
    }
    let k = params.coefficients;
    var result = clamp(
        k.x * foreground * background +
        k.y * foreground +
        k.z * background +
        vec4<f32>(k.w),
        vec4<f32>(0.0),
        vec4<f32>(1.0));
    if (params.enforcePremultipliedColor != 0u) {
        result = vec4<f32>(min(result.rgb, vec3<f32>(result.a)), result.a);
    }

    textureStore(outputTex, pixel, result);
}
