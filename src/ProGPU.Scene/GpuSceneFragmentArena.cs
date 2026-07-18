using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ProGPU.Backend;
using ProGPU.Vector;
using Silk.NET.WebGPU;

namespace ProGPU.Scene;

/// <summary>
/// Grow-only shared GPU storage for stable scene-fragment handles. A handle keeps its largest
/// allocated slot and subsequent updates overwrite only that slot. Arena growth is cold-path
/// O(N); steady updates are O(changed fragment data) with one partial write per non-empty stream.
/// </summary>
internal sealed class GpuSceneFragmentArena : IDisposable
{
    internal readonly record struct DrawRange(Compositor.DrawCallType Type, uint Start, uint Count);

    internal sealed class Allocation
    {
        public long Version;
        public int VectorStart;
        public int VectorCapacity;
        public int VectorCount;
        public int IndexStart;
        public int IndexCapacity;
        public int IndexCount;
        public int TextStart;
        public int TextCapacity;
        public int TextCount;
        public int BrushStart;
        public int BrushCapacity;
        public int BrushCount;
        public int GradientStart;
        public int GradientCapacity;
        public int GradientCount;
        public DrawRange[] DrawRanges = [];
        public ulong LastUsedFrame;
    }

    private readonly WgpuContext _context;
    // The arena must not keep a detached visual and all of its retained resources alive. Slots
    // are grow-only and remain reusable arena storage, while allocation metadata follows the
    // owning handle's lifetime.
    private readonly ConditionalWeakTable<SceneFragmentHandle, Allocation> _allocations = new();
    private VectorVertex[] _vectorShadow = [];
    private uint[] _indexShadow = [];
    private GlyphInstance[] _textShadow = [];
    private GpuBrush[] _brushShadow = [];
    private GpuGradientStop[] _gradientShadow = [];
    private int _vectorUsed;
    private int _indexUsed;
    private int _textUsed;
    private int _brushUsed;
    private int _gradientUsed;
    private bool _disposed;

    private GpuBuffer _vectorBuffer;
    private GpuBuffer _indexBuffer;
    private GpuBuffer _textBuffer;
    private GpuBuffer _brushBuffer;
    private GpuBuffer _gradientBuffer;
    public GpuBuffer VectorBuffer => _vectorBuffer;
    public GpuBuffer IndexBuffer => _indexBuffer;
    public GpuBuffer TextBuffer => _textBuffer;
    public GpuBuffer BrushBuffer => _brushBuffer;
    public GpuBuffer GradientBuffer => _gradientBuffer;
    public ulong BindGroupGeneration { get; private set; } = 1;

    public GpuSceneFragmentArena(WgpuContext context)
    {
        _context = context;
        _vectorBuffer = CreateBuffer<VectorVertex>(256, BufferUsage.Vertex, "Scene Fragment Vector Arena");
        _indexBuffer = CreateBuffer<uint>(512, BufferUsage.Index, "Scene Fragment Index Arena");
        _textBuffer = CreateBuffer<GlyphInstance>(256, BufferUsage.Vertex, "Scene Fragment Text Arena");
        _brushBuffer = CreateBuffer<GpuBrush>(64, BufferUsage.Storage, "Scene Fragment Brush Arena");
        _gradientBuffer = CreateBuffer<GpuGradientStop>(64, BufferUsage.Storage, "Scene Fragment Gradient Arena");
        _vectorShadow = new VectorVertex[256];
        _indexShadow = new uint[512];
        _textShadow = new GlyphInstance[256];
        _brushShadow = new GpuBrush[64];
        _gradientShadow = new GpuGradientStop[64];
    }

    public bool TryGet(SceneFragmentHandle handle, long version, ulong frame, out Allocation allocation)
    {
        if (_allocations.TryGetValue(handle, out allocation!) && allocation.Version == version)
        {
            allocation.LastUsedFrame = frame;
            return true;
        }
        return false;
    }

    public void Remove(SceneFragmentHandle handle) => _allocations.Remove(handle);

