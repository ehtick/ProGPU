using System.Buffers.Binary;
using Silk.NET.WebGPU;

namespace ProGPU.Backend;

public enum GpuTimestampStage
{
    GlyphAtlas = 0,
    PathAtlas = 1,
    ScenePreparation = 2,
    MaskEffects = 3,
    PrimaryRender = 4,
    WavefrontCompute = 5,
    FinalComposite = 6,
    WavefrontGeometry = 7,
    WavefrontBinning = 8,
    WavefrontCompaction = 9,
    WavefrontCoarseFine = 10
}

public readonly record struct GpuTimestampStageMetrics(
    long CompletedSamples,
    double TotalMilliseconds,
    double LastMilliseconds,
    double AverageMilliseconds,
    double MaximumMilliseconds);

public readonly record struct GpuTimestampMetrics(
    long SubmittedSamples,
    long CompletedSamples,
    long FailedSamples,
    long DroppedSamples,
    double LastFrameMilliseconds,
    double TotalFrameMilliseconds,
    double AverageFrameMilliseconds,
    double MaximumFrameMilliseconds,
    GpuTimestampStageMetrics GlyphAtlas,
    GpuTimestampStageMetrics PathAtlas,
    GpuTimestampStageMetrics ScenePreparation,
    GpuTimestampStageMetrics MaskEffects,
    GpuTimestampStageMetrics PrimaryRender,
    GpuTimestampStageMetrics WavefrontCompute,
    GpuTimestampStageMetrics FinalComposite,
    GpuTimestampStageMetrics WavefrontGeometry,
    GpuTimestampStageMetrics WavefrontBinning,
    GpuTimestampStageMetrics WavefrontCompaction,
    GpuTimestampStageMetrics WavefrontCoarseFine);

/// <summary>
/// Resolves timestamp pairs through three independently mappable readback buffers. A frame never
/// waits for a previous mapping; when all slots are busy the diagnostic sample is dropped.
/// Normal rendering does not create this object unless timestamp diagnostics are explicitly enabled.
/// </summary>
internal unsafe sealed class GpuTimestampRing : IDisposable
{
    private const int SlotCount = 3;
    private const int StageCount = 11;
    private const uint QueryCount = 2u + StageCount * 2u;
    private const uint TimestampBytes = QueryCount * sizeof(ulong);
    private const uint ResolveStride = 256;

    private readonly WgpuContext _context;
    private readonly QuerySet* _querySet;
    private readonly GpuBuffer _resolveBuffer;
    private readonly Slot[] _slots = new Slot[SlotCount];
    private int _nextSlot;
    private int _activeSlot = -1;
    private long _submittedSamples;
    private long _completedSamples;
    private long _failedSamples;
    private long _droppedSamples;
    private double _totalMilliseconds;
    private double _lastMilliseconds;
    private double _maximumMilliseconds;
    private readonly long[] _stageCompletedSamples = new long[StageCount];
    private readonly double[] _stageTotalMilliseconds = new double[StageCount];
    private readonly double[] _stageLastMilliseconds = new double[StageCount];
    private readonly double[] _stageMaximumMilliseconds = new double[StageCount];
    private uint _activeStageMask;
    private uint _recordedStageMask;
    private long _metricsEpoch;
    private bool _disposed;

    public GpuTimestampRing(WgpuContext context)
    {
        _context = context;
        var queryDescriptor = new QuerySetDescriptor
        {
            Type = QueryType.Timestamp,
            Count = QueryCount
        };
        _querySet = context.Api.DeviceCreateQuerySet(context.Device, &queryDescriptor);
        if (_querySet == null)
        {
            throw new InvalidOperationException("The WebGPU device did not create a timestamp query set.");
        }

        _resolveBuffer = new GpuBuffer(
            context,
            ResolveStride * SlotCount,
            BufferUsage.QueryResolve | BufferUsage.CopySrc,
            "GPU timestamp resolve ring");
        for (int index = 0; index < SlotCount; index++)
        {
            _slots[index] = new Slot(new GpuBuffer(
                context,
                TimestampBytes,
                BufferUsage.CopyDst | BufferUsage.MapRead,
                $"GPU timestamp readback {index}"));
        }
    }

