// Algorithm: Use selected displacement-map channels to offset one source lookup and write the premultiplied result.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage with two texture reads and one write.
struct Params {
    transform: vec4<f32>,
    xChannel: u32,
    yChannel: u32,
    padding0: u32,
    padding1: u32,
};

@group(0) @binding(0) var sourceTex: texture_2d<f32>;
@group(0) @binding(1) var displacementTex: texture_2d<f32>;
@group(0) @binding(2) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(3) var<uniform> params: Params;

fn load_or_transparent(position: vec2<i32>, size: vec2<u32>) -> vec4<f32> {
    if (position.x < 0 || position.y < 0 || position.x >= i32(size.x) || position.y >= i32(size.y)) {
        return vec4<f32>(0.0);
    }
    return textureLoad(sourceTex, position, 0);
}

fn straight_color(color: vec4<f32>) -> vec4<f32> {
    if (color.a <= 0.0) {
        return vec4<f32>(0.0);
    }
    return vec4<f32>(clamp(color.rgb / color.a, vec3<f32>(0.0), vec3<f32>(1.0)), color.a);
}

fn select_channel(color: vec4<f32>, channel: u32) -> f32 {
    switch channel {
        case 0u: { return color.r; }
        case 1u: { return color.g; }
        case 2u: { return color.b; }
        default: { return color.a; }
    }
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let outputSize = textureDimensions(outputTex);
    if (id.x >= outputSize.x || id.y >= outputSize.y) {
        return;
    }

    let pixel = vec2<i32>(id.xy);
    let sourceSize = textureDimensions(sourceTex);
    let displacementSize = textureDimensions(displacementTex);
    var displacementSample = vec4<f32>(0.0);
    if (id.x < displacementSize.x && id.y < displacementSize.y) {
        displacementSample = textureLoad(displacementTex, pixel, 0);
    }
    let displacement = straight_color(displacementSample);
    let localOffset = vec2<f32>(
        select_channel(displacement, params.xChannel) - 0.5,
        select_channel(displacement, params.yChannel) - 0.5);
    let offset = vec2<f32>(
        localOffset.x * params.transform.x + localOffset.y * params.transform.z,
        localOffset.x * params.transform.y + localOffset.y * params.transform.w);
    let sourcePosition = vec2<f32>(id.xy) + offset;
    let sourcePixel = vec2<i32>(floor(sourcePosition + vec2<f32>(0.5)));
    textureStore(outputTex, pixel, load_or_transparent(sourcePixel, sourceSize));
}
