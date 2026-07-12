namespace ProGPU.Compute;

public static class ComputeShaders
{
    public const string ImageLighting = """
struct Params {
    lightPositionAndType: vec4<f32>,
    lightTargetAndSpotExponent: vec4<f32>,
    lightColor: vec4<f32>,
    surfaceParams: vec4<f32>,
    modeParams: vec4<f32>,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> params: Params;

fn height_at(position: vec2<i32>, size: vec2<u32>) -> f32 {
    let samplePosition = vec2<i32>(
        clamp(position.x, 0, i32(size.x) - 1),
        clamp(position.y, 0, i32(size.y) - 1));
    return textureLoad(inputTex, samplePosition, 0).a * params.surfaceParams.x;
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    if (id.x >= size.x || id.y >= size.y) {
        return;
    }

    let pixel = vec2<i32>(id.xy);
    let left = height_at(pixel + vec2<i32>(-1, 0), size);
    let right = height_at(pixel + vec2<i32>(1, 0), size);
    let top = height_at(pixel + vec2<i32>(0, -1), size);
    let bottom = height_at(pixel + vec2<i32>(0, 1), size);
    let normal = normalize(vec3<f32>(-(right - left) * 0.5, -(bottom - top) * 0.5, 1.0));
    let surfacePosition = vec3<f32>(vec2<f32>(id.xy), height_at(pixel, size));

    let lightType = u32(round(params.lightPositionAndType.w));
    var lightDirection = normalize(params.lightPositionAndType.xyz);
    var attenuation = 1.0;
    if (lightType != 0u) {
        lightDirection = normalize(params.lightPositionAndType.xyz - surfacePosition);
    }
    if (lightType == 2u) {
        let lightToSurface = normalize(surfacePosition - params.lightPositionAndType.xyz);
        let spotDirection = normalize(params.lightTargetAndSpotExponent.xyz - params.lightPositionAndType.xyz);
        let coneCosine = dot(lightToSurface, spotDirection);
        let cutoffCosine = cos(radians(clamp(params.surfaceParams.w, 0.0, 90.0)));
        attenuation = select(
            0.0,
            pow(max(coneCosine, 0.0), max(params.lightTargetAndSpotExponent.w, 0.0)),
            coneCosine >= cutoffCosine);
    }

    let lightColor = clamp(params.lightColor.rgb, vec3<f32>(0.0), vec3<f32>(1.0));
    let lightingConstant = max(params.surfaceParams.y, 0.0);
    let isSpecular = params.modeParams.x > 0.5;
    if (!isSpecular) {
        let diffuse = lightingConstant * max(dot(normal, lightDirection), 0.0) * attenuation;
        textureStore(outputTex, pixel, vec4<f32>(clamp(lightColor * diffuse, vec3<f32>(0.0), vec3<f32>(1.0)), 1.0));
        return;
    }

    let halfVector = normalize(lightDirection + vec3<f32>(0.0, 0.0, 1.0));
    let specular = lightingConstant *
        pow(max(dot(normal, halfVector), 0.0), clamp(params.surfaceParams.z, 1.0, 128.0)) *
        attenuation;
    let specularColor = clamp(lightColor * specular, vec3<f32>(0.0), vec3<f32>(1.0));
    let outputAlpha = max(max(specularColor.r, specularColor.g), specularColor.b);
    textureStore(outputTex, pixel, vec4<f32>(specularColor, outputAlpha));
}
""";

    public const string MatrixConvolution = """
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
""";

    public const string DisplacementMap = """
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
""";

    public const string ArithmeticComposite = """
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
""";

    public const string ColorTable = """
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
""";

    public const string ImageBlend = """
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
""";

    public const string Morphology = @"
struct Params {
    directionX: i32,
    directionY: i32,
    radius: u32,
    dilate: u32,
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

    var result = select(vec4<f32>(1.0), vec4<f32>(0.0), params.dilate != 0u);
    let radius = i32(min(params.radius, 128u));
    for (var offset = -radius; offset <= radius; offset = offset + 1) {
        let sampleX = clamp(x + offset * params.directionX, 0, i32(size.x) - 1);
        let sampleY = clamp(y + offset * params.directionY, 0, i32(size.y) - 1);
        let sampleColor = textureLoad(inputTex, vec2<i32>(sampleX, sampleY), 0);
        if (params.dilate != 0u) {
            result = max(result, sampleColor);
        } else {
            result = min(result, sampleColor);
        }
    }

    textureStore(outputTex, vec2<i32>(x, y), result);
}
";

