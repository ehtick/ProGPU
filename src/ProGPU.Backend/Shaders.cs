namespace ProGPU.Backend;

public static class Shaders
{
    public const string SharedWgpuMathCode = @"
fn solve_quadratic(a: f32, b: f32, c: f32, roots: ptr<function, array<f32, 2>>, root_count: ptr<function, u32>) {
    if (abs(a) < 0.00001) {
        if (abs(b) > 0.00001) {
            (*roots)[0] = -c / b;
            *root_count = 1u;
        } else {
            *root_count = 0u;
        }
    } else {
        let d = b * b - 4.0 * a * c;
        if (d < 0.0) {
            *root_count = 0u;
        } else if (d == 0.0) {
            (*roots)[0] = -b / (2.0 * a);
            *root_count = 1u;
        } else {
            let sqrt_d = sqrt(d);
            (*roots)[0] = (-b - sqrt_d) / (2.0 * a);
            (*roots)[1] = (-b + sqrt_d) / (2.0 * a);
            *root_count = 2u;
        }
    }
}

fn cbrt(x: f32) -> f32 {
    if (x < 0.0) {
        return -pow(-x, 1.0 / 3.0);
    }
    return pow(x, 1.0 / 3.0);
}

fn solve_cubic(a_in: f32, b_in: f32, c_in: f32, d_in: f32, roots: ptr<function, array<f32, 3>>, root_count: ptr<function, u32>) {
    if (abs(a_in) < 0.00001) {
        var quad_roots = array<f32, 2>(0.0, 0.0);
        var quad_count = 0u;
        solve_quadratic(b_in, c_in, d_in, &quad_roots, &quad_count);
        *root_count = quad_count;
        for (var i = 0u; i < quad_count; i = i + 1u) {
            (*roots)[i] = quad_roots[i];
        }
        return;
    }

    let a = b_in / a_in;
    let b = c_in / a_in;
    let c = d_in / a_in;

    let p = b - a * a / 3.0;
    let q = c - a * b / 3.0 + 2.0 * a * a * a / 27.0;

    let D = q * q / 4.0 + p * p * p / 27.0;

    if (D > 0.0) {
        let sqrt_D = sqrt(D);
        let u = cbrt(-q / 2.0 + sqrt_D);
        let v = cbrt(-q / 2.0 - sqrt_D);
        (*roots)[0] = u + v - a / 3.0;
        *root_count = 1u;
    } else {
        if (p < 0.0) {
            let r = 2.0 * sqrt(-p / 3.0);
            let val = clamp(-q / (2.0 * sqrt(-p * p * p / 27.0)), -1.0, 1.0);
            let theta = acos(val);
            let pi = 3.14159265359;
            (*roots)[0] = r * cos(theta / 3.0) - a / 3.0;
            (*roots)[1] = r * cos((theta + 2.0 * pi) / 3.0) - a / 3.0;
            (*roots)[2] = r * cos((theta + 4.0 * pi) / 3.0) - a / 3.0;
            *root_count = 3u;
        } else {
            (*roots)[0] = -a / 3.0;
            *root_count = 1u;
        }
    }
}
";

    public const string VectorShader = @"
struct Brush {
    brushType: u32,
    opacity: f32,
    gradientStart: vec2<f32>,
    gradientEnd: vec2<f32>,
    gradientCenter: vec2<f32>,
    gradientRadius: f32,
    stopCount: u32,
    _pad: u32,
    stopColors0: vec4<f32>,
    stopColors1: vec4<f32>,
    stopColors2: vec4<f32>,
    stopColors3: vec4<f32>,
    stopOffsets: vec4<f32>,
};

struct Uniforms {
    projection: mat4x4<f32>,
    mvp: mat4x4<f32>,
    view: mat4x4<f32>,
    canvasSize: vec2<f32>,
};


@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> brushes: array<Brush>;
@group(1) @binding(0) var pathAtlasSampler: sampler;
@group(1) @binding(1) var pathAtlasTexture: texture_2d<f32>;
@group(2) @binding(0) var maskSampler: sampler;
@group(2) @binding(1) var maskTexture: texture_2d<f32>;

struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>,
    @location(3) brushIndex: f32,
    @location(4) shapeSize: vec2<f32>,
    @location(5) cornerRadius: f32,
    @location(6) strokeThickness: f32,
    @location(7) shapeType: f32,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>,
    @location(2) brushIndex: f32,
    @location(3) shapeSize: vec2<f32>,
    @location(4) cornerRadius: f32,
    @location(5) strokeThickness: f32,
    @location(6) shapeType: f32,
    @location(7) gridIndex: f32,
};

