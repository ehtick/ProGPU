using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using ProGPU.Vector;
using ProGPU.Backend;

namespace ProGPU.Scene.Extensions
{
    public class CustomGridExtensionPipeline : ICompositorExtension
    {
        private const string GridShaderCode = @"
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) texCoord: vec2<f32>,
    @location(1) color: vec4<f32>,
};

@vertex
fn vs_main(
    @location(0) position: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texCoord: vec2<f32>,
) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(position, 0.0, 1.0);
    output.texCoord = texCoord;
    output.color = color;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // Modern glassmorphic background
    let bgColor = vec4<f32>(0.05, 0.05, 0.08, 0.95);

    // Grid coordinates
    let gridCount = 25.0;
    let grid = abs(fract(input.texCoord * gridCount - 0.5) - 0.5) / fwidth(input.texCoord * gridCount);
    let lineVal = min(grid.x, grid.y);
    
    // Smooth grid lines
    let lineAlpha = 1.0 - min(lineVal, 1.0);
    let lineColor = vec4<f32>(0.0, 0.6, 0.8, 0.15 * lineAlpha);

    // Glowing intersections
    let distToIntersection = length(fract(input.texCoord * gridCount - 0.5) - 0.5);
    let glow = exp(-distToIntersection * 12.0) * 0.4;
    let glowColor = vec4<f32>(0.0, 0.8, 1.0, glow);

    // Subtle pulsing center radial glow
    let centerDist = distance(input.texCoord, vec2<f32>(0.5, 0.5));
    let centerGlow = exp(-centerDist * 2.5) * 0.2;
    let centerGlowColor = vec4<f32>(0.0, 0.5, 0.8, centerGlow);

    var finalColor = mix(bgColor, lineColor, lineAlpha);
    finalColor = finalColor + glowColor + centerGlowColor;

    return finalColor;
}
";

        private unsafe RenderPipeline* _cachedPipeline;
        private unsafe RenderPipeline* _cachedPipelineOffscreen;

        public void Compile(
            Compositor compositor,
            IRenderDataProvider? provider,
            Matrix4x4 transform,
            ref RenderCommand cmd)
        {
            // Allocate viewport quad vertices: Position from -1 to 1 in normalized device coords (NDC)
            int startIndex = compositor.VectorVertices.Count;
            float dummyBrush = 0f;
            var color = new Vector4(1f, 1f, 1f, 1f);

            int originalVertexCount = compositor.VectorVertices.Count;
            CollectionsMarshal.SetCount(compositor.VectorVertices, originalVertexCount + 4);
            var vertexSpan = CollectionsMarshal.AsSpan(compositor.VectorVertices).Slice(originalVertexCount, 4);

            vertexSpan[0] = new VectorVertex(new Vector2(-1f, -1f), color, new Vector2(0f, 0f), dummyBrush, default, 0f, 0f, 7f);
            vertexSpan[1] = new VectorVertex(new Vector2(1f, -1f), color, new Vector2(1f, 0f), dummyBrush, default, 0f, 0f, 7f);
            vertexSpan[2] = new VectorVertex(new Vector2(1f, 1f), color, new Vector2(1f, 1f), dummyBrush, default, 0f, 0f, 7f);
            vertexSpan[3] = new VectorVertex(new Vector2(-1f, 1f), color, new Vector2(0f, 1f), dummyBrush, default, 0f, 0f, 7f);

            int originalIndexCount = compositor.VectorIndices.Count;
            CollectionsMarshal.SetCount(compositor.VectorIndices, originalIndexCount + 6);
            var indexSpan = CollectionsMarshal.AsSpan(compositor.VectorIndices).Slice(originalIndexCount, 6);

            indexSpan[0] = (uint)startIndex;
            indexSpan[1] = (uint)(startIndex + 1);
            indexSpan[2] = (uint)(startIndex + 2);
            indexSpan[3] = (uint)startIndex;
            indexSpan[4] = (uint)(startIndex + 2);
            indexSpan[5] = (uint)(startIndex + 3);
        }

        public unsafe void Render(
            Compositor compositor,
            void* renderPassEncoder,
            bool isOffscreen,
            in Compositor.CompositorDrawCall dc)
        {
            var wgpu = compositor.Context.Wgpu;
            var pass = (RenderPassEncoder*)renderPassEncoder;

            var activePipeline = isOffscreen ? _cachedPipelineOffscreen : _cachedPipeline;
            if (activePipeline == null)
            {
                var shaderModule = compositor.PipelineCache.GetOrCreateShader("CustomGridShader", GridShaderCode, "Custom Grid WGSL Shader");
                
                var layouts = new VertexBufferLayout[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = (uint)Marshal.SizeOf<VectorVertex>(),
                        StepMode = VertexStepMode.Vertex,
                        AttributeCount = 8,
                        Attributes = (VertexAttribute*)Marshal.AllocHGlobal(Marshal.SizeOf<VertexAttribute>() * 8)
                    }
                };

                // Populate attributes matching VectorVertex structure
                var attrs = layouts[0].Attributes;
                attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 }; // Position
                attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 8, ShaderLocation = 1 }; // Color
                attrs[2] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 24, ShaderLocation = 2 }; // TexCoord
                attrs[3] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 32, ShaderLocation = 3 };   // BrushIndex
                attrs[4] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 36, ShaderLocation = 4 }; // ShapeSize
                attrs[5] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 44, ShaderLocation = 5 };   // CornerRadius
                attrs[6] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 48, ShaderLocation = 6 };   // StrokeThickness
                attrs[7] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 52, ShaderLocation = 7 };   // ShapeType

                var pipeline = compositor.PipelineCache.GetOrCreateRenderPipeline(
                    isOffscreen ? "CustomGridPipeline_Offscreen" : "CustomGridPipeline",
                    shaderModule,
                    vertexBufferLayouts: layouts,
                    topology: PrimitiveTopology.TriangleList,
                    targetFormat: isOffscreen ? TextureFormat.Rgba8Unorm : compositor.Context.SwapChainFormat,
                    sampleCount: isOffscreen ? 1u : 4u
                );

                Marshal.FreeHGlobal((IntPtr)layouts[0].Attributes);

                if (isOffscreen)
                {
                    _cachedPipelineOffscreen = pipeline;
                    activePipeline = _cachedPipelineOffscreen;
                }
                else
                {
                    _cachedPipeline = pipeline;
                    activePipeline = _cachedPipeline;
                }
            }

            wgpu.RenderPassEncoderSetPipeline(pass, activePipeline);
            wgpu.RenderPassEncoderDrawIndexed(pass, 6, 1, (uint)dc.PointBufferOffset, 0, 0);
        }
    }
}
