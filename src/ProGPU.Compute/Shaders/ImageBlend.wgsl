// Algorithm: Evaluate the selected separable or non-separable blend mode and compose premultiplied source-over output.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage with two texture reads and one write.
struct Params {
    mode: u32,
    linearRgb: u32,
    padding0: u32,
    padding1: u32,
};

@group(0) @binding(0) var backgroundTex: texture_2d<f32>;
@group(0) @binding(1) var foregroundTex: texture_2d<f32>;
@group(0) @binding(2) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(3) var<uniform> params: Params;

fn unpremultiply(color: vec3<f32>, alpha: f32) -> vec3<f32> {
    if (alpha <= 0.0) {
        return vec3<f32>(0.0);
    }
    return clamp(color / alpha, vec3<f32>(0.0), vec3<f32>(1.0));
}

fn srgb_to_linear_component(value: f32) -> f32 {
    if (value <= 0.04045) {
        return value / 12.92;
    }
    return pow((value + 0.055) / 1.055, 2.4);
}

fn linear_to_srgb_component(value: f32) -> f32 {
    if (value <= 0.0031308) {
        return value * 12.92;
    }
    return 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

fn srgb_to_linear(color: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        srgb_to_linear_component(color.r),
        srgb_to_linear_component(color.g),
        srgb_to_linear_component(color.b));
}

fn linear_to_srgb(color: vec3<f32>) -> vec3<f32> {
    let clamped = clamp(color, vec3<f32>(0.0), vec3<f32>(1.0));
    return vec3<f32>(
        linear_to_srgb_component(clamped.r),
        linear_to_srgb_component(clamped.g),
        linear_to_srgb_component(clamped.b));
}

fn screen(backdrop: vec3<f32>, source: vec3<f32>) -> vec3<f32> {
    return backdrop + source - backdrop * source;
}

fn hard_light_component(backdrop: f32, source: f32) -> f32 {
    if (source <= 0.5) {
        return backdrop * (2.0 * source);
    }
    return backdrop + (2.0 * source - 1.0) - backdrop * (2.0 * source - 1.0);
}

fn hard_light(backdrop: vec3<f32>, source: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        hard_light_component(backdrop.r, source.r),
        hard_light_component(backdrop.g, source.g),
        hard_light_component(backdrop.b, source.b));
}

fn color_dodge_component(backdrop: f32, source: f32) -> f32 {
    if (backdrop <= 0.0) {
        return 0.0;
    }
    if (source >= 1.0) {
        return 1.0;
    }
    return min(1.0, backdrop / (1.0 - source));
}

fn color_dodge(backdrop: vec3<f32>, source: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        color_dodge_component(backdrop.r, source.r),
        color_dodge_component(backdrop.g, source.g),
        color_dodge_component(backdrop.b, source.b));
}

fn color_burn_component(backdrop: f32, source: f32) -> f32 {
    if (backdrop >= 1.0) {
        return 1.0;
    }
    if (source <= 0.0) {
        return 0.0;
    }
    return 1.0 - min(1.0, (1.0 - backdrop) / source);
}

fn color_burn(backdrop: vec3<f32>, source: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        color_burn_component(backdrop.r, source.r),
        color_burn_component(backdrop.g, source.g),
        color_burn_component(backdrop.b, source.b));
}

fn soft_light_component(backdrop: f32, source: f32) -> f32 {
    if (source <= 0.5) {
        return backdrop - (1.0 - 2.0 * source) * backdrop * (1.0 - backdrop);
    }
    var curve = sqrt(backdrop);
    if (backdrop <= 0.25) {
        curve = ((16.0 * backdrop - 12.0) * backdrop + 4.0) * backdrop;
    }
    return backdrop + (2.0 * source - 1.0) * (curve - backdrop);
}

fn soft_light(backdrop: vec3<f32>, source: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        soft_light_component(backdrop.r, source.r),
        soft_light_component(backdrop.g, source.g),
        soft_light_component(backdrop.b, source.b));
}

fn luminosity(color: vec3<f32>) -> f32 {
    return dot(color, vec3<f32>(0.3, 0.59, 0.11));
}

fn saturation(color: vec3<f32>) -> f32 {
    return max(max(color.r, color.g), color.b) - min(min(color.r, color.g), color.b);
}

fn clip_color(input: vec3<f32>) -> vec3<f32> {
    var color = input;
    let lightness = luminosity(color);
    let minimum = min(min(color.r, color.g), color.b);
    let maximum = max(max(color.r, color.g), color.b);
    if (minimum < 0.0 && lightness > minimum) {
        color = vec3<f32>(lightness) +
            (color - vec3<f32>(lightness)) * lightness / (lightness - minimum);
    }
    if (maximum > 1.0 && maximum > lightness) {
        color = vec3<f32>(lightness) +
            (color - vec3<f32>(lightness)) * (1.0 - lightness) / (maximum - lightness);
    }
    return color;
}

fn set_luminosity(color: vec3<f32>, lightness: f32) -> vec3<f32> {
    return clip_color(color + vec3<f32>(lightness - luminosity(color)));
}

fn set_saturation(color: vec3<f32>, targetSaturation: f32) -> vec3<f32> {
    let minimum = min(min(color.r, color.g), color.b);
    let maximum = max(max(color.r, color.g), color.b);
    if (maximum <= minimum) {
        return vec3<f32>(0.0);
    }
    return (color - vec3<f32>(minimum)) * targetSaturation / (maximum - minimum);
}