@vertex
fn vs_main(input: VertexInput, @builtin(vertex_index) vertexIndex: u32) -> VertexOutput {
    var output: VertexOutput;
    
    var sType = u32(round(input.shapeType));
    var isStatic = false;
    var useGpuTransforms = false;
    if (input.shapeType >= 195.0) {
        isStatic = true;
        sType = u32(round(input.shapeType - 200.0));
    } else if (input.shapeType >= 95.0) {
        useGpuTransforms = true;
        sType = u32(round(input.shapeType - 100.0));
    }
    


    var inPos = input.position;
    var inTexCoord = input.texCoord;
    var inShapeSize = input.shapeSize;
    var inColor = input.color;

    if ((isStatic || useGpuTransforms) && sType != 8u) {
        if (useGpuTransforms) {
            inPos = (uniforms.view * vec4<f32>(input.position, 0.0, 1.0)).xy;
            if (sType == 3u || sType == 5u || sType == 6u) {
                inTexCoord = (uniforms.view * vec4<f32>(input.texCoord, 0.0, 1.0)).xy;
                inShapeSize = (uniforms.view * vec4<f32>(input.shapeSize, 0.0, 1.0)).xy;
                if (sType == 6u) {
                    inColor = vec4<f32>((uniforms.view * vec4<f32>(input.color.rg, 0.0, 1.0)).xy, input.color.b, input.color.a);
                }
            } else if (sType < 3u) {
                let bIdx = u32(round(input.brushIndex));
                let brush = brushes[bIdx];
                if (brush.brushType > 0u) {
                    inColor = vec4<f32>((uniforms.view * vec4<f32>(input.color.xy, 0.0, 1.0)).xy, input.color.z, input.color.w);
                }
            }
        } else {
            inPos = (uniforms.mvp * vec4<f32>(input.position, 0.0, 1.0)).xy;
            if (sType == 3u || sType == 5u || sType == 6u) {
                inTexCoord = (uniforms.mvp * vec4<f32>(input.texCoord, 0.0, 1.0)).xy;
                inShapeSize = (uniforms.mvp * vec4<f32>(input.shapeSize, 0.0, 1.0)).xy;
                if (sType == 6u) {
                    inColor = vec4<f32>((uniforms.mvp * vec4<f32>(input.color.rg, 0.0, 1.0)).xy, input.color.b, input.color.a);
                }
            } else if (sType < 3u) {
                let bIdx = u32(round(input.brushIndex));
                let brush = brushes[bIdx];
                if (brush.brushType > 0u) {
                    inColor = vec4<f32>((uniforms.mvp * vec4<f32>(input.color.xy, 0.0, 1.0)).xy, input.color.z, input.color.w);
                }
            }
        }
    }

    var worldPos = inPos;
    var texCoord = inTexCoord;
    var gridIndex = 0.0;

    if (sType == 3u) {
        // GPU Stroke Expansion
        var miterN = vec2<f32>(0.0, 0.0);
        var miterScale: f32 = 1.0;
        
        let p0 = inTexCoord;
        let p1 = inShapeSize;
        
        let isStart = abs(input.cornerRadius) < 1.5;
        worldPos = inPos;

        let len1 = length(worldPos - p0);
        let len2 = length(p1 - worldPos);

        if (len1 < 0.001) {
            if (len2 > 0.001) {
                let dir = normalize(p1 - p0);
                miterN = vec2<f32>(-dir.y, dir.x);
            }
        } else if (len2 < 0.001) {
            let dir = normalize(worldPos - p0);
            miterN = vec2<f32>(-dir.y, dir.x);
        } else {
            let dir1 = normalize(worldPos - p0);
            let dir2 = normalize(p1 - worldPos);
            let n1 = vec2<f32>(-dir1.y, dir1.x);
            let n2 = vec2<f32>(-dir2.y, dir2.x);
            miterN = normalize(n1 + n2);
            miterScale = clamp(1.0 / max(dot(miterN, n1), 0.0001), 0.5, 4.0);
        }
        let halfThickness = input.strokeThickness * 0.5;
        let expandedDistance = halfThickness * miterScale + 1.5;
        let signVal = select(-1.0, 1.0, input.cornerRadius > 0.0);
        let offset = miterN * expandedDistance * signVal;
        worldPos = worldPos + offset;
        gridIndex = signVal * expandedDistance;
    } else if (sType == 5u) {
        // GPU Quadratic Bezier Curve Evaluation
        let p0 = inPos;
        let p1 = inTexCoord;
        let p2 = inShapeSize;
        
        let idxStart = u32(round(input.cornerRadius));
        let localIndex = vertexIndex - idxStart;
        let N = clamp(u32(round(input.strokeThickness * 1.5 + 8.0)), 8u, 24u);
        let t = f32(localIndex / 2u) / f32(N);
        let signVal = select(-1.0, 1.0, (localIndex % 2u) == 0u);
        
        let oneMinusT = 1.0 - t;
        let pos = oneMinusT * oneMinusT * p0 + 2.0 * oneMinusT * t * p1 + t * t * p2;
        let tangent = 2.0 * oneMinusT * (p1 - p0) + 2.0 * t * (p2 - p1);
        let len = length(tangent);
        var normal = vec2<f32>(0.0, 0.0);
        if (len > 0.0001) {
            normal = vec2<f32>(-tangent.y, tangent.x) / len;
        }
        let halfThickness = input.strokeThickness * 0.5;
        let expandedDistance = halfThickness + 1.5;
        let offset = normal * expandedDistance * signVal;
        worldPos = pos + offset;
        texCoord = pos;
        gridIndex = signVal * expandedDistance;
    } else if (sType == 6u) {
        // GPU Cubic Bezier Curve Evaluation
        let p0 = inPos;
        let p1 = inTexCoord;
        let p2 = inShapeSize;
        let p3 = inColor.rg;
        
        let idxStart = u32(round(input.cornerRadius));
        let localIndex = vertexIndex - idxStart;
        let N = clamp(u32(round(input.strokeThickness * 1.5 + 8.0)), 8u, 24u);
        let t = f32(localIndex / 2u) / f32(N);
        let signVal = select(-1.0, 1.0, (localIndex % 2u) == 0u);
        
        let oneMinusT = 1.0 - t;
        
        let pos = oneMinusT * oneMinusT * oneMinusT * p0 
                + 3.0 * oneMinusT * oneMinusT * t * p1 
                + 3.0 * oneMinusT * t * t * p2 
                + t * t * t * p3;
                
        let tangent = 3.0 * oneMinusT * oneMinusT * (p1 - p0) 
                    + 6.0 * oneMinusT * t * (p2 - p1) 
                    + 3.0 * t * t * (p3 - p2);
                    
        let len = length(tangent);
        var normal = vec2<f32>(0.0, 0.0);
        if (len > 0.0001) {
            normal = vec2<f32>(-tangent.y, tangent.x) / len;
        }
        let halfThickness = input.strokeThickness * 0.5;
        let expandedDistance = halfThickness + 1.5;
        let offset = normal * expandedDistance * signVal;
        worldPos = pos + offset;
        texCoord = pos;
        gridIndex = signVal * expandedDistance;
    }

    if (sType == 8u) {
        let local3D = vec3<f32>(inPos, inTexCoord.x);
        var pos3D = local3D;
        if (useGpuTransforms) {
            pos3D = (uniforms.view * vec4<f32>(local3D, 1.0)).xyz;
        } else if (isStatic) {
            pos3D = (uniforms.mvp * vec4<f32>(local3D, 1.0)).xyz;
        }
        output.position = uniforms.projection * vec4<f32>(pos3D, 1.0);
    } else {
        var pos = worldPos;
        if (useGpuTransforms) {
            // Since we pre-transformed inPos/inTexCoord/inShapeSize by uniforms.view,
            // worldPos is already in screen-space. Do not transform it again!
            pos = worldPos;
        }
        output.position = uniforms.projection * vec4<f32>(pos, 0.0, 1.0);
    }
    output.color = inColor;
    output.texCoord = texCoord;
    output.brushIndex = input.brushIndex;
    output.shapeSize = inShapeSize;
    output.cornerRadius = input.cornerRadius;
    output.strokeThickness = input.strokeThickness;
    output.shapeType = f32(sType);
    output.gridIndex = gridIndex;
    return output;
}



@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let sType = u32(round(input.shapeType));
    var d: f32 = -1.0;

    var evalCoord = input.texCoord;
    if (sType < 3u) {
        evalCoord = input.color.xy + input.texCoord;
    } else if (sType == 4u) {
        evalCoord = input.shapeSize;
    }

    if (sType == 0u) {
        // Rectangle SDF
        let d_vec = abs(input.texCoord) - input.shapeSize * 0.5;
        d = length(max(d_vec, vec2<f32>(0.0))) + min(max(d_vec.x, d_vec.y), 0.0);
    } else if (sType == 1u) {
        // Ellipse SDF
        let rx = input.shapeSize.x * 0.5;
        let ry = input.shapeSize.y * 0.5;
        if (rx > 0.0001 && ry > 0.0001) {
            let v = (input.texCoord.x * input.texCoord.x) / (rx * rx) + (input.texCoord.y * input.texCoord.y) / (ry * ry);
            let grad = vec2<f32>((2.0 * input.texCoord.x) / (rx * rx), (2.0 * input.texCoord.y) / (ry * ry));
            d = (v - 1.0) / max(length(grad), 0.0001);
        }
    } else if (sType == 2u) {
        // Rounded Rectangle SDF
        let r = input.cornerRadius;
        let d_vec = abs(input.texCoord) - (input.shapeSize * 0.5 - vec2<f32>(r));
        d = length(max(d_vec, vec2<f32>(0.0))) + min(max(d_vec.x, d_vec.y), 0.0) - r;
    }

    var shapeAlpha: f32 = 1.0;
    if (sType < 3u) {
        var d_shape: f32 = 0.0;
        if (input.strokeThickness > 0.0) {
            d_shape = abs(d) - input.strokeThickness * 0.5;
        } else {
            d_shape = d;
        }
        let fw = max(fwidth(d_shape), 0.0001);
        shapeAlpha = 1.0 - smoothstep(-0.5 * fw, 0.5 * fw, d_shape);
    } else if (sType == 3u || sType == 5u || sType == 6u) {
        // Line, Quadratic & Cubic Bezier curves anti-aliasing via signed pixel distance
        let d_pixels = abs(input.gridIndex);
        let d_shape = d_pixels - input.strokeThickness * 0.5;
        shapeAlpha = 1.0 - smoothstep(-0.5, 0.5, d_shape);
    } else if (sType == 4u) {
        // Path rendering: sample coverage directly from PathAtlas
        shapeAlpha = textureSample(pathAtlasTexture, pathAtlasSampler, input.texCoord).r;
    } else if (sType == 7u) {
        // Direct solid fill
        shapeAlpha = 1.0;
    }

    if (shapeAlpha <= 0.0) {
        discard;
    }

    // Process Color Brush
    let bIdx = u32(round(input.brushIndex));
    let brush = brushes[bIdx];

    var finalColor = input.color;
    if (brush.brushType == 0u) {
        let sType = u32(round(input.shapeType));
        if (sType == 5u || sType == 6u) {
            finalColor = vec4<f32>(brush.stopColors0.rgb, brush.stopColors0.a * brush.opacity);
        } else {
            finalColor = vec4<f32>(input.color.rgb, input.color.a * brush.opacity);
        }

    } else {
        var t: f32 = 0.0;
        if (brush.brushType == 1u) {
            // Linear Gradient
            let gradVec = brush.gradientEnd - brush.gradientStart;
            let lenSq = dot(gradVec, gradVec);
            if (lenSq > 0.0001) {
                t = dot(evalCoord - brush.gradientStart, gradVec) / lenSq;
            }
        } else if (brush.brushType == 2u) {
            // Radial Gradient
            let dist = distance(evalCoord, brush.gradientCenter);
            if (brush.gradientRadius > 0.0001) {
                t = dist / brush.gradientRadius;
            }
        }
        t = clamp(t, 0.0, 1.0);

        // Interpolate colors based on stops
        var gradColor = brush.stopColors0;
        if (brush.stopCount > 1u) {
            if (t <= brush.stopOffsets.y) {
                let factor = (t - brush.stopOffsets.x) / max(brush.stopOffsets.y - brush.stopOffsets.x, 0.0001);
                gradColor = mix(brush.stopColors0, brush.stopColors1, clamp(factor, 0.0, 1.0));
            } else if (t <= brush.stopOffsets.z) {
                let factor = (t - brush.stopOffsets.y) / max(brush.stopOffsets.z - brush.stopOffsets.y, 0.0001);
                gradColor = mix(brush.stopColors1, brush.stopColors2, clamp(factor, 0.0, 1.0));
            } else {
                let factor = (t - brush.stopOffsets.z) / max(brush.stopOffsets.w - brush.stopOffsets.z, 0.0001);
                gradColor = mix(brush.stopColors2, brush.stopColors3, clamp(factor, 0.0, 1.0));
            }
        }
        finalColor = vec4<f32>(gradColor.rgb, gradColor.a * brush.opacity);
    }

    let screen_uv = input.position.xy / uniforms.canvasSize;
    let maskAlpha = textureSample(maskTexture, maskSampler, screen_uv).r;
    return vec4<f32>(finalColor.rgb, finalColor.a * shapeAlpha * maskAlpha);
}
";

    public const string TextShader = @"
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>,
    @location(3) brushIndex: f32,
    @location(4) shapeSize: vec2<f32>,
    @location(5) cornerRadius: f32,
    @location(6) strokeThickness: f32,
    @location(7) shapeType: f32,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>,
    @location(2) cornerRadius: f32,
    @location(3) strokeThickness: f32,
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
    if (input.shapeType >= 195.0) {
        pos = (uniforms.mvp * vec4<f32>(input.position, 0.0, 1.0)).xy;
    } else if (input.shapeType >= 95.0) {
        pos = (uniforms.view * vec4<f32>(input.position, 0.0, 1.0)).xy;
    }
    output.position = uniforms.projection * vec4<f32>(pos, 0.0, 1.0);
    output.color = input.color;
    output.texCoord = input.texCoord;
    output.cornerRadius = input.cornerRadius;
    output.strokeThickness = input.strokeThickness;
    return output;
}

