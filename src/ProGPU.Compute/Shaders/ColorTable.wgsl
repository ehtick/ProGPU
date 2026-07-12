// Algorithm: Map each unpremultiplied color channel through a 256-entry lookup table and restore premultiplied alpha.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage with one texture read, four table reads, and one write.
struct ColorTables {
    values: array<u32>,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<storage, read> tables: ColorTables;

fn table_value(channelOffset: u32, value: f32) -> f32 {
    let index = u32(round(clamp(value, 0.0, 1.0) * 255.0));
    return f32(tables.values[channelOffset + index]) / 255.0;
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    if (id.x >= size.x || id.y >= size.y) {
        return;
    }

    let pixelPosition = vec2<i32>(id.xy);
    let input = clamp(textureLoad(inputTex, pixelPosition, 0), vec4<f32>(0.0), vec4<f32>(1.0));
    var straightRgb = vec3<f32>(0.0);
    if (input.a > 0.0) {
        straightRgb = clamp(input.rgb / input.a, vec3<f32>(0.0), vec3<f32>(1.0));
    }

    let outputAlpha = table_value(768u, input.a);
    let outputRgb = vec3<f32>(
        table_value(0u, straightRgb.r),
        table_value(256u, straightRgb.g),
        table_value(512u, straightRgb.b));
    textureStore(outputTex, pixelPosition, vec4<f32>(outputRgb * outputAlpha, outputAlpha));
}
