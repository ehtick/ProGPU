using System.Runtime.InteropServices;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Text.Shaping;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace ProGPU.Compute;

/// <summary>One already-decoded scalar and its UTF input cluster.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct GpuShapingScalar(
    uint CodePoint,
    int Cluster,
    ShapingGlyphFlags Flags = ShapingGlyphFlags.None,
    uint Reserved = 0);

/// <summary>GPU-resident immutable data for one compiled font plan.</summary>
public sealed class GpuOpenTypeFontData : IDisposable
{
    internal GpuBuffer CmapBuffer { get; }
    internal GpuBuffer MetricsBuffer { get; }
    public int CmapRangeCount { get; }
    public int GlyphMetricCount { get; }

    public GpuOpenTypeFontData(WgpuContext context, GpuOpenTypeShapingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        CmapRangeCount = plan.Cmap.Length;
        GlyphMetricCount = plan.Metrics.Length;
        uint cmapBytes = checked((uint)Math.Max(16, CmapRangeCount * Marshal.SizeOf<GpuCmapRange>()));
        uint metricBytes = checked((uint)Math.Max(16, GlyphMetricCount * Marshal.SizeOf<GpuGlyphMetrics>()));
        CmapBuffer = new GpuBuffer(context, cmapBytes, BufferUsage.Storage | BufferUsage.CopyDst, "OpenType cmap ranges");
        MetricsBuffer = new GpuBuffer(context, metricBytes, BufferUsage.Storage | BufferUsage.CopyDst, "OpenType glyph metrics");
        if (CmapRangeCount != 0) CmapBuffer.Write(plan.Cmap.Span);
        if (GlyphMetricCount != 0) MetricsBuffer.Write(plan.Metrics.Span);
    }

    public void Dispose()
    {
        MetricsBuffer.Dispose();
        CmapBuffer.Dispose();
    }
}

