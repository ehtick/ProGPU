using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace ProGPU.Compute;

/// <summary>
/// Reusable hierarchical exclusive scan for unsigned GPU counters. Storage and bind groups grow
/// only when capacity is exceeded; steady calls update one small parameter buffer per active level
/// and record all reduce/add dispatches into a caller-owned command encoder.
/// </summary>
public unsafe sealed class GpuPrefixScan : IDisposable
{
    public const uint ElementsPerBlock = 512;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct ScanParams
    {
        [FieldOffset(0)] public uint Count;
    }

    private sealed class Level : IDisposable
    {
        private readonly WgpuContext _context;

        public Level(
            WgpuContext context,
            uint capacity,
            GpuBuffer input,
            GpuBuffer output,
            GpuBuffer sums)
        {
            _context = context;
            Capacity = capacity;
            Input = input;
            Output = output;
            Sums = sums;
            Params = new GpuBuffer(
                context,
                16,
                BufferUsage.Uniform | BufferUsage.CopyDst,
                "Prefix Scan Parameters");
        }

        public uint Capacity { get; }
        public GpuBuffer Input { get; }
        public GpuBuffer Output { get; }
        public GpuBuffer Sums { get; }
        public GpuBuffer Params { get; }
        public BindGroup* ScanBindGroup { get; set; }
        public BindGroup* AddBindGroup { get; set; }

        public void Dispose()
        {
            if (ScanBindGroup != null)
            {
                _context.Api.BindGroupRelease(ScanBindGroup);
                ScanBindGroup = null;
            }
            if (AddBindGroup != null)
            {
                _context.Api.BindGroupRelease(AddBindGroup);
                AddBindGroup = null;
            }
            Params.Dispose();
            Sums.Dispose();
        }
    }

    private readonly WgpuContext _context;
    private readonly RenderPipelineCache _pipelineCache;
    private readonly ComputePipeline* _scanPipeline;
    private readonly ComputePipeline* _addPipeline;
    private readonly BindGroupLayout* _scanLayout;
    private readonly BindGroupLayout* _addLayout;
    private readonly List<Level> _levels = new();
    private GpuBuffer? _inputBuffer;
    private GpuBuffer? _outputBuffer;
    private uint _capacity;
    private bool _disposed;

    public GpuPrefixScan(WgpuContext context, uint initialCapacity = ElementsPerBlock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _pipelineCache = new RenderPipelineCache(context);
        var reduceModule = _pipelineCache.GetOrCreateShader(
            "PrefixScanReduce",
            ComputeShaders.ScanReduce,
            "PrefixScanReduce");
        var addModule = _pipelineCache.GetOrCreateShader(
            "PrefixScanAdd",
            ComputeShaders.ScanAdd,
            "PrefixScanAdd");
        _scanPipeline = _pipelineCache.GetOrCreateComputePipeline(
            "PrefixScanReduce",
            reduceModule,
            "scan_reduce");
        _addPipeline = _pipelineCache.GetOrCreateComputePipeline(
            "PrefixScanAdd",
            addModule,
            "scan_add");
        _scanLayout = _context.Api.ComputePipelineGetBindGroupLayout(_scanPipeline, 0);
        _addLayout = _context.Api.ComputePipelineGetBindGroupLayout(_addPipeline, 0);
        EnsureCapacity(Math.Max(1u, initialCapacity));
    }

    public uint Capacity => _capacity;

    public GpuBuffer InputBuffer =>
        _inputBuffer ?? throw new ObjectDisposedException(nameof(GpuPrefixScan));

    public GpuBuffer OutputBuffer =>
        _outputBuffer ?? throw new ObjectDisposedException(nameof(GpuPrefixScan));

    public int LevelCount => _levels.Count;

    public void EnsureCapacity(uint count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        count = Math.Max(1u, count);
        if (count <= _capacity)
        {
            return;
        }

        uint capacity = Math.Max(1u, _capacity);
        while (capacity < count)
        {
            capacity = checked(capacity * 2u);
        }
        Rebuild(capacity);
    }

    public void WriteInput(ReadOnlySpan<uint> values)
    {
        EnsureCapacity(checked((uint)Math.Max(1, values.Length)));
        if (!values.IsEmpty)
        {
            InputBuffer.Write(values);
        }
    }

    public void RecordExclusiveScan(CommandEncoder* encoder, uint count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoder == null) throw new ArgumentNullException(nameof(encoder));
        if (count == 0) return;
        EnsureCapacity(count);

        int activeLevelCount = 0;
        uint levelCount = count;
        while (true)
        {
            var level = _levels[activeLevelCount++];
            level.Params.WriteSingle(new ScanParams { Count = levelCount });
            uint blockCount = DivRoundUp(levelCount, ElementsPerBlock);
            if (blockCount <= 1u)
            {
                break;
            }
            levelCount = blockCount;
        }

        var passDescriptor = new ComputePassDescriptor();
        var pass = _context.Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
        if (pass == null)
        {
            throw new InvalidOperationException("Failed to begin the prefix-scan compute pass.");
        }