    public GpuTimestampMetrics Metrics
    {
        get
        {
            HarvestCompletedMappings();
            return new GpuTimestampMetrics(
                _submittedSamples,
                _completedSamples,
                _failedSamples,
                _droppedSamples,
                _lastMilliseconds,
                _totalMilliseconds,
                _completedSamples == 0 ? 0d : _totalMilliseconds / _completedSamples,
                _maximumMilliseconds,
                GetStageMetrics(GpuTimestampStage.GlyphAtlas),
                GetStageMetrics(GpuTimestampStage.PathAtlas),
                GetStageMetrics(GpuTimestampStage.ScenePreparation),
                GetStageMetrics(GpuTimestampStage.MaskEffects),
                GetStageMetrics(GpuTimestampStage.PrimaryRender),
                GetStageMetrics(GpuTimestampStage.WavefrontCompute),
                GetStageMetrics(GpuTimestampStage.FinalComposite),
                GetStageMetrics(GpuTimestampStage.WavefrontGeometry),
                GetStageMetrics(GpuTimestampStage.WavefrontBinning),
                GetStageMetrics(GpuTimestampStage.WavefrontCompaction),
                GetStageMetrics(GpuTimestampStage.WavefrontCoarseFine));
        }
    }

    public bool BeginFrame(CommandEncoder* encoder)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        HarvestCompletedMappings();
        if (_activeSlot >= 0)
        {
            throw new InvalidOperationException("A GPU timestamp frame is already active.");
        }

        int selected = -1;
        for (int offset = 0; offset < SlotCount; offset++)
        {
            int candidate = (_nextSlot + offset) % SlotCount;
            if (_slots[candidate].MapTask == null)
            {
                selected = candidate;
                break;
            }
        }

        if (selected < 0)
        {
            _droppedSamples++;
            return false;
        }