/// <summary>
/// Executes the parallel initialization and metric phases of the OpenType
/// compute pipeline. Lookup execution is added to the same retained buffers by
/// subsequent VM stages; this type deliberately does not implement
/// <see cref="IOpenTypeShaper"/> until all required stages are installed.
/// </summary>
public unsafe sealed class GpuOpenTypeRunPipeline : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct RunParams(uint GlyphCount, uint CmapCount, uint MetricCount, uint Direction);

    private readonly WgpuContext _context;
    private readonly RenderPipelineCache _pipelineCache;
    private readonly ComputePipeline* _initializePipeline;
    private readonly ComputePipeline* _metricsPipeline;
    private GpuBuffer? _paramsBuffer;
    private GpuBuffer? _inputBuffer;
    private GpuBuffer? _glyphBuffer;
    private int _capacity;
    private bool _disposed;

    public GpuOpenTypeRunPipeline(WgpuContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pipelineCache = new RenderPipelineCache(context);
        ShaderModule* shader = _pipelineCache.GetOrCreateShader(
            "OpenTypeShaping", OpenTypeShapingShaders.Source, "OpenType shaping");
        _initializePipeline = _pipelineCache.GetOrCreateComputePipeline(
            "OpenTypeInitialize", shader, "initialize_glyphs");
        _metricsPipeline = _pipelineCache.GetOrCreateComputePipeline(
            "OpenTypeMetrics", shader, "load_metrics");
    }

    public void InitializeRun(
        ReadOnlySpan<GpuShapingScalar> input,
        GpuOpenTypeFontData font,
        ShapingDirection direction,
        ShapingBuffer output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(output);
        if (direction == ShapingDirection.Unspecified)
            throw new ArgumentOutOfRangeException(nameof(direction));
        output.Clear();
        if (input.IsEmpty) return;
        EnsureCapacity(input.Length);

        _paramsBuffer!.WriteSingle(new RunParams(
            checked((uint)input.Length),
            checked((uint)font.CmapRangeCount),
            checked((uint)font.GlyphMetricCount),
            (uint)direction));
        _inputBuffer!.Write(input);

        BindGroup* initializeGroup = CreateBindGroup(_initializePipeline, font);
        BindGroup* metricsGroup = CreateBindGroup(_metricsPipeline, font);
        CommandEncoderDescriptor encoderDescriptor = default;
        CommandEncoder* encoder = _context.Api.DeviceCreateCommandEncoder(_context.Device, &encoderDescriptor);
        if (encoder == null) throw new InvalidOperationException("Failed to create the OpenType shaping command encoder.");
        try
        {
            Dispatch(encoder, _initializePipeline, initializeGroup, checked((uint)input.Length));
            Dispatch(encoder, _metricsPipeline, metricsGroup, checked((uint)input.Length));
            CommandBufferDescriptor commandDescriptor = default;
            CommandBuffer* command = _context.Api.CommandEncoderFinish(encoder, &commandDescriptor);
            if (command == null) throw new InvalidOperationException("Failed to finish the OpenType shaping command buffer.");
            try { _context.Api.QueueSubmit(_context.Queue, 1, &command); }
            finally { _context.Api.CommandBufferRelease(command); }
        }
        finally
        {
            _context.Api.CommandEncoderRelease(encoder);
            _context.Api.BindGroupRelease(metricsGroup);
            _context.Api.BindGroupRelease(initializeGroup);
        }

        output.EnsureCapacity(input.Length);
        byte[] bytes = _glyphBuffer!.ReadBytes(0, checked((uint)(input.Length * Marshal.SizeOf<ShapingGlyph>())));
        output.Append(MemoryMarshal.Cast<byte, ShapingGlyph>(bytes));
    }

    private BindGroup* CreateBindGroup(ComputePipeline* pipeline, GpuOpenTypeFontData font)
    {
        BindGroupLayout* layout = _context.Api.ComputePipelineGetBindGroupLayout(pipeline, 0);
        try
        {
            BindGroupEntry* entries = stackalloc BindGroupEntry[5];
            entries[0] = Entry(0, _paramsBuffer!);
            entries[1] = Entry(1, _inputBuffer!);
            entries[2] = Entry(2, font.CmapBuffer);
            entries[3] = Entry(3, font.MetricsBuffer);
            entries[4] = Entry(4, _glyphBuffer!);
            BindGroupDescriptor descriptor = new() { Layout = layout, EntryCount = 5, Entries = entries };
            BindGroup* group = _context.Api.DeviceCreateBindGroup(_context.Device, &descriptor);
            if (group == null) throw new InvalidOperationException("Failed to create an OpenType shaping bind group.");
            return group;
        }
        finally { _context.Api.BindGroupLayoutRelease(layout); }
    }

    private static BindGroupEntry Entry(uint binding, GpuBuffer buffer) => new()
    {
        Binding = binding,
        Buffer = buffer.BufferPtr,
        Offset = 0,
        Size = buffer.Size
    };

    private void Dispatch(CommandEncoder* encoder, ComputePipeline* pipeline, BindGroup* group, uint count)
    {
        ComputePassDescriptor descriptor = default;
        ComputePassEncoder* pass = _context.Api.CommandEncoderBeginComputePass(encoder, &descriptor);
        _context.Api.ComputePassEncoderSetPipeline(pass, pipeline);
        _context.Api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
        _context.Api.ComputePassEncoderDispatchWorkgroups(pass, (count + 63) / 64, 1, 1);
        _context.Api.ComputePassEncoderEnd(pass);
        _context.Api.ComputePassEncoderRelease(pass);
    }

    private void EnsureCapacity(int count)
    {
        if (_paramsBuffer is null)
            _paramsBuffer = new GpuBuffer(_context, 16, BufferUsage.Uniform | BufferUsage.CopyDst, "OpenType run parameters");
        if (count <= _capacity) return;
        int capacity = Math.Max(64, checked((int)BitOperations.RoundUpToPowerOf2((uint)count)));
        _inputBuffer?.Dispose();
        _glyphBuffer?.Dispose();
        _inputBuffer = new GpuBuffer(
            _context,
            checked((uint)(capacity * Marshal.SizeOf<GpuShapingScalar>())),
            BufferUsage.Storage | BufferUsage.CopyDst,
            "OpenType input scalars");
        _glyphBuffer = new GpuBuffer(
            _context,
            checked((uint)(capacity * Marshal.SizeOf<ShapingGlyph>())),
            BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst,
            "OpenType shaping glyphs");
        _capacity = capacity;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _glyphBuffer?.Dispose();
        _inputBuffer?.Dispose();
        _paramsBuffer?.Dispose();
        _pipelineCache.Dispose();
    }
}