@group(1) @binding(0) var atlasSampler: sampler;
@group(1) @binding(1) var atlasTexture: texture_2d<f32>;
@group(2) @binding(0) var maskSampler: sampler;
@group(2) @binding(1) var maskTexture: texture_2d<f32>;

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let alpha = textureSample(atlasTexture, atlasSampler, input.texCoord).r;
    let dilated = clamp(alpha * input.strokeThickness, 0.0, 1.0);
    let finalAlpha = pow(dilated, input.cornerRadius);
    let screen_uv = input.position.xy / uniforms.canvasSize;
    let maskAlpha = textureSample(maskTexture, maskSampler, screen_uv).r;
    return vec4<f32>(input.color.rgb, input.color.a * finalAlpha * maskAlpha);
}
";

    public const string TextureShader = @"
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>,
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
    return output;
}

    @group(1) @binding(0) var texSampler: sampler;
@group(1) @binding(1) var texTexture: texture_2d<f32>;
@group(2) @binding(0) var maskSampler: sampler;
@group(2) @binding(1) var maskTexture: texture_2d<f32>;

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let texColor = textureSample(texTexture, texSampler, input.texCoord);
    let screen_uv = input.position.xy / uniforms.canvasSize;
    let maskAlpha = textureSample(maskTexture, maskSampler, screen_uv).r;
    return texColor * input.color * maskAlpha;
}
";

    public const string GlyphRasterizerShader = @"
struct GlyphUniforms {
    xStart: f32,
    yStart: f32,
    scale: f32,
    glyphIndex: u32,
    atlasX: u32,
    atlasY: u32,
    width: u32,
    height: u32,
    subpixelX: f32,
    _pad0: f32,
    _pad1: f32,
    _pad2: f32,
};

struct GlyphRecord {
    startSegment: u32,
    segmentCount: u32,
    minX: f32,
    minY: f32,
    maxX: f32,
    maxY: f32,
    _pad0: u32,
    _pad1: u32,
};

struct Segment {
    p0: vec2<f32>,
    p1: vec2<f32>,
    p2: vec2<f32>,
    segmentType: u32,
    _pad: u32,
};

@group(0) @binding(0) var<uniform> uniforms: GlyphUniforms;
@group(0) @binding(1) var<storage, read> glyphRecords: array<GlyphRecord>;
@group(0) @binding(2) var<storage, read> segments: array<Segment>;
@group(0) @binding(3) var atlasTexture: texture_storage_2d<rgba8unorm, write>;

" + SharedWgpuMathCode + @"

fn is_point_inside(p: vec2<f32>, record: GlyphRecord) -> bool {
    var winding: i32 = 0;
    let endIdx = record.startSegment + record.segmentCount;
    for (var i: u32 = record.startSegment; i < endIdx; i = i + 1u) {
        let seg = segments[i];
        if (seg.segmentType == 0u) {
            // Line Segment from A to B
            let A = seg.p0;
            let B = seg.p1;
            if (A.y == B.y) {
                continue;
            }
            if (A.y <= p.y) {
                if (B.y > p.y) { // Upward crossing
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding + 1;
                    }
                }
            } else {
                if (B.y <= p.y) { // Downward crossing
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding - 1;
                    }
                }
            }
        } else if (seg.segmentType == 1u) {
            // Quadratic Bezier from A to C with control point B
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            
            let a = A.y - 2.0 * B.y + C.y;
            let b = 2.0 * (B.y - A.y);
            let c = A.y - p.y;
            
            var roots = array<f32, 2>(0.0, 0.0);
            var root_count: u32 = 0u;
            solve_quadratic(a, b, c, &roots, &root_count);
            
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let omt_eval = 1.0 - t_eval;
                    let deriv_y = 2.0 * omt_eval * (B.y - A.y) + 2.0 * t_eval * (C.y - B.y);
                    
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y >= A.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y < A.y);
                        }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y < C.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y >= C.y);
                        }
                    } else {
                        is_valid = true;
                    }
                    
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * A.x + 2.0 * omt * tc * B.x + tc * tc * C.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) {
                                winding = winding + 1;
                            } else if (deriv_y < 0.0) {
                                winding = winding - 1;
                            }
                        }
                    }
                }
            }
        }
    }
    return winding != 0;
}

@compute @workgroup_size(16, 16)
fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let x = global_id.x;
    let y = global_id.y;
    
    if (x >= uniforms.width || y >= uniforms.height) {
        return;
    }
    
    let glyphIndex = uniforms.glyphIndex;
    let record = glyphRecords[glyphIndex];
    
    let px = uniforms.xStart + f32(x);
    let py = uniforms.yStart + f32(y);
    
    var coverage: f32 = 0.0;
    
    for (var dy: f32 = 0.125; dy < 1.0; dy = dy + 0.25) {
        for (var dx: f32 = 0.125; dx < 1.0; dx = dx + 0.25) {
            let sp = vec2<f32>(px + dx - uniforms.subpixelX, py + dy);
            let fp = vec2<f32>(sp.x / uniforms.scale, -sp.y / uniforms.scale);
            if (is_point_inside(fp, record)) {
                coverage = coverage + 0.0625;
            }
        }
    }
    
    let writeCoord = vec2<u32>(uniforms.atlasX + x, uniforms.atlasY + y);
    textureStore(atlasTexture, writeCoord, vec4<f32>(coverage, 0.0, 0.0, 0.0));
}
";

    public const string PathRasterizerShader = @"
struct PathUniforms {
    xStart: f32,
    yStart: f32,
    scale: f32,
    pathIndex: u32,
    atlasX: u32,
    atlasY: u32,
    width: u32,
    height: u32,
};

struct PathRecord {
    startSegment: u32,
    segmentCount: u32,
    minX: f32,
    minY: f32,
    maxX: f32,
    maxY: f32,
    _pad0: u32,
    _pad1: u32,
};

