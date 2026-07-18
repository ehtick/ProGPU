using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace ProGPU.Compute;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GpuSceneDrawMetadata
{
    public Vector4 Bounds;
    public Vector4 ClipBounds;
    public uint TransformIndex;
    public uint SourceIndex;
    public uint MaterialKey;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct GpuDrawIndirectArgs
{
    public uint VertexCount;
    public uint InstanceCount;
    public uint FirstVertex;
    public uint FirstInstance;
}

/// <summary>
/// Persistent GPU visibility, stable compaction, and homogeneous indirect-argument generation.
/// The buffers and bind groups grow geometrically; a normal frame performs only bounded writes and
/// records cull, hierarchical scan, and scatter work into the caller's command encoder.
/// </summary>
public unsafe sealed class GpuSceneVisibility : IDisposable
{
    public const uint HasClipFlag = 1u;

    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private struct CullParams
    {
        [FieldOffset(0)] public Matrix4x4 RootTransform;
        [FieldOffset(64)] public Vector4 Viewport;
        [FieldOffset(80)] public uint DrawCount;
        [FieldOffset(84)] public uint TransformCount;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct ScatterParams
    {
        [FieldOffset(0)] public uint DrawCount;
        [FieldOffset(4)] public uint VertexCount;
        [FieldOffset(8)] public uint FirstVertex;
        [FieldOffset(12)] public uint FirstInstance;
    }

    private readonly WgpuContext _context;
    private readonly RenderPipelineCache _pipelineCache;
    private readonly GpuPrefixScan _prefixScan;
    private readonly ComputePipeline* _cullPipeline;
    private readonly ComputePipeline* _scatterPipeline;
    private readonly BindGroupLayout* _cullLayout;
    private readonly BindGroupLayout* _scatterLayout;
    private readonly GpuBuffer _cullParamsBuffer;
    private readonly GpuBuffer _scatterParamsBuffer;
    private GpuBuffer? _metadataBuffer;
    private GpuBuffer? _transformBuffer;
    private GpuBuffer? _visibleSourceBuffer;
    private GpuBuffer? _visibleMaterialBuffer;
    private GpuBuffer? _visibleCountBuffer;
    private GpuBuffer? _indirectBuffer;
    private BindGroup* _cullBindGroup;
    private BindGroup* _scatterBindGroup;
    private uint _drawCapacity;
    private uint _transformCapacity;
    private bool _disposed;

    public GpuSceneVisibility(
        WgpuContext context,
        uint initialDrawCapacity = GpuPrefixScan.ElementsPerBlock,
        uint initialTransformCapacity = 64)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pipelineCache = new RenderPipelineCache(context);
        var cullModule = _pipelineCache.GetOrCreateShader(
            "SceneCull",
            ComputeShaders.SceneCull,
            "SceneCull");
        var scatterModule = _pipelineCache.GetOrCreateShader(
            "SceneScatter",
            ComputeShaders.SceneScatter,
            "SceneScatter");
        _cullPipeline = _pipelineCache.GetOrCreateComputePipeline(
            "SceneCull",
            cullModule,
            "scene_cull");
        _scatterPipeline = _pipelineCache.GetOrCreateComputePipeline(
            "SceneScatter",
            scatterModule,
            "scene_scatter");
        _cullLayout = _context.Api.ComputePipelineGetBindGroupLayout(_cullPipeline, 0);
        _scatterLayout = _context.Api.ComputePipelineGetBindGroupLayout(_scatterPipeline, 0);
        _cullParamsBuffer = new GpuBuffer(
            context,
            96,
            BufferUsage.Uniform | BufferUsage.CopyDst,
            "Scene Cull Parameters");
        _scatterParamsBuffer = new GpuBuffer(
            context,
            16,
            BufferUsage.Uniform | BufferUsage.CopyDst,
            "Scene Scatter Parameters");
        _prefixScan = new GpuPrefixScan(context, Math.Max(1u, initialDrawCapacity));
        Rebuild(
            Math.Max(1u, initialDrawCapacity),
            Math.Max(1u, initialTransformCapacity));
    }

    public uint DrawCapacity => _drawCapacity;

    public uint TransformCapacity => _transformCapacity;

    public GpuBuffer VisibleSourceBuffer =>
        _visibleSourceBuffer ?? throw new ObjectDisposedException(nameof(GpuSceneVisibility));

    public GpuBuffer VisibleMaterialBuffer =>
        _visibleMaterialBuffer ?? throw new ObjectDisposedException(nameof(GpuSceneVisibility));

    public GpuBuffer VisibleCountBuffer =>
        _visibleCountBuffer ?? throw new ObjectDisposedException(nameof(GpuSceneVisibility));

    public GpuBuffer IndirectBuffer =>
        _indirectBuffer ?? throw new ObjectDisposedException(nameof(GpuSceneVisibility));

    public void Upload(
        ReadOnlySpan<GpuSceneDrawMetadata> draws,
        ReadOnlySpan<Matrix4x4> transforms)
    {
        EnsureCapacity(
            checked((uint)Math.Max(1, draws.Length)),
            checked((uint)Math.Max(1, transforms.Length)));
        if (!draws.IsEmpty)
        {
            _metadataBuffer!.Write(draws);
        }
        if (!transforms.IsEmpty)
        {
            _transformBuffer!.Write(transforms);
        }
    }

    public void Record(
        CommandEncoder* encoder,
        uint drawCount,
        uint transformCount,
        Vector4 viewport,
        uint vertexCount,
        uint firstVertex = 0,
        uint firstInstance = 0)
    {
        Record(
            encoder,
            drawCount,
            transformCount,
            viewport,
            Matrix4x4.Identity,
            vertexCount,
            firstVertex,
            firstInstance);
    }

    public void Record(
        CommandEncoder* encoder,
        uint drawCount,
        uint transformCount,
        Vector4 viewport,
        Matrix4x4 rootTransform,
        uint vertexCount,
        uint firstVertex = 0,
        uint firstInstance = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder == null) throw new ArgumentNullException(nameof(encoder));
        if (drawCount > _drawCapacity) throw new ArgumentOutOfRangeException(nameof(drawCount));
        if (transformCount > _transformCapacity) throw new ArgumentOutOfRangeException(nameof(transformCount));

        _visibleCountBuffer!.WriteSingle(0u);
        _indirectBuffer!.WriteSingle(new GpuDrawIndirectArgs
        {
            VertexCount = vertexCount,
            InstanceCount = 0,
            FirstVertex = firstVertex,
            FirstInstance = firstInstance
        });
        if (drawCount == 0)
        {
            return;
        }

        _cullParamsBuffer.WriteSingle(new CullParams
        {
            RootTransform = rootTransform,
            Viewport = viewport,
            DrawCount = drawCount,
            TransformCount = transformCount
        });
        _scatterParamsBuffer.WriteSingle(new ScatterParams
        {
            DrawCount = drawCount,
            VertexCount = vertexCount,
            FirstVertex = firstVertex,
            FirstInstance = firstInstance
        });

        var passDescriptor = new ComputePassDescriptor();
        var cullPass = _context.Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
        if (cullPass == null)
        {
            throw new InvalidOperationException("Failed to begin the scene-cull compute pass.");
        }
        _context.Api.ComputePassEncoderSetPipeline(cullPass, _cullPipeline);
        _context.Api.ComputePassEncoderSetBindGroup(cullPass, 0, _cullBindGroup, 0, null);
        _context.Api.ComputePassEncoderDispatchWorkgroups(cullPass, DivRoundUp(drawCount, 256u), 1, 1);
        _context.Api.ComputePassEncoderEnd(cullPass);
        _context.Api.ComputePassEncoderRelease(cullPass);

        _prefixScan.RecordExclusiveScan(encoder, drawCount);

        var scatterPass = _context.Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
        if (scatterPass == null)
        {
            throw new InvalidOperationException("Failed to begin the scene-scatter compute pass.");
        }
        _context.Api.ComputePassEncoderSetPipeline(scatterPass, _scatterPipeline);
        _context.Api.ComputePassEncoderSetBindGroup(scatterPass, 0, _scatterBindGroup, 0, null);
        _context.Api.ComputePassEncoderDispatchWorkgroups(scatterPass, DivRoundUp(drawCount, 256u), 1, 1);
        _context.Api.ComputePassEncoderEnd(scatterPass);
        _context.Api.ComputePassEncoderRelease(scatterPass);
    }

    public static int CompactVisibleCpu(
        ReadOnlySpan<GpuSceneDrawMetadata> draws,
        ReadOnlySpan<Matrix4x4> transforms,
        Vector4 viewport,
        Span<uint> visibleSources,
        Span<uint> visibleMaterials)
    {
        return CompactVisibleCpu(
            draws,
            transforms,
            viewport,
            Matrix4x4.Identity,
            visibleSources,
            visibleMaterials);
    }

    public static int CompactVisibleCpu(
        ReadOnlySpan<GpuSceneDrawMetadata> draws,
        ReadOnlySpan<Matrix4x4> transforms,
        Vector4 viewport,
        Matrix4x4 rootTransform,
        Span<uint> visibleSources,
        Span<uint> visibleMaterials)
    {
        if (visibleSources.Length < draws.Length)
        {
            throw new ArgumentException("The source output span is too short.", nameof(visibleSources));
        }
        if (visibleMaterials.Length < draws.Length)
        {
            throw new ArgumentException("The material output span is too short.", nameof(visibleMaterials));
        }

        int visibleCount = 0;
        for (int index = 0; index < draws.Length; index++)
        {
            ref readonly var draw = ref draws[index];
            if (draw.TransformIndex >= transforms.Length)
            {
                continue;
            }

            var bounds = draw.Bounds;
            var transform = transforms[checked((int)draw.TransformIndex)] * rootTransform;
            var p0 = Vector2.Transform(new Vector2(bounds.X, bounds.Y), transform);
            var p1 = Vector2.Transform(new Vector2(bounds.Z, bounds.Y), transform);
            var p2 = Vector2.Transform(new Vector2(bounds.Z, bounds.W), transform);
            var p3 = Vector2.Transform(new Vector2(bounds.X, bounds.W), transform);
            var minimum = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
            var maximum = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
            minimum = Vector2.Max(minimum, new Vector2(viewport.X, viewport.Y));
            maximum = Vector2.Min(maximum, new Vector2(viewport.Z, viewport.W));
            if ((draw.Flags & HasClipFlag) != 0)
            {
                minimum = Vector2.Max(minimum, new Vector2(draw.ClipBounds.X, draw.ClipBounds.Y));
                maximum = Vector2.Min(maximum, new Vector2(draw.ClipBounds.Z, draw.ClipBounds.W));
            }

            if (!IsFinite(minimum) || !IsFinite(maximum) ||
                maximum.X <= minimum.X || maximum.Y <= minimum.Y)
            {
                continue;
            }

            visibleSources[visibleCount] = draw.SourceIndex;
            visibleMaterials[visibleCount] = draw.MaterialKey;
            visibleCount++;
        }
        return visibleCount;
    }

    private void EnsureCapacity(uint drawCount, uint transformCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (drawCount <= _drawCapacity && transformCount <= _transformCapacity)
        {
            return;
        }

        uint drawCapacity = GrowCapacity(_drawCapacity, drawCount);
        uint transformCapacity = GrowCapacity(_transformCapacity, transformCount);
        Rebuild(drawCapacity, transformCapacity);
    }

    private void Rebuild(uint drawCapacity, uint transformCapacity)
    {
        ReleaseSceneBuffers();
        _prefixScan.EnsureCapacity(drawCapacity);
        _drawCapacity = drawCapacity;
        _transformCapacity = transformCapacity;
        _metadataBuffer = CreateStorageBuffer<GpuSceneDrawMetadata>(drawCapacity, "Scene Draw Metadata");
        _transformBuffer = CreateStorageBuffer<Matrix4x4>(transformCapacity, "Scene Draw Transforms");
        _visibleSourceBuffer = CreateStorageBuffer<uint>(
            drawCapacity,
            "Visible Scene Source Indices",
            copySource: true);
        _visibleMaterialBuffer = CreateStorageBuffer<uint>(
            drawCapacity,
            "Visible Scene Material Keys",
            copySource: true);
        _visibleCountBuffer = CreateStorageBuffer<uint>(1, "Visible Scene Count", copySource: true);
        _indirectBuffer = new GpuBuffer(
            _context,
            (uint)Marshal.SizeOf<GpuDrawIndirectArgs>(),
            BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc | BufferUsage.Indirect,
            "Visible Scene Indirect Arguments");
        _cullBindGroup = CreateCullBindGroup();
        _scatterBindGroup = CreateScatterBindGroup();
    }

    private BindGroup* CreateCullBindGroup()
    {
        var entries = stackalloc BindGroupEntry[4];
        entries[0] = BufferEntry(0, _cullParamsBuffer);
        entries[1] = BufferEntry(1, _metadataBuffer!);
        entries[2] = BufferEntry(2, _transformBuffer!);
        entries[3] = BufferEntry(3, _prefixScan.InputBuffer);
        return CreateBindGroup(_cullLayout, entries, 4, "scene-cull");
    }

    private BindGroup* CreateScatterBindGroup()
    {
        var entries = stackalloc BindGroupEntry[8];
        entries[0] = BufferEntry(0, _scatterParamsBuffer);
        entries[1] = BufferEntry(1, _metadataBuffer!);
        entries[2] = BufferEntry(2, _prefixScan.InputBuffer);
        entries[3] = BufferEntry(3, _prefixScan.OutputBuffer);
        entries[4] = BufferEntry(4, _visibleSourceBuffer!);
        entries[5] = BufferEntry(5, _visibleMaterialBuffer!);
        entries[6] = BufferEntry(6, _visibleCountBuffer!);
        entries[7] = BufferEntry(7, _indirectBuffer!);
        return CreateBindGroup(_scatterLayout, entries, 8, "scene-scatter");
    }

    private BindGroup* CreateBindGroup(
        BindGroupLayout* layout,
        BindGroupEntry* entries,
        uint count,
        string operation)
    {
        var descriptor = new BindGroupDescriptor
        {
            Layout = layout,
            EntryCount = count,
            Entries = entries
        };
        var bindGroup = _context.Api.DeviceCreateBindGroup(_context.Device, &descriptor);
        if (bindGroup == null)
        {
            throw new InvalidOperationException($"Failed to create the {operation} bind group.");
        }
        return bindGroup;
    }

    private static BindGroupEntry BufferEntry(uint binding, GpuBuffer buffer) => new()
    {
        Binding = binding,
        Buffer = buffer.BufferPtr,
        Offset = 0,
        Size = buffer.Size
    };

    private GpuBuffer CreateStorageBuffer<T>(
        uint count,
        string label,
        bool copySource = false) where T : unmanaged
    {
        var usage = BufferUsage.Storage | BufferUsage.CopyDst;
        if (copySource) usage |= BufferUsage.CopySrc;
        return new GpuBuffer(
            _context,
            checked(Math.Max(1u, count) * (uint)Marshal.SizeOf<T>()),
            usage,
            label);
    }

    private void ReleaseSceneBuffers()
    {
        if (_cullBindGroup != null)
        {
            _context.Api.BindGroupRelease(_cullBindGroup);
            _cullBindGroup = null;
        }
        if (_scatterBindGroup != null)
        {
            _context.Api.BindGroupRelease(_scatterBindGroup);
            _scatterBindGroup = null;
        }
        _metadataBuffer?.Dispose();
        _transformBuffer?.Dispose();
        _visibleSourceBuffer?.Dispose();
        _visibleMaterialBuffer?.Dispose();
        _visibleCountBuffer?.Dispose();
        _indirectBuffer?.Dispose();
        _metadataBuffer = null;
        _transformBuffer = null;
        _visibleSourceBuffer = null;
        _visibleMaterialBuffer = null;
        _visibleCountBuffer = null;
        _indirectBuffer = null;
    }

    private static uint GrowCapacity(uint current, uint required)
    {
        uint capacity = Math.Max(1u, current);
        while (capacity < required)
        {
            capacity = checked(capacity * 2u);
        }
        return capacity;
    }

    private static uint DivRoundUp(uint value, uint divisor) =>
        checked((value + divisor - 1u) / divisor);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseSceneBuffers();
        _cullParamsBuffer.Dispose();
        _scatterParamsBuffer.Dispose();
        _prefixScan.Dispose();
        _context.Api.BindGroupLayoutRelease(_cullLayout);
        _context.Api.BindGroupLayoutRelease(_scatterLayout);
        _pipelineCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
