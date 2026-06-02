using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Silk.NET.WebGPU;
using Silk.NET.Core.Native;
using ProGPU.Vector;
using ProGPU.Backend;

namespace ProGPU.Scene.Extensions
{
    public enum RenderMode3D
    {
        Solid = 0,
        Wireframe = 1,
        SolidWireframe = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GpuVertex3D
    {
        public Vector3 Position;
        public Vector3 Normal;

        public GpuVertex3D(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct GpuMesh3DRecord
    {
        public Matrix4x4 ModelTransform;      // 3D Model transform for lighting
        public Vector4 Color;
        public Vector4 LightDirection;        // xyz = direction, w = intensity
        public Vector4 AmbientColor;          // rgb = color, w = intensity
        public float Opacity;
        public float IsWireframe;             // 1.0f if wireframe, 0.0f otherwise
        private float _pad1;
        private float _pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct GpuMesh3DUniforms
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
    }

    public class Mesh3DExtensionPipeline : ICompositorExtension
    {
        private const string Mesh3DShaderCode = @"
struct VSUniforms {
    projection: mat4x4<f32>,
    view: mat4x4<f32>,
};

struct GpuMesh3DRecord {
    modelTransform: mat4x4<f32>,
    color: vec4<f32>,
    lightDirection: vec4<f32>,
    ambientColor: vec4<f32>,
    opacity: f32,
    isWireframe: f32,
    _pad1: f32,
    _pad2: f32,
};

@group(0) @binding(0) var<uniform> uniforms: VSUniforms;
@group(0) @binding(1) var<storage, read> meshRecords: array<GpuMesh3DRecord>;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
};

@vertex
fn vs_main(input: VertexInput, @builtin(instance_index) instanceIdx: u32) -> VertexOutput {
    var output: VertexOutput;
    let record = meshRecords[instanceIdx];

    // Transform position and normal to world space
    var localPos = input.position;
    if (record.isWireframe > 0.5) {
        localPos += input.normal * 0.003; // Tiny normal offset to prevent z-fighting
    }

    let worldPos = record.modelTransform * vec4<f32>(localPos, 1.0);
    let worldNormal = normalize((record.modelTransform * vec4<f32>(input.normal, 0.0)).xyz);

    // Dynamic 3D depth matrices transform
    output.position = uniforms.projection * uniforms.view * worldPos;

    // Calculate Gouraud diffuse + ambient lighting
    let lightDir = normalize(record.lightDirection.xyz);
    let diffuse = max(dot(worldNormal, lightDir), 0.0) * record.lightDirection.w;
    
    let ambient = record.ambientColor.rgb * record.ambientColor.w;
    var litColor = (ambient + diffuse * record.color.rgb) * record.opacity;

    if (record.isWireframe > 0.5) {
        litColor = litColor * 1.4 + vec3<f32>(0.05, 0.05, 0.05); // Boost wireframe overlay brightness
    }

    output.color = vec4<f32>(litColor, record.opacity);
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return input.color;
}
";

        private struct CachedGeometry
        {
            public GpuBuffer VertexBuffer;
            public GpuBuffer IndexBuffer;
            public GpuBuffer? LineIndexBuffer;
            public uint LineIndexCount;
        }

        private readonly Dictionary<object, CachedGeometry> _geometryCache = new();
        private GpuBuffer? _dynamicRecordsBuffer;
        private GpuBuffer? _uniformsBuffer;
        
        private unsafe RenderPipeline* _cachedPipeline;
        private unsafe RenderPipeline* _cachedWireframePipeline;
        private unsafe BindGroup* _cachedBindGroup;
        private unsafe BindGroup* _cachedWireframeBindGroup;
        private int _cachedRecordGen = -1;

        public void BeginFrame(Compositor compositor)
        {
        }

        public void Dispose()
        {
            foreach (var cache in _geometryCache.Values)
            {
                cache.VertexBuffer.Dispose();
                cache.IndexBuffer.Dispose();
                cache.LineIndexBuffer?.Dispose();
            }
            _geometryCache.Clear();

            _dynamicRecordsBuffer?.Dispose();
            _uniformsBuffer?.Dispose();
        }

        public unsafe void Compile(
            Compositor compositor,
            IRenderDataProvider? provider,
            Matrix4x4 transform,
            ref RenderCommand cmd)
        {
            var payload = cmd.DataParam as Viewport3DCompilationPayload;
            if (payload == null || payload.Meshes.Count == 0 || payload.ColorTexture == null || payload.DepthTexture == null) return;

            var wgpu = compositor.Context.Wgpu;
            var device = compositor.Context.Device;
            var queue = compositor.Context.Queue;

            // 1. Create or update dynamic record buffer and uniforms buffer
            int recordCount = payload.Meshes.Count;
            if (payload.RenderMode == RenderMode3D.SolidWireframe)
            {
                recordCount = payload.Meshes.Count * 2;
            }

            uint reqRecordsSize = (uint)recordCount * (uint)Marshal.SizeOf<GpuMesh3DRecord>();
            if (_dynamicRecordsBuffer == null || _dynamicRecordsBuffer.Size < reqRecordsSize)
            {
                _dynamicRecordsBuffer?.Dispose();
                _dynamicRecordsBuffer = new GpuBuffer(compositor.Context, reqRecordsSize * 2, BufferUsage.Storage | BufferUsage.CopyDst, "Dynamic Mesh3D Records Buffer");
            }

            uint uniformsSize = (uint)Marshal.SizeOf<GpuMesh3DUniforms>();
            if (_uniformsBuffer == null)
            {
                _uniformsBuffer = new GpuBuffer(compositor.Context, uniformsSize, BufferUsage.Uniform | BufferUsage.CopyDst, "Mesh3D Uniforms Buffer");
            }

            // 2. Upload records data
            var cpuRecords = new GpuMesh3DRecord[recordCount];
            int n = payload.Meshes.Count;
            for (int i = 0; i < n; i++)
            {
                var mesh = payload.Meshes[i];
                var solidRecord = new GpuMesh3DRecord
                {
                    ModelTransform = mesh.ModelTransform,
                    Color = mesh.Color,
                    LightDirection = new Vector4(payload.LightDirection, payload.LightIntensity),
                    AmbientColor = new Vector4(payload.AmbientColor, payload.AmbientIntensity),
                    Opacity = mesh.Opacity * compositor.ActiveOpacity,
                    IsWireframe = 0.0f
                };

                if (payload.RenderMode == RenderMode3D.Solid)
                {
                    cpuRecords[i] = solidRecord;
                }
                else if (payload.RenderMode == RenderMode3D.Wireframe)
                {
                    solidRecord.IsWireframe = 1.0f;
                    cpuRecords[i] = solidRecord;
                }
                else // SolidWireframe
                {
                    cpuRecords[i] = solidRecord;

                    var wireRecord = solidRecord;
                    wireRecord.IsWireframe = 1.0f;
                    cpuRecords[n + i] = wireRecord;
                }
            }
            _dynamicRecordsBuffer.Write(cpuRecords);

            // 3. Upload uniforms data
            var cpuUniforms = new GpuMesh3DUniforms
            {
                Projection = cmd.Transform, // Perspective projection matrix
                View = cmd.CameraView       // View matrix
            };
            _uniformsBuffer.WriteSingle(cpuUniforms);

            // 4. Create solid pipeline if needed
            if (_cachedPipeline == null)
            {
                var shaderModule = compositor.PipelineCache.GetOrCreateShader("Mesh3DShader_3D", Mesh3DShaderCode, "Mesh3D WGSL 3D Shader");

                var layouts = new VertexBufferLayout[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = (uint)Marshal.SizeOf<GpuVertex3D>(),
                        StepMode = VertexStepMode.Vertex,
                        AttributeCount = 2,
                        Attributes = (VertexAttribute*)Marshal.AllocHGlobal(Marshal.SizeOf<VertexAttribute>() * 2)
                    }
                };

                var attrs = layouts[0].Attributes;
                attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 }; // Position
                attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 }; // Normal

                _cachedPipeline = compositor.PipelineCache.GetOrCreateRenderPipeline(
                    "Mesh3DPipeline_3D",
                    shaderModule,
                    vertexBufferLayouts: layouts,
                    topology: PrimitiveTopology.TriangleList,
                    targetFormat: TextureFormat.Rgba8Unorm,
                    enableDepthStencil: true,
                    depthFormat: TextureFormat.Depth24PlusStencil8,
                    sampleCount: 1u,
                    depthWriteEnabled: true,
                    depthCompare: CompareFunction.Less
                );

                Marshal.FreeHGlobal((IntPtr)layouts[0].Attributes);
            }