struct Segment {
    p0: vec2<f32>,
    p1: vec2<f32>,
    p2: vec2<f32>,
    p3: vec2<f32>,
    segmentType: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

@group(0) @binding(0) var<uniform> uniforms: PathUniforms;
@group(0) @binding(1) var<storage, read> pathRecords: array<PathRecord>;
@group(0) @binding(2) var<storage, read> segments: array<Segment>;
@group(0) @binding(3) var atlasTexture: texture_storage_2d<rgba8unorm, write>;

" + SharedWgpuMathCode + @"

fn is_point_inside(p: vec2<f32>, record: PathRecord) -> bool {
    var winding: i32 = 0;
    let endIdx = record.startSegment + record.segmentCount;
    for (var i: u32 = record.startSegment; i < endIdx; i = i + 1u) {
        let seg = segments[i];
        if (seg.segmentType == 0u) {
            let A = seg.p0;
            let B = seg.p1;
            if (A.y == B.y) {
                continue;
            }
            if (A.y <= p.y) {
                if (B.y > p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding + 1;
                    }
                }
            } else {
                if (B.y <= p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding - 1;
                    }
                }
            }
        } else if (seg.segmentType == 1u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            
            let a = A.y - 2.0 * B.y + C.y;
            let b = 2.0 * (B.y - A.y);
            let c = A.y - p.y;
            
            var roots = array<f32, 2>(0.0, 0.0);
            var root_count: u32 = 0u;
            solve_quadratic(a, b, c, &roots, &root_count);
            
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let omt_eval = 1.0 - t_eval;
                    let deriv_y = 2.0 * omt_eval * (B.y - A.y) + 2.0 * t_eval * (C.y - B.y);
                    
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y >= A.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y < A.y);
                        }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y < C.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y >= C.y);
                        }
                    } else {
                        is_valid = true;
                    }
                    
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * A.x + 2.0 * omt * tc * B.x + tc * tc * C.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) {
                                winding = winding + 1;
                            } else if (deriv_y < 0.0) {
                                winding = winding - 1;
                            }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 2u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            let D_pt = seg.p3;
            
            let a = -A.y + 3.0 * B.y - 3.0 * C.y + D_pt.y;
            let b = 3.0 * A.y - 6.0 * B.y + 3.0 * C.y;
            let c = -3.0 * A.y + 3.0 * B.y;
            let d = A.y - p.y;
            
            var roots = array<f32, 3>(0.0, 0.0, 0.0);
            var root_count: u32 = 0u;
            solve_cubic(a, b, c, d, &roots, &root_count);
            
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let deriv_y = 3.0 * a * t_eval * t_eval + 2.0 * b * t_eval + c;
                    
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y >= A.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y < A.y);
                        }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) {
                            is_valid = (p.y < D_pt.y);
                        } else if (deriv_y < 0.0) {
                            is_valid = (p.y >= D_pt.y);
                        }
                    } else {
                        is_valid = true;
                    }
                    
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * omt * A.x
                                + 3.0 * omt * omt * tc * B.x
                                + 3.0 * omt * tc * tc * C.x
                                + tc * tc * tc * D_pt.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) {
                                winding = winding + 1;
                            } else if (deriv_y < 0.0) {
                                winding = winding - 1;
                            }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 3u) {
            let p0 = seg.p0;
            let p1 = seg.p1;
            let center = seg.p2;
            let r = seg.p3;
            let rx = r.x;
            let ry = r.y;
            
            let theta1 = bitcast<f32>(seg._pad0);
            let delta_theta = bitcast<f32>(seg._pad1);
            let phi = bitcast<f32>(seg._pad2);
            
            let cos_phi = cos(phi);
            let sin_phi = sin(phi);
            
            let dy = p.y - center.y;
            
            let rx2 = rx * rx;
            let ry2 = ry * ry;
            
            let A_val = (cos_phi * cos_phi) / rx2 + (sin_phi * sin_phi) / ry2;
            let B_val = 2.0 * dy * cos_phi * sin_phi * (1.0 / rx2 - 1.0 / ry2);
            let C_val = dy * dy * ((sin_phi * sin_phi) / rx2 + (cos_phi * cos_phi) / ry2) - 1.0;
            
            let discriminant = B_val * B_val - 4.0 * A_val * C_val;
            if (discriminant >= 0.0) {
                let sqrt_d = sqrt(discriminant);
                let dx1 = (-B_val - sqrt_d) / (2.0 * A_val);
                let dx2 = (-B_val + sqrt_d) / (2.0 * A_val);
                
                var roots = array<f32, 2>(dx1, dx2);
                for (var r_idx: u32 = 0u; r_idx < 2u; r_idx = r_idx + 1u) {
                    let dx = roots[r_idx];
                    let intersectX = center.x + dx;
                    if (p.x < intersectX) {
                        let localX = dx * cos_phi + dy * sin_phi;
                        let localY = -dx * sin_phi + dy * cos_phi;
                        let theta = atan2(localY / ry, localX / rx);
                        
                        var t: f32 = 0.0;
                        let pi2 = 6.283185307179586;
                        if (delta_theta > 0.0) {
                            let diff = (theta - theta1) - pi2 * floor((theta - theta1) / pi2);
                            t = diff / delta_theta;
                        } else {
                            let diff = (theta1 - theta) - pi2 * floor((theta1 - theta) / pi2);
                            t = diff / (-delta_theta);
                        }
                        
                        let deriv_y = delta_theta * (-rx * sin(theta) * sin_phi + ry * cos(theta) * cos_phi);
                        
                        var is_valid = false;
                        if (deriv_y > 0.0) {
                            is_valid = (t >= 0.0 && t < 1.0);
                        } else if (deriv_y < 0.0) {
                            is_valid = (t > 0.0 && t <= 1.0);
                        }
                        
                        if (is_valid) {
                            if (deriv_y > 0.0) {
                                winding = winding + 1;
                            } else if (deriv_y < 0.0) {
                                winding = winding - 1;
                            }
                        }
                    }
                }
            }
        }
    }
    return winding != 0;
}

@compute @workgroup_size(16, 16)
fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
    let x = global_id.x;
    let y = global_id.y;
    
    if (x >= uniforms.width || y >= uniforms.height) {
        return;
    }
    
    let pathIndex = uniforms.pathIndex;
    let record = pathRecords[pathIndex];
    
    let px = uniforms.xStart + f32(x);
    let py = uniforms.yStart + f32(y);
    
    var coverage: f32 = 0.0;
    
    // Sample 0: +0.25, +0.25
    let sp0 = vec2<f32>(px + 0.25, py + 0.25);
    let fp0 = vec2<f32>(sp0.x / uniforms.scale, sp0.y / uniforms.scale);
    if (is_point_inside(fp0, record)) {
        coverage = coverage + 0.25;
    }
    
    // Sample 1: +0.75, +0.25
    let sp1 = vec2<f32>(px + 0.75, py + 0.25);
    let fp1 = vec2<f32>(sp1.x / uniforms.scale, sp1.y / uniforms.scale);
    if (is_point_inside(fp1, record)) {
        coverage = coverage + 0.25;
    }
    
    // Sample 2: +0.25, +0.75
    let sp2 = vec2<f32>(px + 0.25, py + 0.75);
    let fp2 = vec2<f32>(sp2.x / uniforms.scale, sp2.y / uniforms.scale);
    if (is_point_inside(fp2, record)) {
        coverage = coverage + 0.25;
    }
    
    // Sample 3: +0.75, +0.75
    let sp3 = vec2<f32>(px + 0.75, py + 0.75);
    let fp3 = vec2<f32>(sp3.x / uniforms.scale, sp3.y / uniforms.scale);
    if (is_point_inside(fp3, record)) {
        coverage = coverage + 0.25;
    }
    
    let writeCoord = vec2<u32>(uniforms.atlasX + x, uniforms.atlasY + y);
    textureStore(atlasTexture, writeCoord, vec4<f32>(coverage, 0.0, 0.0, 0.0));
}
";

    public const string ChartLineShader = @"
const AA_PADDING: f32 = 1.5;

struct VSUniforms {
  transform       : mat4x4<f32>,
  canvasSize      : vec2<f32>,
  devicePixelRatio: f32,
  lineWidthCssPx  : f32,
  scale           : vec2<f32>,
  translate       : vec2<f32>,
};

@group(0) @binding(0) var<uniform> vsUniforms : VSUniforms;

