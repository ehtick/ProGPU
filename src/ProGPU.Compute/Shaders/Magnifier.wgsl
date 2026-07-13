// Algorithm: Apply Skia's bounded magnifier lens by mixing each output coordinate with its zoom transform, using squared inset-distance weighting and circular 2x-inset corner transitions before nearest, bilinear, or bicubic sampling.
// Time complexity: O(1) per output texel with one, four, or sixteen source reads according to the sampling mode.
// Space complexity: O(1) local storage per invocation with one source texture and one output texture.
struct Params {
    lensBounds: vec4<f32>,
    outputBounds: vec4<f32>,
    zoomTransform: vec4<f32>,
    inverseInset: vec2<f32>,
    samplingMode: u32,
    padding0: u32,
    cubic: vec2<f32>,
    padding1: vec2<f32>,
};

@group(0) @binding(0) var sourceTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> params: Params;

fn load_or_transparent(position: vec2<i32>) -> vec4<f32> {
    let size = textureDimensions(sourceTex);
    if (position.x < 0 || position.y < 0 ||
        position.x >= i32(size.x) || position.y >= i32(size.y)) {
        return vec4<f32>(0.0);
    }
    return textureLoad(sourceTex, position, 0);
}

fn sample_nearest(coord: vec2<f32>) -> vec4<f32> {
    return load_or_transparent(vec2<i32>(floor(coord)));
}

fn sample_linear(coord: vec2<f32>) -> vec4<f32> {
    let pixel = coord - vec2<f32>(0.5);
    let base = vec2<i32>(floor(pixel));
    let fraction = fract(pixel);
    let top = mix(
        load_or_transparent(base),
        load_or_transparent(base + vec2<i32>(1, 0)),
        fraction.x);
    let bottom = mix(
        load_or_transparent(base + vec2<i32>(0, 1)),
        load_or_transparent(base + vec2<i32>(1, 1)),
        fraction.x);
    return mix(top, bottom, fraction.y);
}

fn cubic_weight(distance: f32, b: f32, c: f32) -> f32 {
    let x = abs(distance);
    if (x < 1.0) {
        return ((12.0 - 9.0 * b - 6.0 * c) * x * x * x +
                (-18.0 + 12.0 * b + 6.0 * c) * x * x +
                (6.0 - 2.0 * b)) / 6.0;
    }
    if (x < 2.0) {
        return ((-b - 6.0 * c) * x * x * x +
                (6.0 * b + 30.0 * c) * x * x +
                (-12.0 * b - 48.0 * c) * x +
                (8.0 * b + 24.0 * c)) / 6.0;
    }
    return 0.0;
}

fn sample_cubic(coord: vec2<f32>) -> vec4<f32> {
    let pixel = coord - vec2<f32>(0.5);
    let base = vec2<i32>(floor(pixel));
    var color = vec4<f32>(0.0);
    var weightSum = 0.0;
    for (var y = -1; y <= 2; y = y + 1) {
        let wy = cubic_weight(pixel.y - f32(base.y + y), params.cubic.x, params.cubic.y);
        for (var x = -1; x <= 2; x = x + 1) {
            let wx = cubic_weight(pixel.x - f32(base.x + x), params.cubic.x, params.cubic.y);
            let weight = wx * wy;
            color += load_or_transparent(base + vec2<i32>(x, y)) * weight;
            weightSum += weight;
        }
    }
    return select(vec4<f32>(0.0), color / weightSum, abs(weightSum) > 1e-6);
}

fn sample_source(coord: vec2<f32>) -> vec4<f32> {
    switch params.samplingMode {
        case 0u: { return sample_nearest(coord); }
        case 2u: { return sample_cubic(coord); }
        default: { return sample_linear(coord); }
    }
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let outputSize = textureDimensions(outputTex);
    if (id.x >= outputSize.x || id.y >= outputSize.y) {
        return;
    }

    let coord = vec2<f32>(id.xy) + vec2<f32>(0.5);
    let outputBounds = params.outputBounds;
    if (coord.x < outputBounds.x || coord.y < outputBounds.y ||
        coord.x >= outputBounds.z || coord.y >= outputBounds.w) {
        textureStore(outputTex, vec2<i32>(id.xy), vec4<f32>(0.0));
        return;
    }

    let lens = params.lensBounds;
    let zoomCoord = params.zoomTransform.xy + params.zoomTransform.zw * coord;
    var weight = 1.0;
    if (params.inverseInset.x > 0.0 && params.inverseInset.y > 0.0) {
        let edgeInset = min(coord - lens.xy, lens.zw - coord) * params.inverseInset;
        if (edgeInset.x < 2.0 && edgeInset.y < 2.0) {
            weight = 2.0 - length(vec2<f32>(2.0) - edgeInset);
        } else {
            weight = min(edgeInset.x, edgeInset.y);
        }
        weight = clamp(weight, 0.0, 1.0);
        weight *= weight;
    }

    textureStore(
        outputTex,
        vec2<i32>(id.xy),
        sample_source(mix(coord, zoomCoord, weight)));
}