    public Allocation Update(
        SceneFragmentHandle handle,
        long version,
        ulong frame,
        ReadOnlySpan<VectorVertex> vectors,
        ReadOnlySpan<uint> indices,
        int sourceVectorStart,
        ReadOnlySpan<GlyphInstance> text,
        ReadOnlySpan<GpuBrush> brushes,
        ReadOnlySpan<GpuGradientStop> gradients,
        ReadOnlySpan<DrawRange> ranges)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_allocations.TryGetValue(handle, out var allocation))
        {
            allocation = new Allocation();
            _allocations.Add(handle, allocation);
        }

        Reserve(ref allocation.VectorStart, ref allocation.VectorCapacity, vectors.Length, ref _vectorUsed);
        Reserve(ref allocation.IndexStart, ref allocation.IndexCapacity, indices.Length, ref _indexUsed);
        Reserve(ref allocation.TextStart, ref allocation.TextCapacity, text.Length, ref _textUsed);
        Reserve(ref allocation.BrushStart, ref allocation.BrushCapacity, brushes.Length, ref _brushUsed);
        Reserve(ref allocation.GradientStart, ref allocation.GradientCapacity, gradients.Length, ref _gradientUsed);

        EnsureShadowCapacity(ref _vectorShadow, _vectorUsed);
        EnsureShadowCapacity(ref _indexShadow, _indexUsed);
        EnsureShadowCapacity(ref _textShadow, _textUsed);
        EnsureShadowCapacity(ref _brushShadow, _brushUsed);
        EnsureShadowCapacity(ref _gradientShadow, _gradientUsed);

        for (int i = 0; i < vectors.Length; i++)
        {
            var value = vectors[i];
            value.BrushIndex += allocation.BrushStart;
            _vectorShadow[allocation.VectorStart + i] = value;
        }
        for (int i = 0; i < indices.Length; i++)
        {
            _indexShadow[allocation.IndexStart + i] = checked(
                (uint)(indices[i] - sourceVectorStart + allocation.VectorStart));
        }
        Array.Clear(_textShadow, allocation.TextStart, allocation.TextCapacity);
        for (int i = 0; i < text.Length; i++)
        {
            var value = text[i];
            value.BrushIndex += allocation.BrushStart;
            _textShadow[allocation.TextStart + i] = value;
        }
        for (int i = 0; i < brushes.Length; i++)
        {
            var value = brushes[i];
            if (value.StopCount > 0)
            {
                value.StopOffset = checked(value.StopOffset + (uint)allocation.GradientStart);
            }
            _brushShadow[allocation.BrushStart + i] = value;
        }
        gradients.CopyTo(_gradientShadow.AsSpan(allocation.GradientStart));

        bool vectorsGrew = EnsureGpuCapacity(ref _vectorBuffer, _vectorShadow, _vectorUsed, BufferUsage.Vertex, "Scene Fragment Vector Arena");
        bool indicesGrew = EnsureGpuCapacity(ref _indexBuffer, _indexShadow, _indexUsed, BufferUsage.Index, "Scene Fragment Index Arena");
        bool textGrew = EnsureGpuCapacity(ref _textBuffer, _textShadow, _textUsed, BufferUsage.Vertex, "Scene Fragment Text Arena");
        bool brushesGrew = EnsureGpuCapacity(ref _brushBuffer, _brushShadow, _brushUsed, BufferUsage.Storage, "Scene Fragment Brush Arena");
        bool gradientsGrew = EnsureGpuCapacity(ref _gradientBuffer, _gradientShadow, _gradientUsed, BufferUsage.Storage, "Scene Fragment Gradient Arena");

        WriteChanged(_vectorBuffer, _vectorShadow, allocation.VectorStart, vectors.Length, vectorsGrew);
        WriteChanged(_indexBuffer, _indexShadow, allocation.IndexStart, indices.Length, indicesGrew);
        WriteChanged(_textBuffer, _textShadow, allocation.TextStart, allocation.TextCapacity, textGrew);
        WriteChanged(_brushBuffer, _brushShadow, allocation.BrushStart, brushes.Length, brushesGrew);
        WriteChanged(_gradientBuffer, _gradientShadow, allocation.GradientStart, gradients.Length, gradientsGrew);

        if (brushesGrew || gradientsGrew)
        {
            unchecked { BindGroupGeneration++; }
        }

        allocation.Version = version;
        allocation.LastUsedFrame = frame;
        allocation.VectorCount = vectors.Length;
        allocation.IndexCount = indices.Length;
        allocation.TextCount = text.Length;
        allocation.BrushCount = brushes.Length;
        allocation.GradientCount = gradients.Length;
        allocation.DrawRanges = new DrawRange[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            var range = ranges[i];
            allocation.DrawRanges[i] = range.Type == Compositor.DrawCallType.Vector
                ? range with { Start = checked(range.Start + (uint)allocation.IndexStart) }
                : range with { Start = checked(range.Start + (uint)allocation.TextStart) };
        }
        return allocation;
    }

    private GpuBuffer CreateBuffer<T>(int count, BufferUsage usage, string label) where T : unmanaged =>
        new(_context, checked((uint)count * (uint)Marshal.SizeOf<T>()), usage | BufferUsage.CopyDst, label);

    private bool EnsureGpuCapacity<T>(
        ref GpuBuffer buffer,
        T[] shadow,
        int used,
        BufferUsage usage,
        string label) where T : unmanaged
    {
        uint required = checked((uint)Math.Max(1, shadow.Length) * (uint)Marshal.SizeOf<T>());
        if (buffer.Size >= required)
        {
            return false;
        }
        var replacement = new GpuBuffer(_context, required, usage | BufferUsage.CopyDst, label);
        if (used > 0)
        {
            replacement.Write(shadow.AsSpan(0, used));
        }
        buffer.Dispose();
        buffer = replacement;
        return true;
    }

    private static void WriteChanged<T>(GpuBuffer buffer, T[] shadow, int start, int count, bool uploadedByGrowth)
        where T : unmanaged
    {
        if (count == 0 || uploadedByGrowth)
        {
            return;
        }
        buffer.Write(shadow.AsSpan(start, count), checked((uint)start * (uint)Marshal.SizeOf<T>()));
    }

    private static void Reserve(ref int start, ref int capacity, int required, ref int used)
    {
        if (required <= capacity)
        {
            return;
        }
        capacity = RoundUpPowerOfTwo(Math.Max(1, required));
        start = used;
        used = checked(used + capacity);
    }

    private static void EnsureShadowCapacity<T>(ref T[] values, int required)
    {
        if (values.Length >= required)
        {
            return;
        }
        Array.Resize(ref values, RoundUpPowerOfTwo(required));
    }

    private static int RoundUpPowerOfTwo(int value)
    {
        if (value <= 1) return 1;
        return checked((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)value));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vectorBuffer.Dispose();
        _indexBuffer.Dispose();
        _textBuffer.Dispose();
        _brushBuffer.Dispose();
        _gradientBuffer.Dispose();
    }
}