struct FSUniforms {
  color : vec4<f32>,
};

@group(0) @binding(1) var<uniform> fsUniforms : FSUniforms;

@group(0) @binding(2) var<storage, read> points : array<vec2<f32>>;

struct VSOut {
  @builtin(position) clipPosition : vec4<f32>,
  @location(0) acrossDevice       : f32,
  @location(1) @interpolate(flat) widthDevice : f32,
};

fn quadUv(vid : u32) -> vec2<f32> {
  switch (vid) {
    case 0u: { return vec2<f32>(0.0, 0.0); }
    case 1u: { return vec2<f32>(1.0, 0.0); }
    case 2u: { return vec2<f32>(0.0, 1.0); }
    case 3u: { return vec2<f32>(0.0, 1.0); }
    case 4u: { return vec2<f32>(1.0, 0.0); }
    default: { return vec2<f32>(1.0, 1.0); }
  }
}

@vertex
fn vs_main(
  @builtin(vertex_index) vid : u32,
  @builtin(instance_index) iid : u32,
) -> VSOut {
  let uv = quadUv(vid);
  let pA_data = points[iid];
  let pB_data = points[iid + 1u];

  if (pA_data.x != pA_data.x || pA_data.y != pA_data.y ||
      pB_data.x != pB_data.x || pB_data.y != pB_data.y) {
    var out: VSOut;
    out.clipPosition = vec4<f32>(0.0, 0.0, 0.0, 0.0);
    out.acrossDevice = 0.0;
    out.widthDevice = 0.0;
    return out;
  }

  let pA_scaled = pA_data * vsUniforms.scale + vsUniforms.translate;
  let pB_scaled = pB_data * vsUniforms.scale + vsUniforms.translate;

  let clipA = vsUniforms.transform * vec4<f32>(pA_scaled, 0.0, 1.0);
  let clipB = vsUniforms.transform * vec4<f32>(pB_scaled, 0.0, 1.0);

  let ndcA = clipA.xy / clipA.w;
  let ndcB = clipB.xy / clipB.w;
  let screenA = vec2<f32>(
    (ndcA.x * 0.5 + 0.5) * vsUniforms.canvasSize.x,
    (1.0 - (ndcA.y * 0.5 + 0.5)) * vsUniforms.canvasSize.y,
  );
  let screenB = vec2<f32>(
    (ndcB.x * 0.5 + 0.5) * vsUniforms.canvasSize.x,
    (1.0 - (ndcB.y * 0.5 + 0.5)) * vsUniforms.canvasSize.y,
  );

  let delta = screenB - screenA;
  let segLen = length(delta);

  if (segLen < 1e-6) {
    var out : VSOut;
    out.clipPosition = clipA;
    out.acrossDevice = 0.0;
    out.widthDevice = 0.0;
    return out;
  }

  let dir = delta / segLen;
  let perp = vec2<f32>(dir.y, -dir.x);

  let widthHalfCss = vsUniforms.lineWidthCssPx * 0.5;
  let widthHalfDevice = widthHalfCss * vsUniforms.devicePixelRatio;
  let totalHalfDevice = widthHalfDevice + AA_PADDING;

  let offsetDevice = perp * totalHalfDevice * (uv.y * 2.0 - 1.0);
  let lengthOffsetDevice = dir * totalHalfDevice * (uv.x * (segLen + totalHalfDevice * 2.0) - totalHalfDevice);

  let pDevice = mix(screenA, screenB, uv.x) + offsetDevice;

  var out : VSOut;
  let pNdc = vec2<f32>(
    (pDevice.x / vsUniforms.canvasSize.x - 0.5) * 2.0,
    (1.0 - pDevice.y / vsUniforms.canvasSize.y - 0.5) * 2.0,
  );
  out.clipPosition = vec4<f32>(pNdc, 0.0, 1.0);
  out.acrossDevice = (uv.y * 2.0 - 1.0) * totalHalfDevice;
  out.widthDevice = widthHalfDevice;
  return out;
}

@fragment
fn fs_main(in : VSOut) -> @location(0) vec4<f32> {
  let dist = abs(in.acrossDevice) - in.widthDevice;
  let alpha = 1.0 - smoothstep(0.0, AA_PADDING, dist);
  if (alpha <= 0.0) {
    discard;
  }
  return vec4<f32>(fsUniforms.color.rgb, fsUniforms.color.a * alpha);
}
";

    public const string ChartScatterShader = @"
struct VSUniforms {
  transform: mat4x4<f32>,
  viewportPx: vec2<f32>,
  _pad0: vec2<f32>,
  scale: vec2<f32>,
  translate: vec2<f32>,
};

@group(0) @binding(0) var<uniform> vsUniforms: VSUniforms;

struct FSUniforms {
  color: vec4<f32>,
};

@group(0) @binding(1) var<uniform> fsUniforms: FSUniforms;

struct VSIn {
  @location(0) center: vec2<f32>,
  @location(1) radiusPx: f32,
};

struct VSOut {
  @builtin(position) clipPosition: vec4<f32>,
  @location(0) localPx: vec2<f32>,
  @location(1) radiusPx: f32,
};

fn quadCorner(vid : u32) -> vec2<f32> {
  switch (vid) {
    case 0u: { return vec2<f32>(-1.0, -1.0); }
    case 1u: { return vec2<f32>( 1.0, -1.0); }
    case 2u: { return vec2<f32>(-1.0,  1.0); }
    case 3u: { return vec2<f32>(-1.0,  1.0); }
    case 4u: { return vec2<f32>( 1.0, -1.0); }
    default: { return vec2<f32>( 1.0,  1.0); }
  }
}

@vertex
fn vs_main(in: VSIn, @builtin(vertex_index) vertexIndex: u32) -> VSOut {
  let corner = quadCorner(vertexIndex);
  let localPx = corner * in.radiusPx;

  let localClip = localPx * (2.0 / vsUniforms.viewportPx);
  let centerScaled = in.center * vsUniforms.scale + vsUniforms.translate;
  let centerClip = (vsUniforms.transform * vec4<f32>(centerScaled, 0.0, 1.0)).xy;

  var out: VSOut;
  out.clipPosition = vec4<f32>(centerClip + localClip, 0.0, 1.0);
  out.localPx = localPx;
  out.radiusPx = in.radiusPx;
  return out;
}

@fragment
fn fs_main(in: VSOut) -> @location(0) vec4<f32> {
  let dist = length(in.localPx) - in.radiusPx;
  let w = fwidth(dist);
  let a = 1.0 - smoothstep(0.0, w, dist);

  if (a <= 0.0) {
    discard;
  }

  return vec4<f32>(fsUniforms.color.rgb, fsUniforms.color.a * a);
}
";

    public const string PathOpGeometryShader = @"