fn blend_rgb(backdrop: vec3<f32>, source: vec3<f32>, mode: u32) -> vec3<f32> {
    switch mode {
        case 11u: { return backdrop * source; }
        case 12u: { return screen(backdrop, source); }
        case 13u: { return min(backdrop, source); }
        case 14u: { return max(backdrop, source); }
        case 15u: { return backdrop + source - 2.0 * backdrop * source; }
        case 18u: { return hard_light(source, backdrop); }
        case 19u: { return color_dodge(backdrop, source); }
        case 20u: { return color_burn(backdrop, source); }
        case 21u: { return hard_light(backdrop, source); }
        case 22u: { return soft_light(backdrop, source); }
        case 23u: { return abs(backdrop - source); }
        case 24u: {
            return set_luminosity(
                set_saturation(source, saturation(backdrop)),
                luminosity(backdrop));
        }
        case 25u: {
            return set_luminosity(
                set_saturation(backdrop, saturation(source)),
                luminosity(backdrop));
        }
        case 26u: { return set_luminosity(source, luminosity(backdrop)); }
        case 27u: { return set_luminosity(backdrop, luminosity(source)); }
        default: { return source; }
    }
}

fn compose(
    backdrop: vec3<f32>,
    backdropAlpha: f32,
    source: vec3<f32>,
    sourceAlpha: f32,
    mode: u32) -> vec4<f32> {
    let backdropPremul = backdrop * backdropAlpha;
    let sourcePremul = source * sourceAlpha;
    switch mode {
        case 1u: { return vec4<f32>(sourcePremul, sourceAlpha); }
        case 2u: { return vec4<f32>(backdropPremul, backdropAlpha); }
        case 3u: { return vec4<f32>(sourcePremul * backdropAlpha, sourceAlpha * backdropAlpha); }
        case 4u: { return vec4<f32>(backdropPremul * sourceAlpha, backdropAlpha * sourceAlpha); }
        case 5u: {
            return vec4<f32>(sourcePremul * (1.0 - backdropAlpha), sourceAlpha * (1.0 - backdropAlpha));
        }
        case 6u: {
            return vec4<f32>(backdropPremul * (1.0 - sourceAlpha), backdropAlpha * (1.0 - sourceAlpha));
        }
        case 7u: {
            return vec4<f32>(
                sourcePremul * backdropAlpha + backdropPremul * (1.0 - sourceAlpha),
                backdropAlpha);
        }
        case 8u: {
            return vec4<f32>(
                backdropPremul * sourceAlpha + sourcePremul * (1.0 - backdropAlpha),
                sourceAlpha);
        }
        case 9u: {
            return vec4<f32>(
                sourcePremul * (1.0 - backdropAlpha) + backdropPremul * (1.0 - sourceAlpha),
                sourceAlpha * (1.0 - backdropAlpha) + backdropAlpha * (1.0 - sourceAlpha));
        }
        case 10u: {
            return vec4<f32>(
                backdropPremul + sourcePremul * (1.0 - backdropAlpha),
                backdropAlpha + sourceAlpha * (1.0 - backdropAlpha));
        }
        case 16u: {
            return min(
                vec4<f32>(sourcePremul + backdropPremul, sourceAlpha + backdropAlpha),
                vec4<f32>(1.0));
        }
        case 17u: { return vec4<f32>(0.0); }
        default: {
            if (mode >= 11u) {
                let mixed = clamp(blend_rgb(backdrop, source, mode), vec3<f32>(0.0), vec3<f32>(1.0));
                return vec4<f32>(
                    sourcePremul * (1.0 - backdropAlpha) +
                        backdropPremul * (1.0 - sourceAlpha) +
                        mixed * sourceAlpha * backdropAlpha,
                    sourceAlpha + backdropAlpha - sourceAlpha * backdropAlpha);
            }
            return vec4<f32>(
                sourcePremul + backdropPremul * (1.0 - sourceAlpha),
                sourceAlpha + backdropAlpha * (1.0 - sourceAlpha));
        }
    }
}

fn encode_premultiplied(color: vec4<f32>, linearRgb: bool) -> vec4<f32> {
    let alpha = clamp(color.a, 0.0, 1.0);
    if (alpha <= 0.0) {
        return vec4<f32>(0.0);
    }
    let straight = clamp(color.rgb / alpha, vec3<f32>(0.0), vec3<f32>(1.0));
    let encoded = select(straight, linear_to_srgb(straight), linearRgb);
    return vec4<f32>(encoded * alpha, alpha);
}

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
        background = clamp(textureLoad(backgroundTex, pixel, 0), vec4<f32>(0.0), vec4<f32>(1.0));
    }
    if (id.x < foregroundSize.x && id.y < foregroundSize.y) {
        foreground = clamp(textureLoad(foregroundTex, pixel, 0), vec4<f32>(0.0), vec4<f32>(1.0));
    }
    if (params.mode == 1u) {
        textureStore(outputTex, pixel, foreground);
        return;
    }
    if (params.mode == 2u) {
        textureStore(outputTex, pixel, background);
        return;
    }
    if (params.mode == 17u) {
        textureStore(outputTex, pixel, vec4<f32>(0.0));
        return;
    }

    var backdropColor = unpremultiply(background.rgb, background.a);
    var sourceColor = unpremultiply(foreground.rgb, foreground.a);
    let useLinearRgb = params.linearRgb != 0u;
    if (useLinearRgb) {
        backdropColor = srgb_to_linear(backdropColor);
        sourceColor = srgb_to_linear(sourceColor);
    }
    let result = compose(
        backdropColor,
        background.a,
        sourceColor,
        foreground.a,
        params.mode);
    textureStore(outputTex, pixel, encode_premultiplied(result, useLinearRgb));
}
