// Algorithm: Estimate a height-field normal from neighboring texels and apply diffuse/specular image lighting.
// Time complexity: O(1) per output texel.
// Space complexity: O(1) local storage and a fixed neighboring-texture footprint.
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
