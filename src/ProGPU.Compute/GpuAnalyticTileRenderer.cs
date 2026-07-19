using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace ProGPU.Compute;

/// <summary>
/// One visible 16x16 tile and its contiguous painter-ordered fill-command range.
/// Coordinates are in target tile space, not logical UI coordinates.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GpuAnalyticTile
{
    [FieldOffset(0)] public uint X;
    [FieldOffset(4)] public uint Y;
    [FieldOffset(8)] public uint CommandOffset;
    [FieldOffset(12)] public uint CommandCount;
}

/// <summary>
/// A Vello-style analytic fill command. <see cref="SegmentCountAndRule"/> stores the segment count
/// in the high 31 bits and the even-odd flag in bit zero. Color is premultiplied.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GpuAnalyticFill
{
    [FieldOffset(0)] public uint SegmentOffset;
    [FieldOffset(4)] public uint SegmentCountAndRule;
    [FieldOffset(8)] public int Backdrop;
    [FieldOffset(12)] private uint _pad0;
    [FieldOffset(16)] public Vector4 PremultipliedColor;

    public readonly uint SegmentCount => SegmentCountAndRule >> 1;

    public readonly bool IsEvenOdd => (SegmentCountAndRule & 1u) != 0;

    public static uint PackSegmentCountAndRule(uint segmentCount, bool evenOdd)
    {
        if (segmentCount > uint.MaxValue >> 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        }

        return (segmentCount << 1) | (evenOdd ? 1u : 0u);
    }
}

/// <summary>
/// One line clipped to a 16x16 tile. Coordinates and <see cref="YEdge"/> are tile-local.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GpuAnalyticSegment
{
    [FieldOffset(0)] public Vector2 Point0;
    [FieldOffset(8)] public Vector2 Point1;
    [FieldOffset(16)] public float YEdge;
    [FieldOffset(20)] private float _pad0;
}

/// <summary>
/// Browser-valid Vello-style fine rasterization core. It consumes already tiled commands and
/// segments, performs analytic area anti-aliasing without barriers or signed-distance evaluation,
/// and writes one workgroup per visible 16x16 tile. Persistent buffers and bind groups grow only
/// when capacity or destination identity changes.
/// </summary>
public unsafe sealed class GpuAnalyticTileRenderer : IDisposable
{
    public const uint TileSize = 16;
    public const uint MaximumPortableDispatchDimension = 65535;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FineParams
    {
        [FieldOffset(0)] public uint Width;
        [FieldOffset(4)] public uint Height;
        [FieldOffset(8)] public uint TileCount;
    }

    private readonly WgpuContext _context;
    private readonly RenderPipelineCache _pipelineCache;
    private readonly ComputePipeline* _pipeline;
    private readonly BindGroupLayout* _layout;
    private readonly GpuBuffer _paramsBuffer;
    private GpuBuffer? _tileBuffer;
    private GpuBuffer? _commandBuffer;
    private GpuBuffer? _segmentBuffer;
    private BindGroup* _bindGroup;
    private uint _tileCapacity;
    private uint _commandCapacity;
    private uint _segmentCapacity;
    private uint _tileCount;
    private ulong _destinationId;
    private uint _destinationGeneration;
    private bool _disposed;