            // Create wireframe pipeline if needed
            if (_cachedWireframePipeline == null)
            {
                var shaderModule = compositor.PipelineCache.GetOrCreateShader("Mesh3DShader_3D", Mesh3DShaderCode, "Mesh3D WGSL 3D Shader");

                var layouts = new VertexBufferLayout[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = (uint)Marshal.SizeOf<GpuVertex3D>(),
                        StepMode = VertexStepMode.Vertex,
                        AttributeCount = 2,
                        Attributes = (VertexAttribute*)Marshal.AllocHGlobal(Marshal.SizeOf<VertexAttribute>() * 2)
                    }
                };

                var attrs = layouts[0].Attributes;
                attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 }; // Position
                attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 }; // Normal

                _cachedWireframePipeline = compositor.PipelineCache.GetOrCreateRenderPipeline(
                    "Mesh3DWireframePipeline_3D",
                    shaderModule,
                    vertexBufferLayouts: layouts,
                    topology: PrimitiveTopology.LineList,
                    targetFormat: TextureFormat.Rgba8Unorm,
                    enableDepthStencil: true,
                    depthFormat: TextureFormat.Depth24PlusStencil8,
                    sampleCount: 1u,
                    depthWriteEnabled: true,
                    depthCompare: CompareFunction.LessEqual
                );

                Marshal.FreeHGlobal((IntPtr)layouts[0].Attributes);
            }

            // 5. Create or get cached BindGroup
            int currentGen = _dynamicRecordsBuffer.GetHashCode() ^ _uniformsBuffer.GetHashCode();
            if (_cachedBindGroup == null || _cachedWireframeBindGroup == null || currentGen != _cachedRecordGen)
            {
                _cachedRecordGen = currentGen;

                var bgEntries = stackalloc BindGroupEntry[2];
                bgEntries[0] = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _uniformsBuffer.BufferPtr,
                    Offset = 0,
                    Size = uniformsSize
                };
                bgEntries[1] = new BindGroupEntry
                {
                    Binding = 1,
                    Buffer = _dynamicRecordsBuffer.BufferPtr,
                    Offset = 0,
                    Size = _dynamicRecordsBuffer.Size
                };

                // Bind group for Solid Pipeline
                var pipelineLayout = wgpu.RenderPipelineGetBindGroupLayout(_cachedPipeline, 0);
                var bgDesc = new BindGroupDescriptor
                {
                    Layout = pipelineLayout,
                    EntryCount = 2,
                    Entries = bgEntries,
                    Label = (byte*)SilkMarshal.StringToPtr("Mesh3D 3D BindGroup")
                };

                if (_cachedBindGroup != null) wgpu.BindGroupRelease(_cachedBindGroup);
                _cachedBindGroup = wgpu.DeviceCreateBindGroup(device, &bgDesc);
                SilkMarshal.Free((nint)bgDesc.Label);

                // Bind group for Wireframe Pipeline
                var wireframeLayout = wgpu.RenderPipelineGetBindGroupLayout(_cachedWireframePipeline, 0);
                var wireframeBgDesc = new BindGroupDescriptor
                {
                    Layout = wireframeLayout,
                    EntryCount = 2,
                    Entries = bgEntries,
                    Label = (byte*)SilkMarshal.StringToPtr("Mesh3D Wireframe BindGroup")
                };

                if (_cachedWireframeBindGroup != null) wgpu.BindGroupRelease(_cachedWireframeBindGroup);
                _cachedWireframeBindGroup = wgpu.DeviceCreateBindGroup(device, &wireframeBgDesc);
                SilkMarshal.Free((nint)wireframeBgDesc.Label);
            }

            // 6. Begin offscreen WebGPU Render Pass targeting the custom color and depth textures!
            var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Mesh3D Offscreen Encoder") };
            var encoder = wgpu.DeviceCreateCommandEncoder(device, &encoderDesc);
            SilkMarshal.Free((nint)encoderDesc.Label);

            var colorAttachment = new RenderPassColorAttachment
            {
                View = payload.ColorTexture.ViewPtr,
                ResolveTarget = null,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Silk.NET.WebGPU.Color { R = 0.05f, G = 0.05f, B = 0.06f, A = 1.0f } // Slate premium dark background
            };

            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = payload.DepthTexture.ViewPtr,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Store,
                StencilClearValue = 0,
                StencilReadOnly = false
            };

            var passDesc = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthAttachment
            };

            var pass = wgpu.CommandEncoderBeginRenderPass(encoder, &passDesc);

            // 7. Compile mesh buffers on demand
            for (int i = 0; i < payload.Meshes.Count; i++)
            {
                var entry = payload.Meshes[i];
                if (entry.Geometry == null) continue;

                if (!_geometryCache.TryGetValue(entry.Geometry, out var cache))
                {
                    // Create Vertex Buffer
                    var cpuVertices = new GpuVertex3D[entry.Positions.Length];
                    for (int v = 0; v < cpuVertices.Length; v++)
                    {
                        var norm = v < entry.Normals.Length ? entry.Normals[v] : Vector3.UnitY;
                        cpuVertices[v] = new GpuVertex3D(entry.Positions[v], norm);
                    }

                    uint vSize = (uint)cpuVertices.Length * (uint)Marshal.SizeOf<GpuVertex3D>();
                    var vBuffer = new GpuBuffer(compositor.Context, vSize, BufferUsage.Vertex | BufferUsage.CopyDst, "3D Mesh Vertex Buffer");
                    vBuffer.Write(cpuVertices);

                    // Create Index Buffer
                    var cpuIndices = new uint[entry.Indices.Length];
                    for (int idx = 0; idx < cpuIndices.Length; idx++)
                    {
                        cpuIndices[idx] = (uint)entry.Indices[idx];
                    }

                    uint iSize = (uint)cpuIndices.Length * 4;
                    var iBuffer = new GpuBuffer(compositor.Context, iSize, BufferUsage.Index | BufferUsage.CopyDst, "3D Mesh Index Buffer");
                    iBuffer.Write(cpuIndices);

                    // Create Line Index Buffer
                    var cpuLineIndices = new uint[entry.Indices.Length * 2];
                    int lineIdx = 0;
                    for (int t = 0; t < entry.Indices.Length; t += 3)
                    {
                        if (t + 2 >= entry.Indices.Length) break;
                        uint i0 = (uint)entry.Indices[t];
                        uint i1 = (uint)entry.Indices[t + 1];
                        uint i2 = (uint)entry.Indices[t + 2];

                        cpuLineIndices[lineIdx++] = i0;
                        cpuLineIndices[lineIdx++] = i1;

                        cpuLineIndices[lineIdx++] = i1;
                        cpuLineIndices[lineIdx++] = i2;

                        cpuLineIndices[lineIdx++] = i2;
                        cpuLineIndices[lineIdx++] = i0;
                    }

                    uint iSizeLine = (uint)lineIdx * 4;
                    var iBufferLine = new GpuBuffer(compositor.Context, iSizeLine, BufferUsage.Index | BufferUsage.CopyDst, "3D Mesh Line Index Buffer");
                    iBufferLine.Write(new ReadOnlySpan<uint>(cpuLineIndices, 0, lineIdx));

                    cache = new CachedGeometry
                    {
                        VertexBuffer = vBuffer,
                        IndexBuffer = iBuffer,
                        LineIndexBuffer = iBufferLine,
                        LineIndexCount = (uint)lineIdx
                    };
                    _geometryCache[entry.Geometry] = cache;
                }
            }

            // Draw Passes
            var mode = payload.RenderMode;

            // Pass A: Draw Solid Shaded Triangles
            if (mode == RenderMode3D.Solid || mode == RenderMode3D.SolidWireframe)
            {
                wgpu.RenderPassEncoderSetPipeline(pass, _cachedPipeline);
                wgpu.RenderPassEncoderSetBindGroup(pass, 0, _cachedBindGroup, 0, null);
                for (int i = 0; i < payload.Meshes.Count; i++)
                {
                    var entry = payload.Meshes[i];
                    if (entry.Geometry == null) continue;

                    var cache = _geometryCache[entry.Geometry];

                    wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, cache.VertexBuffer.BufferPtr, 0, cache.VertexBuffer.Size);
                    wgpu.RenderPassEncoderSetIndexBuffer(pass, cache.IndexBuffer.BufferPtr, IndexFormat.Uint32, 0, cache.IndexBuffer.Size);
                    wgpu.RenderPassEncoderDrawIndexed(pass, (uint)entry.Indices.Length, 1, 0, 0, (uint)i);
                }
            }

            // Pass B: Draw Wireframe Outlines
            if (mode == RenderMode3D.Wireframe || mode == RenderMode3D.SolidWireframe)
            {
                wgpu.RenderPassEncoderSetPipeline(pass, _cachedWireframePipeline);
                wgpu.RenderPassEncoderSetBindGroup(pass, 0, _cachedWireframeBindGroup, 0, null);
                for (int i = 0; i < payload.Meshes.Count; i++)
                {
                    var entry = payload.Meshes[i];
                    if (entry.Geometry == null) continue;

                    var cache = _geometryCache[entry.Geometry];
                    if (cache.LineIndexBuffer == null || cache.LineIndexCount == 0) continue;

                    uint instanceIdx = (uint)i;
                    if (mode == RenderMode3D.SolidWireframe)
                    {
                        instanceIdx = (uint)(payload.Meshes.Count + i);
                    }

                    wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, cache.VertexBuffer.BufferPtr, 0, cache.VertexBuffer.Size);
                    wgpu.RenderPassEncoderSetIndexBuffer(pass, cache.LineIndexBuffer.BufferPtr, IndexFormat.Uint32, 0, cache.LineIndexBuffer.Size);
                    wgpu.RenderPassEncoderDrawIndexed(pass, cache.LineIndexCount, 1, 0, 0, instanceIdx);
                }
            }

            wgpu.RenderPassEncoderEnd(pass);
            wgpu.RenderPassEncoderRelease(pass);

            // 8. Submit offscreen command buffer to WebGPU queue immediately!
            var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Mesh3D Offscreen Command Buffer") };
            var cmdBuffer = wgpu.CommandEncoderFinish(encoder, &cmdDesc);
            SilkMarshal.Free((nint)cmdDesc.Label);

            wgpu.QueueSubmit(queue, 1, &cmdBuffer);

            wgpu.CommandBufferRelease(cmdBuffer);
            wgpu.CommandEncoderRelease(encoder);

            // DrawExtension is now a no-op in the main compositor pass since the offscreen pass is fully complete and
            // the Viewport3D control appends a separate DrawTexture command!
            cmd.PointBufferOffset = 0;
            cmd.PointBufferCount = 0;
        }

        public unsafe void Render(
            Compositor compositor,
            void* renderPassEncoder,
            bool isOffscreen,
            in Compositor.CompositorDrawCall dc)
        {
            // Fully no-op
        }
    }

    public class Viewport3DCompilationPayload
    {
        public Vector2 ViewportSize { get; set; } = new Vector2(400f, 300f);
        public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 1f, -0.5f);
        public float LightIntensity { get; set; } = 1.0f;
        public Vector3 AmbientColor { get; set; } = new Vector3(1f, 1f, 1f);
        public float AmbientIntensity { get; set; } = 0.2f;
        public List<MeshCompilationEntry> Meshes { get; } = new();

        public GpuTexture? ColorTexture { get; set; }
        public GpuTexture? DepthTexture { get; set; }
        
        public RenderMode3D RenderMode { get; set; } = RenderMode3D.Solid;
    }

    public class MeshCompilationEntry
    {
        public object? Geometry { get; set; }
        public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
        public Vector3[] Normals { get; set; } = Array.Empty<Vector3>();
        public int[] Indices { get; set; } = Array.Empty<int>();
        public Matrix4x4 ModelTransform { get; set; } = Matrix4x4.Identity;
        public Vector4 Color { get; set; } = Vector4.One;
        public float Opacity { get; set; } = 1.0f;
    }
}