    public const string GaussianBlurHorizontal = @"
struct Params {
    sigma: f32,
    radius: u32,
    padding0: u32,
    padding1: u32,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> blurParams: Params;

fn sample_input(x: i32, y: i32, size: vec2<u32>) -> vec4<f32> {
    if (x < 0 || y < 0 || x >= i32(size.x) || y >= i32(size.y)) {
        return vec4<f32>(0.0);
    }
    return textureLoad(inputTex, vec2<i32>(x, y), 0);
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    let x = i32(id.x);
    let y = i32(id.y);

    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    if (blurParams.sigma <= 0.0001 || blurParams.radius == 0u) {
        textureStore(outputTex, vec2<i32>(x, y), textureLoad(inputTex, vec2<i32>(x, y), 0));
        return;
    }

    var color = sample_input(x, y, size);
    var weightSum = 1.0;
    let inverseVariance = 0.5 / (blurParams.sigma * blurParams.sigma);
    let radius = i32(min(blurParams.radius, 128u));
    for (var offset = 1; offset <= radius; offset = offset + 1) {
        let distance = f32(offset);
        let weight = exp(-(distance * distance) * inverseVariance);
        color = color +
            (sample_input(x - offset, y, size) + sample_input(x + offset, y, size)) * weight;
        weightSum = weightSum + 2.0 * weight;
    }

    textureStore(outputTex, vec2<i32>(x, y), color / weightSum);
}
";

    public const string GaussianBlurVertical = @"
struct Params {
    sigma: f32,
    radius: u32,
    padding0: u32,
    padding1: u32,
};

@group(0) @binding(0) var inputTex: texture_2d<f32>;
@group(0) @binding(1) var outputTex: texture_storage_2d<rgba8unorm, write>;
@group(0) @binding(2) var<uniform> blurParams: Params;

fn sample_input(x: i32, y: i32, size: vec2<u32>) -> vec4<f32> {
    if (x < 0 || y < 0 || x >= i32(size.x) || y >= i32(size.y)) {
        return vec4<f32>(0.0);
    }
    return textureLoad(inputTex, vec2<i32>(x, y), 0);
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let size = textureDimensions(inputTex);
    let x = i32(id.x);
    let y = i32(id.y);

    if (x >= i32(size.x) || y >= i32(size.y)) {
        return;
    }

    if (blurParams.sigma <= 0.0001 || blurParams.radius == 0u) {
        textureStore(outputTex, vec2<i32>(x, y), textureLoad(inputTex, vec2<i32>(x, y), 0));
        return;
    }

    var color = sample_input(x, y, size);
    var weightSum = 1.0;
    let inverseVariance = 0.5 / (blurParams.sigma * blurParams.sigma);
    let radius = i32(min(blurParams.radius, 128u));
    for (var offset = 1; offset <= radius; offset = offset + 1) {
        let distance = f32(offset);
        let weight = exp(-(distance * distance) * inverseVariance);
        color = color +
            (sample_input(x, y - offset, size) + sample_input(x, y + offset, size)) * weight;
        weightSum = weightSum + 2.0 * weight;
    }

    textureStore(outputTex, vec2<i32>(x, y), color / weightSum);
}
";

    public const string DropShadow = @"
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
";

    public const string ShadowBlurHorizontal = @"
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

    let sigma = max(params.blurRadius, 0.5);
    let radius = min(i32(ceil(sigma * 3.0)), 64);
    var alphaSum: f32 = 0.0;
    var weightSum: f32 = 0.0;
    for (var dx = -radius; dx <= radius; dx = dx + 1) {
        let sampleX = clamp(x + dx, 0, i32(size.x) - 1);
        let distance = f32(dx);
        let weight = exp(-0.5 * distance * distance / (sigma * sigma));
        alphaSum += textureLoad(inputTex, vec2<i32>(sampleX, y), 0).a * weight;
        weightSum += weight;
    }

    let shadowAlpha = params.color.a * alphaSum / max(weightSum, 0.0001);
    let shadowColor = vec4<f32>(params.color.rgb * shadowAlpha, shadowAlpha);
    textureStore(outputTex, vec2<i32>(x, y), shadowColor);
}
";

    public const string ShadowBlurVertical = @"
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

    let sigma = max(params.blurRadius, 0.5);
    let radius = min(i32(ceil(sigma * 3.0)), 64);
    var colorSum = vec4<f32>(0.0);
    var weightSum: f32 = 0.0;
    for (var dy = -radius; dy <= radius; dy = dy + 1) {
        let sampleY = clamp(y + dy, 0, i32(size.y) - 1);
        let distance = f32(dy);
        let weight = exp(-0.5 * distance * distance / (sigma * sigma));
        colorSum += textureLoad(inputTex, vec2<i32>(x, sampleY), 0) * weight;
        weightSum += weight;
    }

    textureStore(outputTex, vec2<i32>(x, y), colorSum / max(weightSum, 0.0001));
}
";


}