        try
        {
            levelCount = count;
            _context.Api.ComputePassEncoderSetPipeline(pass, _scanPipeline);
            for (int levelIndex = 0; levelIndex < activeLevelCount; levelIndex++)
            {
                var level = _levels[levelIndex];
                uint blockCount = DivRoundUp(levelCount, ElementsPerBlock);
                _context.Api.ComputePassEncoderSetBindGroup(pass, 0, level.ScanBindGroup, 0, null);
                _context.Api.ComputePassEncoderDispatchWorkgroups(pass, blockCount, 1, 1);
                levelCount = blockCount;
            }

            _context.Api.ComputePassEncoderSetPipeline(pass, _addPipeline);
            for (int levelIndex = activeLevelCount - 2; levelIndex >= 0; levelIndex--)
            {
                var level = _levels[levelIndex];
                uint elementCount = levelIndex == 0
                    ? count
                    : DivRoundUpCount(count, levelIndex);
                uint blockCount = DivRoundUp(elementCount, ElementsPerBlock);
                _context.Api.ComputePassEncoderSetBindGroup(pass, 0, level.AddBindGroup, 0, null);
                _context.Api.ComputePassEncoderDispatchWorkgroups(pass, blockCount, 1, 1);
            }

            _context.Api.ComputePassEncoderEnd(pass);
        }
        finally
        {
            _context.Api.ComputePassEncoderRelease(pass);
        }
    }

    public static uint ExclusiveScanCpu(ReadOnlySpan<uint> input, Span<uint> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output span is shorter than the input span.", nameof(output));
        }

        uint sum = 0;
        for (int index = 0; index < input.Length; index++)
        {
            uint value = input[index];
            output[index] = sum;
            sum = unchecked(sum + value);
        }
        return sum;
    }

    private void Rebuild(uint capacity)
    {
        ReleaseBuffers();
        _capacity = capacity;
        _inputBuffer = CreateValueBuffer(capacity, "Prefix Scan Input");
        _outputBuffer = CreateValueBuffer(capacity, "Prefix Scan Output", copySource: true);

        uint levelCapacity = capacity;
        GpuBuffer levelInput = _inputBuffer;
        GpuBuffer levelOutput = _outputBuffer;
        while (true)
        {
            uint blockCapacity = DivRoundUp(levelCapacity, ElementsPerBlock);
            var sums = CreateValueBuffer(blockCapacity, "Prefix Scan Block Sums");
            var level = new Level(
                _context,
                levelCapacity,
                levelInput,
                levelOutput,
                sums);
            _levels.Add(level);
            if (blockCapacity <= 1u)
            {
                break;
            }

            levelInput = sums;
            levelOutput = CreateValueBuffer(
                blockCapacity,
                "Prefix Scan Block Offsets");
            levelCapacity = blockCapacity;
        }

        for (int levelIndex = 0; levelIndex < _levels.Count; levelIndex++)
        {
            var level = _levels[levelIndex];
            level.ScanBindGroup = CreateScanBindGroup(level);
            if (levelIndex + 1 < _levels.Count)
            {
                level.AddBindGroup = CreateAddBindGroup(level, _levels[levelIndex + 1].Output);
            }
        }
    }

    private BindGroup* CreateScanBindGroup(Level level)
    {
        var entries = stackalloc BindGroupEntry[4];
        entries[0] = BufferEntry(0, level.Params);
        entries[1] = BufferEntry(1, level.Input);
        entries[2] = BufferEntry(2, level.Output);
        entries[3] = BufferEntry(3, level.Sums);
        var descriptor = new BindGroupDescriptor
        {
            Layout = _scanLayout,
            EntryCount = 4,
            Entries = entries
        };
        return CreateBindGroup(&descriptor, "prefix-scan");
    }

    private BindGroup* CreateAddBindGroup(Level level, GpuBuffer blockOffsets)
    {
        var entries = stackalloc BindGroupEntry[3];
        entries[0] = BufferEntry(0, level.Params);
        entries[1] = BufferEntry(1, level.Output);
        entries[2] = BufferEntry(2, blockOffsets);
        var descriptor = new BindGroupDescriptor
        {
            Layout = _addLayout,
            EntryCount = 3,
            Entries = entries
        };
        return CreateBindGroup(&descriptor, "prefix-add");
    }

    private BindGroup* CreateBindGroup(BindGroupDescriptor* descriptor, string operation)
    {
        var bindGroup = _context.Api.DeviceCreateBindGroup(_context.Device, descriptor);
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

    private GpuBuffer CreateValueBuffer(uint count, string label, bool copySource = false)
    {
        var usage = BufferUsage.Storage | BufferUsage.CopyDst;
        if (copySource) usage |= BufferUsage.CopySrc;
        return new GpuBuffer(
            _context,
            checked(Math.Max(1u, count) * sizeof(uint)),
            usage,
            label);
    }

    private static uint DivRoundUp(uint value, uint divisor) =>
        checked((value + divisor - 1u) / divisor);

    private static uint DivRoundUpCount(uint baseCount, int level)
    {
        uint count = baseCount;
        for (int index = 0; index < level; index++)
        {
            count = DivRoundUp(count, ElementsPerBlock);
        }
        return count;
    }

    private void ReleaseBuffers()
    {
        for (int index = 0; index < _levels.Count; index++)
        {
            // Outputs above level zero are owned by the level that writes them, while each
            // level owns only its sum buffer and parameter/bind-group resources.
            if (index > 0)
            {
                _levels[index].Output.Dispose();
            }
            _levels[index].Dispose();
        }
        _levels.Clear();
        _inputBuffer?.Dispose();
        _outputBuffer?.Dispose();
        _inputBuffer = null;
        _outputBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBuffers();
        _context.Api.BindGroupLayoutRelease(_scanLayout);
        _context.Api.BindGroupLayoutRelease(_addLayout);
        _pipelineCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
