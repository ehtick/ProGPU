// Algorithm: Transform batched image/lattice quads, emit fixed-color lattice cells without sampling, or sample nearest, linear, or Mitchell-Netravali cubic kernels before compositing.
// Time complexity: O(1) per invocation; fixed-color cells perform no image sample and cubic filtering performs a fixed 4x4 sample footprint.
// Space complexity: O(1) local storage and O(1) bounded texture bandwidth per fragment; one indexed batch stores four vertices and six indices per visible lattice cell.
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>,
    @location(3) patchKind: f32,
    @location(4) cubicResampler: vec2<f32>,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>,
    @location(2) @interpolate(flat) cubicResampler: vec2<f32>,
    @location(3) @interpolate(flat) patchKind: f32,
};

struct Uniforms {
    projection: mat4x4<f32>,
    mvp: mat4x4<f32>,
    view: mat4x4<f32>,
    canvasSize: vec2<f32>,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    var pos = input.position;
    output.position = uniforms.projection * vec4<f32>(pos, 0.0, 1.0);
    output.color = input.color;
    output.texCoord = input.texCoord;
    output.cubicResampler = input.cubicResampler;
    output.patchKind = input.patchKind;
    return output;
}

    @group(1) @binding(0) var texSampler: sampler;
@group(1) @binding(1) var texTexture: texture_2d<f32>;
@group(2) @binding(0) var maskSampler: sampler;
@group(2) @binding(1) var maskTexture: texture_2d<f32>;

fn cubic_weight(x: f32, b: f32, c: f32) -> f32 {
    let ax = abs(x);
    let ax2 = ax * ax;
    let ax3 = ax2 * ax;

    if (b == 0.0 && c == 0.5) {
        let a = -0.5;
        if (ax <= 1.0) {
            return ((a + 2.0) * ax3) - ((a + 3.0) * ax2) + 1.0;
        }
        if (ax < 2.0) {
            return (a * ax3) - (5.0 * a * ax2) + (8.0 * a * ax) - (4.0 * a);
        }
        return 0.0;
    }

    if (ax <= 1.0) {
        return ((12.0 - 9.0 * b - 6.0 * c) * ax3
            + (-18.0 + 12.0 * b + 6.0 * c) * ax2
            + (6.0 - 2.0 * b)) / 6.0;
    }

    if (ax < 2.0) {
        return ((-b - 6.0 * c) * ax3
            + (6.0 * b + 30.0 * c) * ax2
            + (-12.0 * b - 48.0 * c) * ax
            + (8.0 * b + 24.0 * c)) / 6.0;
    }

    return 0.0;
}

fn sample_bicubic(uv: vec2<f32>, resampler: vec2<f32>) -> vec4<f32> {
    let size = textureDimensions(texTexture);
    let sizef = vec2<f32>(f32(size.x), f32(size.y));
    let texel = uv * sizef - vec2<f32>(0.5, 0.5);
    let base = floor(texel);
    let f = texel - base;
    let maxCoord = vec2<i32>(i32(size.x) - 1, i32(size.y) - 1);
    var color = vec4<f32>(0.0);
    var total = 0.0;

    for (var y: i32 = -1; y <= 2; y = y + 1) {
        let wy = cubic_weight(f.y - f32(y), resampler.x, resampler.y);
        for (var x: i32 = -1; x <= 2; x = x + 1) {
            let wx = cubic_weight(f.x - f32(x), resampler.x, resampler.y);
            let weight = wx * wy;
            let coord = clamp(
                vec2<i32>(i32(base.x) + x, i32(base.y) + y),
                vec2<i32>(0, 0),
                maxCoord);
            color = color + textureLoad(texTexture, coord, 0) * weight;
            total = total + weight;
        }
    }

    return color / max(total, 0.0001);
}

fn texture_fs_main(input: VertexOutput) -> vec4<f32> {
    let screen_uv = input.position.xy / uniforms.canvasSize;
    let maskAlpha = textureSample(maskTexture, maskSampler, screen_uv).r;
    if (maskAlpha <= 0.0) {
        discard;
    }

    // patchKind 1 carries straight fixed color; 2 carries premultiplied fixed color.
    if (input.patchKind > 0.5) {
        if (input.patchKind > 1.5) {
            return vec4<f32>(input.color.rgb * maskAlpha, input.color.a * maskAlpha);
        }
        return vec4<f32>(input.color.rgb, input.color.a * maskAlpha);
    }

    var texColor = textureSample(texTexture, texSampler, input.texCoord);
    if (input.color.a < 0.0) {
        texColor = sample_bicubic(input.texCoord, input.cubicResampler);
    }
    let opacity = abs(input.color.a);
    let sourceIsPremultiplied = input.color.g > 0.5;
    let rgbScale = input.color.r;
    let coverage = opacity * maskAlpha;
    if (sourceIsPremultiplied) {
        return vec4<f32>(texColor.rgb * rgbScale * maskAlpha, texColor.a * coverage);
    }

    return vec4<f32>(texColor.rgb * rgbScale, texColor.a * coverage);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return texture_fs_main(input);
}

@fragment
fn fs_main_premultiplied(input: VertexOutput) -> @location(0) vec4<f32> {
    let color = texture_fs_main(input);
    return vec4<f32>(color.rgb * color.a, color.a);
}

@fragment
fn fs_mask(input: VertexOutput) -> @location(0) vec4<f32> {
    let color = texture_fs_main(input);
    return vec4<f32>(color.a, 0.0, 0.0, 1.0);
}