        _nextSlot = (selected + 1) % SlotCount;
        _activeSlot = selected;
        _activeStageMask = 0;
        _recordedStageMask = 0;
        _context.Api.CommandEncoderWriteTimestamp(encoder, _querySet, 0);
        return true;
    }

    public bool BeginStage(CommandEncoder* encoder, GpuTimestampStage stage)
    {
        if (_activeSlot < 0)
        {
            return false;
        }

        int stageIndex = ValidateStage(stage);
        uint bit = 1u << stageIndex;
        if ((_activeStageMask & bit) != 0)
        {
            throw new InvalidOperationException($"GPU timestamp stage {stage} is already active.");
        }

        _activeStageMask |= bit;
        _recordedStageMask |= bit;
        _context.Api.CommandEncoderWriteTimestamp(
            encoder,
            _querySet,
            checked(2u + (uint)stageIndex * 2u));
        return true;
    }

    public void EndStage(CommandEncoder* encoder, GpuTimestampStage stage)
    {
        if (_activeSlot < 0)
        {
            return;
        }

        int stageIndex = ValidateStage(stage);
        uint bit = 1u << stageIndex;
        if ((_activeStageMask & bit) == 0)
        {
            throw new InvalidOperationException($"GPU timestamp stage {stage} is not active.");
        }

        _context.Api.CommandEncoderWriteTimestamp(
            encoder,
            _querySet,
            checked(3u + (uint)stageIndex * 2u));
        _activeStageMask &= ~bit;
    }

    public void EndFrame(CommandEncoder* encoder)
    {
        if (_activeSlot < 0)
        {
            return;
        }

        if (_activeStageMask != 0)
        {
            throw new InvalidOperationException("One or more GPU timestamp stages were not ended before the frame.");
        }

        int slot = _activeSlot;
        _slots[slot].RecordedStageMask = _recordedStageMask;
        ulong resolveOffset = (ulong)slot * ResolveStride;
        _context.Api.CommandEncoderWriteTimestamp(encoder, _querySet, 1);
        _context.Api.CommandEncoderResolveQuerySet(encoder, _querySet, 0, QueryCount, _resolveBuffer.BufferPtr, resolveOffset);
        _context.Api.CommandEncoderCopyBufferToBuffer(
            encoder,
            _resolveBuffer.BufferPtr,
            resolveOffset,
            _slots[slot].Readback.BufferPtr,
            0,
            TimestampBytes);
    }

    public void NotifySubmitted()
    {
        if (_activeSlot < 0)
        {
            return;
        }

        var slot = _slots[_activeSlot];
        slot.MetricsEpoch = _metricsEpoch;
        slot.MapTask = _context.Api.BufferMapAsyncTask(
            slot.Readback.BufferPtr,
            MapMode.Read,
            0,
            TimestampBytes);
        _submittedSamples++;
        _activeSlot = -1;
    }

    public void ResetMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        HarvestCompletedMappings();
        _metricsEpoch++;
        _submittedSamples = 0;
        _completedSamples = 0;
        _failedSamples = 0;
        _droppedSamples = 0;
        _totalMilliseconds = 0d;
        _lastMilliseconds = 0d;
        _maximumMilliseconds = 0d;
        Array.Clear(_stageCompletedSamples);
        Array.Clear(_stageTotalMilliseconds);
        Array.Clear(_stageLastMilliseconds);
        Array.Clear(_stageMaximumMilliseconds);
    }

    private void HarvestCompletedMappings()
    {
        for (int index = 0; index < SlotCount; index++)
        {
            var slot = _slots[index];
            var task = slot.MapTask;
            if (task == null || !task.IsCompleted)
            {
                continue;
            }

            slot.MapTask = null;
            bool shouldUnmap = false;
            try
            {
                if (task.GetAwaiter().GetResult() != BufferMapAsyncStatus.Success)
                {
                    if (slot.MetricsEpoch == _metricsEpoch)
                    {
                        _failedSamples++;
                    }
                    continue;
                }
                shouldUnmap = true;

                if (slot.MetricsEpoch != _metricsEpoch)
                {
                    continue;
                }

                var mapped = _context.Api.BufferGetConstMappedRange(
                    slot.Readback.BufferPtr,
                    0,
                    TimestampBytes);
                if (mapped == null)
                {
                    _failedSamples++;
                    continue;
                }

                var bytes = new ReadOnlySpan<byte>(mapped, checked((int)TimestampBytes));
                ulong begin = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
                ulong end = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
                if (end <= begin)
                {
                    _failedSamples++;
                    continue;
                }

                double milliseconds = (end - begin) / 1_000_000d;
                _lastMilliseconds = milliseconds;
                _totalMilliseconds += milliseconds;
                _maximumMilliseconds = Math.Max(_maximumMilliseconds, milliseconds);
                _completedSamples++;

                for (int stageIndex = 0; stageIndex < StageCount; stageIndex++)
                {
                    if ((slot.RecordedStageMask & (1u << stageIndex)) == 0)
                    {
                        continue;
                    }

                    int byteOffset = checked((2 + stageIndex * 2) * sizeof(ulong));
                    ulong stageBegin = BinaryPrimitives.ReadUInt64LittleEndian(bytes[byteOffset..]);
                    ulong stageEnd = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(byteOffset + sizeof(ulong))..]);
                    if (stageEnd <= stageBegin)
                    {
                        continue;
                    }

                    double stageMilliseconds = (stageEnd - stageBegin) / 1_000_000d;
                    _stageLastMilliseconds[stageIndex] = stageMilliseconds;
                    _stageTotalMilliseconds[stageIndex] += stageMilliseconds;
                    _stageMaximumMilliseconds[stageIndex] = Math.Max(
                        _stageMaximumMilliseconds[stageIndex],
                        stageMilliseconds);
                    _stageCompletedSamples[stageIndex]++;
                }
            }
            catch
            {
                _failedSamples++;
            }
            finally
            {
                if (shouldUnmap)
                {
                    _context.Api.BufferUnmap(slot.Readback.BufferPtr);
                }
            }
        }
    }

    private GpuTimestampStageMetrics GetStageMetrics(GpuTimestampStage stage)
    {
        int index = ValidateStage(stage);
        long completed = _stageCompletedSamples[index];
        return new GpuTimestampStageMetrics(
            completed,
            _stageTotalMilliseconds[index],
            _stageLastMilliseconds[index],
            completed == 0 ? 0d : _stageTotalMilliseconds[index] / completed,
            _stageMaximumMilliseconds[index]);
    }

    private static int ValidateStage(GpuTimestampStage stage)
    {
        int index = (int)stage;
        if ((uint)index >= StageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }
        return index;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        HarvestCompletedMappings();
        foreach (var slot in _slots)
        {
            slot.Readback.Dispose();
        }
        _resolveBuffer.Dispose();
        _context.Api.QuerySetRelease(_querySet);
        _disposed = true;
    }

    private sealed class Slot(GpuBuffer readback)
    {
        public GpuBuffer Readback { get; } = readback;
        public Task<BufferMapAsyncStatus>? MapTask { get; set; }
        public long MetricsEpoch { get; set; }
        public uint RecordedStageMask { get; set; }
    }
}
