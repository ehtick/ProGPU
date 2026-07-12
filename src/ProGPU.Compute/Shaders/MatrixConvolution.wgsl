// Algorithm: Apply a configurable 2D convolution kernel with tile-mode and premultiplied-alpha handling.
// Time complexity: O(Kw*Kh) per output texel for kernel width Kw and height Kh.
// Space complexity: O(1) local storage with O(Kw*Kh) texture and kernel reads.
struct Params {
    kernelWidth: i32,
    kernelHeight: i32,
    kernelOffsetX: i32,
    kernelOffsetY: i32,
    gain: f32,
    bias: f32,
    tileMode: u32,
    convolveAlpha: u32,
    tileOriginX: i32,
    tileOriginY: i32,
    tileWidth: i32,
    tileHeight: i32,
};

struct Kernel {
    values: array<f32>,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> params: Params;
@group(0) @binding(3) var<storage, read> kernel: Kernel;

fn positive_modulo(value: i32, divisor: i32) -> i32 {
    let remainder = value % divisor;
    return select(remainder + divisor, remainder, remainder >= 0);
}

fn resolve_coordinate(value: i32, origin: i32, size: i32, tileMode: u32) -> i32 {
    if (tileMode == 0u) {
        return clamp(value, origin, origin + size - 1);
    }
    if (tileMode == 1u) {
        return origin + positive_modulo(value - origin, size);
    }
    if (tileMode == 2u) {
        let period = size * 2;
        let mirrored = positive_modulo(value - origin, period);
        return origin + select(period - mirrored - 1, mirrored, mirrored < size);
    }
    return value;
}

fn sample_input(position: vec2<i32>, size: vec2<u32>) -> vec4<f32> {
    let width = i32(size.x);
    let height = i32(size.y);
    var resolved = position;
    if (params.tileMode == 3u) {
        if (position.x < 0 || position.y < 0 || position.x >= width || position.y >= height) {
            return vec4<f32>(0.0);
        }
    } else {
        if (params.tileWidth <= 0 || params.tileHeight <= 0) {
            return vec4<f32>(0.0);
        }
        resolved = vec2<i32>(
            resolve_coordinate(position.x, params.tileOriginX, params.tileWidth, params.tileMode),
            resolve_coordinate(position.y, params.tileOriginY, params.tileHeight, params.tileMode));
        if (resolved.x < 0 || resolved.y < 0 || resolved.x >= width || resolved.y >= height) {
            return vec4<f32>(0.0);
        }
    }
    return textureLoad(inputTex, resolved, 0);
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    if (id.x >= size.x || id.y >= size.y) {
        return;
    }

    let pixel = vec2<i32>(id.xy);
    let kernelWidth = clamp(params.kernelWidth, 1, 64);
    let kernelHeight = clamp(params.kernelHeight, 1, 64);
    var accumulated = vec4<f32>(0.0);
    for (var kernelY = 0; kernelY < kernelHeight; kernelY = kernelY + 1) {
        for (var kernelX = 0; kernelX < kernelWidth; kernelX = kernelX + 1) {
            var sampleColor = sample_input(
                pixel + vec2<i32>(kernelX - params.kernelOffsetX, kernelY - params.kernelOffsetY),
                size);
            if (params.convolveAlpha == 0u) {
                sampleColor = vec4<f32>(
                    select(vec3<f32>(0.0), sampleColor.rgb / max(sampleColor.a, 0.000001), sampleColor.a > 0.0),
                    sampleColor.a);
            }
            let weight = kernel.values[u32(kernelY * kernelWidth + kernelX)];
            accumulated = accumulated + sampleColor * weight;
        }
    }

    let normalizedBias = params.bias / 255.0;
    if (params.convolveAlpha != 0u) {
        var result = clamp(
            accumulated * params.gain + vec4<f32>(normalizedBias),
            vec4<f32>(0.0),
            vec4<f32>(1.0));
        result = vec4<f32>(min(result.rgb, vec3<f32>(result.a)), result.a);
        textureStore(outputTex, pixel, result);
        return;
    }

    let sourceAlpha = textureLoad(inputTex, pixel, 0).a;
    let straightRgb = clamp(
        accumulated.rgb * params.gain + vec3<f32>(normalizedBias),
        vec3<f32>(0.0),
        vec3<f32>(1.0));
    textureStore(outputTex, pixel, vec4<f32>(straightRgb * sourceAlpha, sourceAlpha));
}