    public GpuAnalyticTileRenderer(
        WgpuContext context,
        uint initialTileCapacity = 256,
        uint initialCommandCapacity = 512,
        uint initialSegmentCapacity = 4096)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pipelineCache = new RenderPipelineCache(context);
        var module = _pipelineCache.GetOrCreateShader(
            "AnalyticTileFine",
            ComputeShaders.AnalyticTileFine,
            "AnalyticTileFine");
        _pipeline = _pipelineCache.GetOrCreateComputePipeline(
            "AnalyticTileFine",
            module,
            "analytic_tile_fine");
        _layout = _context.Api.ComputePipelineGetBindGroupLayout(_pipeline, 0);
        _paramsBuffer = new GpuBuffer(
            context,
            16,
            BufferUsage.Uniform | BufferUsage.CopyDst,
            "Analytic Tile Fine Parameters");
        RebuildBuffers(
            Math.Max(1u, initialTileCapacity),
            Math.Max(1u, initialCommandCapacity),
            Math.Max(1u, initialSegmentCapacity));
    }

    public uint TileCapacity => _tileCapacity;

    public uint CommandCapacity => _commandCapacity;

    public uint SegmentCapacity => _segmentCapacity;

    public void Upload(
        ReadOnlySpan<GpuAnalyticTile> tiles,
        ReadOnlySpan<GpuAnalyticFill> commands,
        ReadOnlySpan<GpuAnalyticSegment> segments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRanges(tiles, commands, segments);
        EnsureCapacity(
            checked((uint)Math.Max(1, tiles.Length)),
            checked((uint)Math.Max(1, commands.Length)),
            checked((uint)Math.Max(1, segments.Length)));

        if (!tiles.IsEmpty)
        {
            _tileBuffer!.Write(tiles);
        }
        if (!commands.IsEmpty)
        {
            _commandBuffer!.Write(commands);
        }
        if (!segments.IsEmpty)
        {
            _segmentBuffer!.Write(segments);
        }
        _tileCount = checked((uint)tiles.Length);
    }

    public void Record(CommandEncoder* encoder, GpuTexture destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder == null) throw new ArgumentNullException(nameof(encoder));
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Context != _context)
        {
            throw new ArgumentException("The destination belongs to another WebGPU context.", nameof(destination));
        }
        if (destination.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("Analytic fine output currently requires Rgba8Unorm.", nameof(destination));
        }
        if (!destination.Usage.HasFlag(TextureUsage.StorageBinding))
        {
            throw new ArgumentException("The destination requires StorageBinding usage.", nameof(destination));
        }
        if (_tileCount == 0)
        {
            return;
        }

        _paramsBuffer.WriteSingle(new FineParams
        {
            Width = destination.Width,
            Height = destination.Height,
            TileCount = _tileCount
        });
        EnsureBindGroup(destination);

        var passDescriptor = new ComputePassDescriptor();
        var pass = _context.Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
        if (pass == null)
        {
            throw new InvalidOperationException("Failed to begin the analytic tile fine pass.");
        }

        try
        {
            _context.Api.ComputePassEncoderSetPipeline(pass, _pipeline);
            _context.Api.ComputePassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            uint x = Math.Min(_tileCount, MaximumPortableDispatchDimension);
            uint y = DivRoundUp(_tileCount, MaximumPortableDispatchDimension);
            _context.Api.ComputePassEncoderDispatchWorkgroups(pass, x, y, 1);
            _context.Api.ComputePassEncoderEnd(pass);
        }
        finally
        {
            _context.Api.ComputePassEncoderRelease(pass);
        }
    }

    /// <summary>
    /// CPU oracle for the fine stage. The caller supplies one 16x16 tile output cleared to the
    /// desired destination color; commands are applied in source order.
    /// </summary>
    public static void RasterizeTileCpu(
        ReadOnlySpan<GpuAnalyticFill> commands,
        ReadOnlySpan<GpuAnalyticSegment> segments,
        Span<Vector4> output)
    {
        if (output.Length < checked((int)(TileSize * TileSize)))
        {
            throw new ArgumentException("A tile output requires 256 pixels.", nameof(output));
        }

        for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
        {
            ref readonly var command = ref commands[commandIndex];
            uint segmentCount = command.SegmentCount;
            if (command.SegmentOffset > segments.Length ||
                segmentCount > (uint)segments.Length - command.SegmentOffset)
            {
                throw new ArgumentException("A fill command references segments outside the supplied span.", nameof(commands));
            }

            for (uint yPixel = 0; yPixel < TileSize; yPixel++)
            {
                for (uint xPixel = 0; xPixel < TileSize; xPixel++)
                {
                    float area = command.Backdrop;
                    for (uint segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                    {
                        ref readonly var segment = ref segments[checked((int)(command.SegmentOffset + segmentIndex))];
                        Vector2 delta = segment.Point1 - segment.Point0;
                        float y = segment.Point0.Y - yPixel;
                        float y0 = Math.Clamp(y, 0f, 1f);
                        float y1 = Math.Clamp(y + delta.Y, 0f, 1f);
                        float dy = y0 - y1;
                        float yEdge = MathF.Sign(delta.X) * Math.Clamp(yPixel - segment.YEdge + 1f, 0f, 1f);
                        if (dy != 0f)
                        {
                            float inverseY = 1f / delta.Y;
                            float t0 = (y0 - y) * inverseY;
                            float t1 = (y1 - y) * inverseY;
                            float x0 = segment.Point0.X + t0 * delta.X;
                            float x1 = segment.Point0.X + t1 * delta.X;
                            float minimumX = MathF.Min(x0, x1);
                            float maximumX = MathF.Max(x0, x1);
                            area += yEdge + SegmentArea(minimumX, maximumX, xPixel) * dy;
                        }
                        else
                        {
                            area += yEdge;
                        }
                    }

                    float coverage = command.IsEvenOdd
                        ? MathF.Abs(area - 2f * MathF.Round(0.5f * area))
                        : MathF.Min(MathF.Abs(area), 1f);
                    Vector4 source = command.PremultipliedColor * coverage;
                    int pixelIndex = checked((int)(yPixel * TileSize + xPixel));
                    output[pixelIndex] = SourceOver(source, output[pixelIndex]);
                }
            }
        }
    }

    private static float SegmentArea(float minimumX, float maximumX, float pixelX)
    {
        float xmin = MathF.Min(minimumX - pixelX, 1f) - 1e-6f;
        float xmax = maximumX - pixelX;
        float b = MathF.Min(xmax, 1f);
        float c = MathF.Max(b, 0f);
        float d = MathF.Max(xmin, 0f);
        return (b + 0.5f * (d * d - c * c) - xmin) / (xmax - xmin);
    }

    private static Vector4 SourceOver(Vector4 source, Vector4 destination) =>
        source + destination * (1f - source.W);

    private static void ValidateRanges(
        ReadOnlySpan<GpuAnalyticTile> tiles,
        ReadOnlySpan<GpuAnalyticFill> commands,
        ReadOnlySpan<GpuAnalyticSegment> segments)
    {
        for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
        {
            ref readonly var tile = ref tiles[tileIndex];
            if (tile.CommandOffset > commands.Length ||
                tile.CommandCount > (uint)commands.Length - tile.CommandOffset)
            {
                throw new ArgumentException("A tile references commands outside the supplied span.", nameof(tiles));
            }
        }

        for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
        {
            ref readonly var command = ref commands[commandIndex];
            if (command.SegmentOffset > segments.Length ||
                command.SegmentCount > (uint)segments.Length - command.SegmentOffset)
            {
                throw new ArgumentException("A fill command references segments outside the supplied span.", nameof(commands));
            }
        }
    }

    private void EnsureCapacity(uint tileCount, uint commandCount, uint segmentCount)
    {
        if (tileCount <= _tileCapacity &&
            commandCount <= _commandCapacity &&
            segmentCount <= _segmentCapacity)
        {
            return;
        }

        RebuildBuffers(
            GrowCapacity(_tileCapacity, tileCount),
            GrowCapacity(_commandCapacity, commandCount),
            GrowCapacity(_segmentCapacity, segmentCount));
    }

    private void RebuildBuffers(uint tileCapacity, uint commandCapacity, uint segmentCapacity)
    {
        ReleaseBindGroup();
        _tileBuffer?.Dispose();
        _commandBuffer?.Dispose();
        _segmentBuffer?.Dispose();
        _tileCapacity = tileCapacity;
        _commandCapacity = commandCapacity;
        _segmentCapacity = segmentCapacity;
        _tileBuffer = CreateStorageBuffer<GpuAnalyticTile>(tileCapacity, "Analytic Fine Tiles");
        _commandBuffer = CreateStorageBuffer<GpuAnalyticFill>(commandCapacity, "Analytic Fine Commands");
        _segmentBuffer = CreateStorageBuffer<GpuAnalyticSegment>(segmentCapacity, "Analytic Fine Segments");
    }

    private void EnsureBindGroup(GpuTexture destination)
    {
        if (_bindGroup != null &&
            _destinationId == destination.Id &&
            _destinationGeneration == destination.Generation)
        {
            return;
        }

        ReleaseBindGroup();
        var entries = stackalloc BindGroupEntry[5];
        entries[0] = BufferEntry(0, _paramsBuffer);
        entries[1] = BufferEntry(1, _tileBuffer!);
        entries[2] = BufferEntry(2, _commandBuffer!);
        entries[3] = BufferEntry(3, _segmentBuffer!);
        entries[4] = new BindGroupEntry { Binding = 4, TextureView = destination.ViewPtr };
        var descriptor = new BindGroupDescriptor
        {
            Layout = _layout,
            EntryCount = 5,
            Entries = entries
        };
        _bindGroup = _context.Api.DeviceCreateBindGroup(_context.Device, &descriptor);
        if (_bindGroup == null)
        {
            throw new InvalidOperationException("Failed to create the analytic tile fine bind group.");
        }
        _destinationId = destination.Id;
        _destinationGeneration = destination.Generation;
    }

    private GpuBuffer CreateStorageBuffer<T>(uint count, string label) where T : unmanaged =>
        new(
            _context,
            checked(Math.Max(1u, count) * (uint)Marshal.SizeOf<T>()),
            BufferUsage.Storage | BufferUsage.CopyDst,
            label);

    private static BindGroupEntry BufferEntry(uint binding, GpuBuffer buffer) => new()
    {
        Binding = binding,
        Buffer = buffer.BufferPtr,
        Offset = 0,
        Size = buffer.Size
    };

    private void ReleaseBindGroup()
    {
        if (_bindGroup != null)
        {
            _context.Api.BindGroupRelease(_bindGroup);
            _bindGroup = null;
        }
        _destinationId = 0;
        _destinationGeneration = 0;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBindGroup();
        _tileBuffer?.Dispose();
        _commandBuffer?.Dispose();
        _segmentBuffer?.Dispose();
        _paramsBuffer.Dispose();
        _context.Api.BindGroupLayoutRelease(_layout);
        _pipelineCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