struct PathOpUniforms {
    op: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

struct PathRecord {
    startSegment: u32,
    segmentCount: u32,
    minX: f32,
    minY: f32,
    maxX: f32,
    maxY: f32,
    _pad0: u32,
    _pad1: u32,
};

struct Segment {
    p0: vec2<f32>,
    p1: vec2<f32>,
    p2: vec2<f32>,
    p3: vec2<f32>,
    segmentType: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

struct OutputSegments {
    count: atomic<u32>,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
    segments: array<Segment>,
};

@group(0) @binding(0) var<uniform> uniforms: PathOpUniforms;
@group(0) @binding(1) var<storage, read> recordA: PathRecord;
@group(0) @binding(2) var<storage, read> segmentsA: array<Segment>;
@group(0) @binding(3) var<storage, read> recordB: PathRecord;
@group(0) @binding(4) var<storage, read> segmentsB: array<Segment>;
@group(0) @binding(5) var<storage, read_write> destRecord: PathRecord;
@group(0) @binding(6) var<storage, read_write> destSegments: OutputSegments;

" + SharedWgpuMathCode + @"

fn is_point_inside_A(p: vec2<f32>) -> bool {
    var winding: i32 = 0;
    let endIdx = recordA.startSegment + recordA.segmentCount;
    for (var i: u32 = recordA.startSegment; i < endIdx; i = i + 1u) {
        let seg = segmentsA[i];
        if (seg.segmentType == 0u) {
            let A = seg.p0;
            let B = seg.p1;
            if (A.y == B.y) {
                continue;
            }
            if (A.y <= p.y) {
                if (B.y > p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding + 1;
                    }
                }
            } else {
                if (B.y <= p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding - 1;
                    }
                }
            }
        } else if (seg.segmentType == 1u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            let a = A.y - 2.0 * B.y + C.y;
            let b = 2.0 * (B.y - A.y);
            let c = A.y - p.y;
            var roots = array<f32, 2>(0.0, 0.0);
            var root_count: u32 = 0u;
            solve_quadratic(a, b, c, &roots, &root_count);
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let omt_eval = 1.0 - t_eval;
                    let deriv_y = 2.0 * omt_eval * (B.y - A.y) + 2.0 * t_eval * (C.y - B.y);
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) { is_valid = (p.y >= A.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y < A.y); }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) { is_valid = (p.y < C.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y >= C.y); }
                    } else {
                        is_valid = true;
                    }
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * A.x + 2.0 * omt * tc * B.x + tc * tc * C.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 2u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            let D_pt = seg.p3;
            let a = -A.y + 3.0 * B.y - 3.0 * C.y + D_pt.y;
            let b = 3.0 * A.y - 6.0 * B.y + 3.0 * C.y;
            let c = -3.0 * A.y + 3.0 * B.y;
            let d = A.y - p.y;
            var roots = array<f32, 3>(0.0, 0.0, 0.0);
            var root_count: u32 = 0u;
            solve_cubic(a, b, c, d, &roots, &root_count);
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let deriv_y = 3.0 * a * t_eval * t_eval + 2.0 * b * t_eval + c;
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) { is_valid = (p.y >= A.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y < A.y); }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) { is_valid = (p.y < D_pt.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y >= D_pt.y); }
                    } else {
                        is_valid = true;
                    }
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * omt * A.x + 3.0 * omt * omt * tc * B.x + 3.0 * omt * tc * tc * C.x + tc * tc * tc * D_pt.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 3u) {
            let p0 = seg.p0; let p1 = seg.p1; let center = seg.p2; let r = seg.p3;
            let rx = r.x; let ry = r.y;
            let theta1 = bitcast<f32>(seg._pad0);
            let delta_theta = bitcast<f32>(seg._pad1);
            let phi = bitcast<f32>(seg._pad2);
            let cos_phi = cos(phi); let sin_phi = sin(phi);
            let dy = p.y - center.y;
            let rx2 = rx * rx; let ry2 = ry * ry;
            let A_val = (cos_phi * cos_phi) / rx2 + (sin_phi * sin_phi) / ry2;
            let B_val = 2.0 * dy * cos_phi * sin_phi * (1.0 / rx2 - 1.0 / ry2);
            let C_val = dy * dy * ((sin_phi * sin_phi) / rx2 + (cos_phi * cos_phi) / ry2) - 1.0;
            let discriminant = B_val * B_val - 4.0 * A_val * C_val;
            if (discriminant >= 0.0) {
                let sqrt_d = sqrt(discriminant);
                let dx1 = (-B_val - sqrt_d) / (2.0 * A_val);
                let dx2 = (-B_val + sqrt_d) / (2.0 * A_val);
                var roots = array<f32, 2>(dx1, dx2);
                for (var r_idx: u32 = 0u; r_idx < 2u; r_idx = r_idx + 1u) {
                    let dx = roots[r_idx];
                    let intersectX = center.x + dx;
                    if (p.x < intersectX) {
                        let localX = dx * cos_phi + dy * sin_phi;
                        let localY = -dx * sin_phi + dy * cos_phi;
                        let theta = atan2(localY / ry, localX / rx);
                        var t: f32 = 0.0;
                        let pi2 = 6.283185307179586;
                        if (delta_theta > 0.0) {
                            let diff = (theta - theta1) - pi2 * floor((theta - theta1) / pi2);
                            t = diff / delta_theta;
                        } else {
                            let diff = (theta1 - theta) - pi2 * floor((theta1 - theta) / pi2);
                            t = diff / (-delta_theta);
                        }
                        let deriv_y = delta_theta * (-rx * sin(theta) * sin_phi + ry * cos(theta) * cos_phi);
                        var is_valid = false;
                        if (deriv_y > 0.0) { is_valid = (t >= 0.0 && t < 1.0); }
                        else if (deriv_y < 0.0) { is_valid = (t > 0.0 && t <= 1.0); }
                        if (is_valid) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        }
    }
    return winding != 0;
}

fn is_point_inside_B(p: vec2<f32>) -> bool {
    var winding: i32 = 0;
    let endIdx = recordB.startSegment + recordB.segmentCount;
    for (var i: u32 = recordB.startSegment; i < endIdx; i = i + 1u) {
        let seg = segmentsB[i];
        if (seg.segmentType == 0u) {
            let A = seg.p0;
            let B = seg.p1;
            if (A.y == B.y) {
                continue;
            }
            if (A.y <= p.y) {
                if (B.y > p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding + 1;
                    }
                }
            } else {
                if (B.y <= p.y) {
                    let t = (p.y - A.y) / (B.y - A.y);
                    let intersectX = A.x + t * (B.x - A.x);
                    if (p.x < intersectX) {
                        winding = winding - 1;
                    }
                }
            }
        } else if (seg.segmentType == 1u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            let a = A.y - 2.0 * B.y + C.y;
            let b = 2.0 * (B.y - A.y);
            let c = A.y - p.y;
            var roots = array<f32, 2>(0.0, 0.0);
            var root_count: u32 = 0u;
            solve_quadratic(a, b, c, &roots, &root_count);
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let omt_eval = 1.0 - t_eval;
                    let deriv_y = 2.0 * omt_eval * (B.y - A.y) + 2.0 * t_eval * (C.y - B.y);
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) { is_valid = (p.y >= A.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y < A.y); }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) { is_valid = (p.y < C.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y >= C.y); }
                    } else {
                        is_valid = true;
                    }
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * A.x + 2.0 * omt * tc * B.x + tc * tc * C.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 2u) {
            let A = seg.p0;
            let B = seg.p1;
            let C = seg.p2;
            let D_pt = seg.p3;
            let a = -A.y + 3.0 * B.y - 3.0 * C.y + D_pt.y;
            let b = 3.0 * A.y - 6.0 * B.y + 3.0 * C.y;
            let c = -3.0 * A.y + 3.0 * B.y;
            let d = A.y - p.y;
            var roots = array<f32, 3>(0.0, 0.0, 0.0);
            var root_count: u32 = 0u;
            solve_cubic(a, b, c, d, &roots, &root_count);
            for (var r: u32 = 0u; r < root_count; r = r + 1u) {
                let t = roots[r];
                if (t >= -0.01 && t <= 1.01) {
                    let t_eval = clamp(t, 0.00001, 0.99999);
                    let deriv_y = 3.0 * a * t_eval * t_eval + 2.0 * b * t_eval + c;
                    var is_valid = false;
                    if (t < 0.005) {
                        if (deriv_y > 0.0) { is_valid = (p.y >= A.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y < A.y); }
                    } else if (t > 0.995) {
                        if (deriv_y > 0.0) { is_valid = (p.y < D_pt.y); }
                        else if (deriv_y < 0.0) { is_valid = (p.y >= D_pt.y); }
                    } else {
                        is_valid = true;
                    }
                    if (is_valid) {
                        let tc = clamp(t, 0.0, 1.0);
                        let omt = 1.0 - tc;
                        let x_t = omt * omt * omt * A.x + 3.0 * omt * omt * tc * B.x + 3.0 * omt * tc * tc * C.x + tc * tc * tc * D_pt.x;
                        if (p.x < x_t) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        } else if (seg.segmentType == 3u) {
            let p0 = seg.p0; let p1 = seg.p1; let center = seg.p2; let r = seg.p3;
            let rx = r.x; let ry = r.y;
            let theta1 = bitcast<f32>(seg._pad0);
            let delta_theta = bitcast<f32>(seg._pad1);
            let phi = bitcast<f32>(seg._pad2);
            let cos_phi = cos(phi); let sin_phi = sin(phi);
            let dy = p.y - center.y;
            let rx2 = rx * rx; let ry2 = ry * ry;
            let A_val = (cos_phi * cos_phi) / rx2 + (sin_phi * sin_phi) / ry2;
            let B_val = 2.0 * dy * cos_phi * sin_phi * (1.0 / rx2 - 1.0 / ry2);
            let C_val = dy * dy * ((sin_phi * sin_phi) / rx2 + (cos_phi * cos_phi) / ry2) - 1.0;
            let discriminant = B_val * B_val - 4.0 * A_val * C_val;
            if (discriminant >= 0.0) {
                let sqrt_d = sqrt(discriminant);
                let dx1 = (-B_val - sqrt_d) / (2.0 * A_val);
                let dx2 = (-B_val + sqrt_d) / (2.0 * A_val);
                var roots = array<f32, 2>(dx1, dx2);
                for (var r_idx: u32 = 0u; r_idx < 2u; r_idx = r_idx + 1u) {
                    let dx = roots[r_idx];
                    let intersectX = center.x + dx;
                    if (p.x < intersectX) {
                        let localX = dx * cos_phi + dy * sin_phi;
                        let localY = -dx * sin_phi + dy * cos_phi;
                        let theta = atan2(localY / ry, localX / rx);
                        var t: f32 = 0.0;
                        let pi2 = 6.283185307179586;
                        if (delta_theta > 0.0) {
                            let diff = (theta - theta1) - pi2 * floor((theta - theta1) / pi2);
                            t = diff / delta_theta;
                        } else {
                            let diff = (theta1 - theta) - pi2 * floor((theta1 - theta) / pi2);
                            t = diff / (-delta_theta);
                        }
                        let deriv_y = delta_theta * (-rx * sin(theta) * sin_phi + ry * cos(theta) * cos_phi);
                        var is_valid = false;
                        if (deriv_y > 0.0) { is_valid = (t >= 0.0 && t < 1.0); }
                        else if (deriv_y < 0.0) { is_valid = (t > 0.0 && t <= 1.0); }
                        if (is_valid) {
                            if (deriv_y > 0.0) { winding = winding + 1; }
                            else if (deriv_y < 0.0) { winding = winding - 1; }
                        }
                    }
                }
            }
        }
    }
    return winding != 0;
}

fn intersect_lines(p0: vec2<f32>, p1: vec2<f32>, q0: vec2<f32>, q1: vec2<f32>, t: ptr<function, f32>, u: ptr<function, f32>) -> bool {
    let r = p1 - p0;
    let s = q1 - q0;
    let denom = r.x * s.y - r.y * s.x;
    if (abs(denom) < 0.00001) {
        return false;
    }
    let t_val = ((q0.x - p0.x) * s.y - (q0.y - p0.y) * s.x) / denom;
    let u_val = ((q0.x - p0.x) * r.y - (q0.y - p0.y) * r.x) / denom;
    if (t_val >= -0.001 && t_val <= 1.001 && u_val >= -0.001 && u_val <= 1.001) {
        *t = clamp(t_val, 0.0, 1.0);
        *u = clamp(u_val, 0.0, 1.0);
        return true;
    }
    return false;
}

fn evaluate_segment(seg: Segment, t: f32) -> vec2<f32> {
    if (seg.segmentType == 0u) {
        return mix(seg.p0, seg.p1, t);
    } else if (seg.segmentType == 1u) {
        let p01 = mix(seg.p0, seg.p1, t);
        let p12 = mix(seg.p1, seg.p2, t);
        return mix(p01, p12, t);
    } else if (seg.segmentType == 2u) {
        let p01 = mix(seg.p0, seg.p1, t);
        let p12 = mix(seg.p1, seg.p2, t);
        let p23 = mix(seg.p2, seg.p3, t);
        let p012 = mix(p01, p12, t);
        let p123 = mix(p12, p23, t);
        return mix(p012, p123, t);
    } else { // Arc
        let theta1 = bitcast<f32>(seg._pad0);
        let delta_theta = bitcast<f32>(seg._pad1);
        let phi = bitcast<f32>(seg._pad2);
        let theta = theta1 + t * delta_theta;
        let center = seg.p2;
        let r = seg.p3;
        let rx = r.x;
        let ry = r.y;
        let cos_phi = cos(phi);
        let sin_phi = sin(phi);
        let cosT = cos(theta);
        let sinT = sin(theta);
        return vec2<f32>(
            rx * cosT * cos_phi - ry * sinT * sin_phi + center.x,
            rx * cosT * sin_phi + ry * sinT * cos_phi + center.y
        );
    }
}

fn split_segment(seg: Segment, t: f32, left: ptr<function, Segment>, right: ptr<function, Segment>) {
    (*left).segmentType = seg.segmentType;
    (*right).segmentType = seg.segmentType;
    (*left)._pad0 = seg._pad0; (*left)._pad1 = seg._pad1; (*left)._pad2 = seg._pad2;
    (*right)._pad0 = seg._pad0; (*right)._pad1 = seg._pad1; (*right)._pad2 = seg._pad2;

    if (seg.segmentType == 0u) {
        let p_t = mix(seg.p0, seg.p1, t);
        (*left).p0 = seg.p0; (*left).p1 = p_t;
        (*right).p0 = p_t; (*right).p1 = seg.p1;
    } else if (seg.segmentType == 1u) {
        let p01 = mix(seg.p0, seg.p1, t);
        let p12 = mix(seg.p1, seg.p2, t);
        let p_t = mix(p01, p12, t);
        (*left).p0 = seg.p0; (*left).p1 = p01; (*left).p2 = p_t;
        (*right).p0 = p_t; (*right).p1 = p12; (*right).p2 = seg.p2;
    } else if (seg.segmentType == 2u) {
        let p01 = mix(seg.p0, seg.p1, t);
        let p12 = mix(seg.p1, seg.p2, t);
        let p23 = mix(seg.p2, seg.p3, t);
        let p012 = mix(p01, p12, t);
        let p123 = mix(p12, p23, t);
        let p_t = mix(p012, p123, t);
        (*left).p0 = seg.p0; (*left).p1 = p01; (*left).p2 = p012; (*left).p3 = p_t;
        (*right).p0 = p_t; (*right).p1 = p123; (*right).p2 = p23; (*right).p3 = seg.p3;
    } else if (seg.segmentType == 3u) {
        let theta1 = bitcast<f32>(seg._pad0);
        let delta_theta = bitcast<f32>(seg._pad1);
        let phi = bitcast<f32>(seg._pad2);
        let split_theta = theta1 + t * delta_theta;
        (*left).p0 = seg.p0;
        let center = seg.p2; let r = seg.p3; let rx = r.x; let ry = r.y;
        let cos_phi = cos(phi); let sin_phi = sin(phi);
        let cosT = cos(split_theta); let sinT = sin(split_theta);
        let p_t = vec2<f32>(
            rx * cosT * cos_phi - ry * sinT * sin_phi + center.x,
            rx * cosT * sin_phi + ry * sinT * cos_phi + center.y
        );
        (*left).p1 = p_t; (*left).p2 = center; (*left).p3 = r;
        (*left)._pad0 = seg._pad0; (*left)._pad1 = bitcast<u32>(t * delta_theta);
        
        (*right).p0 = p_t; (*right).p1 = seg.p1; (*right).p2 = center; (*right).p3 = r;
        (*right)._pad0 = bitcast<u32>(split_theta); (*right)._pad1 = bitcast<u32>((1.0 - t) * delta_theta);
    }
}

fn get_sub_segment(seg: Segment, t0: f32, t1: f32) -> Segment {
    if (t0 <= 0.0001 && t1 >= 0.9999) {
        return seg;
    }
    var left = seg;
    var right = seg;
    split_segment(seg, t1, &left, &right);
    var sub_left = left;
    var sub_right = left;
    let u = clamp(t0 / max(t1, 0.0001), 0.0, 1.0);
    split_segment(left, u, &sub_left, &sub_right);
    return sub_right;
}

fn reverse_segment(seg: Segment) -> Segment {
    var out = seg;
    if (seg.segmentType == 0u) {
        out.p0 = seg.p1;
        out.p1 = seg.p0;
    } else if (seg.segmentType == 1u) {
        out.p0 = seg.p2;
        out.p2 = seg.p0;
    } else if (seg.segmentType == 2u) {
        out.p0 = seg.p3;
        out.p3 = seg.p0;
        out.p1 = seg.p2;
        out.p2 = seg.p1;
    } else if (seg.segmentType == 3u) {
        out.p0 = seg.p1;
        out.p1 = seg.p0;
        let delta_theta = bitcast<f32>(seg._pad1);
        out._pad1 = bitcast<u32>(-delta_theta);
        let theta1 = bitcast<f32>(seg._pad0);
        out._pad0 = bitcast<u32>(theta1 + delta_theta);
    }
    return out;
}

@compute @workgroup_size(64)
fn cs_main(@builtin(global_invocation_id) global_id: vec3<u32>) {
    if (global_id.x == 999999u) {
        destRecord.startSegment = 0u;
    }
    let segmentCountA = recordA.segmentCount;
    let segmentCountB = recordB.segmentCount;
    
    if (global_id.x >= segmentCountA + segmentCountB) {
        return;
    }
    
    let isA = global_id.x < segmentCountA;
    var seg: Segment;
    if (isA) {
        seg = segmentsA[recordA.startSegment + global_id.x];
    } else {
        seg = segmentsB[recordB.startSegment + (global_id.x - segmentCountA)];
    }
    
    var t_values = array<f32, 16>();
    t_values[0] = 0.0;
    t_values[1] = 1.0;
    var count = 2u;
    
    if (isA) {
        for (var j: u32 = 0u; j < segmentCountB; j = j + 1u) {
            let otherSeg = segmentsB[recordB.startSegment + j];
            let stepsA = select(16u, 1u, seg.segmentType == 0u);
            let stepsB = select(16u, 1u, otherSeg.segmentType == 0u);
            
            for (var stepA: u32 = 0u; stepA < stepsA; stepA = stepA + 1u) {
                let t0 = f32(stepA) / f32(stepsA); let t1 = f32(stepA + 1u) / f32(stepsA);
                let cp0 = evaluate_segment(seg, t0); let cp1 = evaluate_segment(seg, t1);
                
                for (var stepB: u32 = 0u; stepB < stepsB; stepB = stepB + 1u) {
                    let u0 = f32(stepB) / f32(stepsB); let u1 = f32(stepB + 1u) / f32(stepsB);
                    let cq0 = evaluate_segment(otherSeg, u0); let cq1 = evaluate_segment(otherSeg, u1);
                    
                    var t_sub: f32 = 0.0; var u_sub: f32 = 0.0;
                    if (intersect_lines(cp0, cp1, cq0, cq1, &t_sub, &u_sub)) {
                        let t_intersect = clamp(t0 + t_sub * (t1 - t0), 0.0, 1.0);
                        var duplicate = false;
                        for (var d: u32 = 0u; d < count; d = d + 1u) {
                            if (abs(t_values[d] - t_intersect) < 0.001) { duplicate = true; break; }
                        }
                        if (!duplicate && count < 16u) {
                            t_values[count] = t_intersect;
                            count = count + 1u;
                        }
                    }
                }
            }
        }
    } else {
        for (var i: u32 = 0u; i < segmentCountA; i = i + 1u) {
            let otherSeg = segmentsA[recordA.startSegment + i];
            let stepsA = select(16u, 1u, seg.segmentType == 0u);
            let stepsB = select(16u, 1u, otherSeg.segmentType == 0u);
            
            for (var stepA: u32 = 0u; stepA < stepsA; stepA = stepA + 1u) {
                let t0 = f32(stepA) / f32(stepsA); let t1 = f32(stepA + 1u) / f32(stepsA);
                let cp0 = evaluate_segment(seg, t0); let cp1 = evaluate_segment(seg, t1);
                
                for (var stepB: u32 = 0u; stepB < stepsB; stepB = stepB + 1u) {
                    let u0 = f32(stepB) / f32(stepsB); let u1 = f32(stepB + 1u) / f32(stepsB);
                    let cq0 = evaluate_segment(otherSeg, u0); let cq1 = evaluate_segment(otherSeg, u1);
                    
                    var t_sub: f32 = 0.0; var u_sub: f32 = 0.0;
                    if (intersect_lines(cp0, cp1, cq0, cq1, &t_sub, &u_sub)) {
                        let t_intersect = clamp(t0 + t_sub * (t1 - t0), 0.0, 1.0);
                        var duplicate = false;
                        for (var d: u32 = 0u; d < count; d = d + 1u) {
                            if (abs(t_values[d] - t_intersect) < 0.001) { duplicate = true; break; }
                        }
                        if (!duplicate && count < 16u) {
                            t_values[count] = t_intersect;
                            count = count + 1u;
                        }
                    }
                }
            }
        }
    }
    
    // Sort t_values using simple bubble sort
    if (count > 2u) {
        for (var step: u32 = 0u; step < count - 1u; step = step + 1u) {
            for (var idx: u32 = 0u; idx < count - step - 1u; idx = idx + 1u) {
                if (t_values[idx] > t_values[idx + 1u]) {
                    let temp = t_values[idx];
                    t_values[idx] = t_values[idx + 1u];
                    t_values[idx + 1u] = temp;
                }
            }
        }
    }
    
    // Classify and output sub-segments
    for (var k: u32 = 0u; k < count - 1u; k = k + 1u) {
        let t0 = t_values[k];
        let t1 = t_values[k + 1u];
        let sub = get_sub_segment(seg, t0, t1);
        let mid = evaluate_segment(sub, 0.5);
        let mid_perturbed = mid + vec2<f32>(1e-4, 1.5e-4);
        
        var inside = false;
        if (isA) {
            inside = is_point_inside_B(mid_perturbed);
        } else {
            inside = is_point_inside_A(mid_perturbed);
        }
        
        var keep = false;
        var rev = false;
        let op = uniforms.op;
        
        if (op == 0u) { // Difference (A - B)
            if (isA) { keep = !inside; }
            else { keep = inside; rev = true; }
        } else if (op == 1u) { // Intersect
            keep = inside;
        } else if (op == 2u) { // Union
            keep = !inside;
        } else if (op == 3u) { // XOR
            keep = true;
            if (inside) { rev = true; }
        } else if (op == 4u) { // Reverse Difference (B - A)
            if (isA) { keep = inside; rev = true; }
            else { keep = !inside; }
        }
        
        if (keep) {
            var finalSeg = sub;
            if (rev) {
                finalSeg = reverse_segment(sub);
            }
            let destIdx = atomicAdd(&destSegments.count, 1u);
            destSegments.segments[destIdx] = finalSeg;
        }
    }
}
";

    public const string PathOpRecordFinalizerShader = @"
struct PathOpUniforms {
    op: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

struct PathRecord {
    startSegment: u32,
    segmentCount: u32,
    minX: f32,
    minY: f32,
    maxX: f32,
    maxY: f32,
    _pad0: u32,
    _pad1: u32,
};

struct OutputSegments {
    count: u32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
};

@group(0) @binding(0) var<uniform> uniforms: PathOpUniforms;
@group(0) @binding(1) var<storage, read> recordA: PathRecord;
@group(0) @binding(2) var<storage, read> recordB: PathRecord;
@group(0) @binding(3) var<storage, read_write> destRecord: PathRecord;
@group(0) @binding(4) var<storage, read> destSegments: OutputSegments;

@compute @workgroup_size(1)
fn cs_main() {
    destRecord.startSegment = 0u;
    destRecord.segmentCount = destSegments.count;
    
    let op = uniforms.op;
    if (op == 0u) { // Difference (A - B)
        destRecord.minX = recordA.minX;
        destRecord.minY = recordA.minY;
        destRecord.maxX = recordA.maxX;
        destRecord.maxY = recordA.maxY;
    } else if (op == 4u) { // Reverse Difference (B - A)
        destRecord.minX = recordB.minX;
        destRecord.minY = recordB.minY;
        destRecord.maxX = recordB.maxX;
        destRecord.maxY = recordB.maxY;
    } else if (op == 1u) { // Intersect
        destRecord.minX = max(recordA.minX, recordB.minX);
        destRecord.minY = max(recordA.minY, recordB.minY);
        destRecord.maxX = min(recordA.maxX, recordB.maxX);
        destRecord.maxY = min(recordA.maxY, recordB.maxY);
    } else { // Union / XOR
        destRecord.minX = min(recordA.minX, recordB.minX);
        destRecord.minY = min(recordA.minY, recordB.minY);
        destRecord.maxX = max(recordA.maxX, recordB.maxX);
        destRecord.maxY = max(recordA.maxY, recordB.maxY);
    }
}
";

}

