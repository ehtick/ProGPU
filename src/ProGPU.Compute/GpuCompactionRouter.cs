namespace ProGPU.Compute;

/// <summary>
/// Allocation-free hysteresis router for choosing cached CPU replay or GPU visibility compaction.
/// It requires repeated measured wins before changing lanes, preventing frame-to-frame oscillation.
/// </summary>
public sealed class GpuCompactionRouter
{
    private readonly int _minimumGpuItemCount;
    private readonly int _requiredConsecutiveWins;
    private readonly double _switchMarginMilliseconds;
    private readonly double _smoothingFactor;
    private double _cpuAverageMilliseconds;
    private double _gpuAverageMilliseconds;
    private int _gpuWins;
    private int _cpuWins;
    private bool _hasSample;

    public GpuCompactionRouter(
        int minimumGpuItemCount = 512,
        int requiredConsecutiveWins = 4,
        double switchMarginMilliseconds = 0.02,
        double smoothingFactor = 0.2)
    {
        if (minimumGpuItemCount < 1) throw new ArgumentOutOfRangeException(nameof(minimumGpuItemCount));
        if (requiredConsecutiveWins < 1) throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveWins));
        if (!double.IsFinite(switchMarginMilliseconds) || switchMarginMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(switchMarginMilliseconds));
        }
        if (!double.IsFinite(smoothingFactor) || smoothingFactor <= 0 || smoothingFactor > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingFactor));
        }

        _minimumGpuItemCount = minimumGpuItemCount;
        _requiredConsecutiveWins = requiredConsecutiveWins;
        _switchMarginMilliseconds = switchMarginMilliseconds;
        _smoothingFactor = smoothingFactor;
    }

    public bool UseGpu { get; private set; }

    public double CpuAverageMilliseconds => _cpuAverageMilliseconds;

    public double GpuAverageMilliseconds => _gpuAverageMilliseconds;

    public bool ShouldUseGpu(int itemCount) =>
        UseGpu && itemCount >= _minimumGpuItemCount;

    public void RecordSample(
        int itemCount,
        double cpuMilliseconds,
        double gpuMilliseconds)
    {
        if (itemCount < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (!IsValidDuration(cpuMilliseconds) || !IsValidDuration(gpuMilliseconds))
        {
            return;
        }

        if (!_hasSample)
        {
            _cpuAverageMilliseconds = cpuMilliseconds;
            _gpuAverageMilliseconds = gpuMilliseconds;
            _hasSample = true;
        }
        else
        {
            _cpuAverageMilliseconds +=
                (cpuMilliseconds - _cpuAverageMilliseconds) * _smoothingFactor;
            _gpuAverageMilliseconds +=
                (gpuMilliseconds - _gpuAverageMilliseconds) * _smoothingFactor;
        }

        if (itemCount < _minimumGpuItemCount)
        {
            _gpuWins = 0;
            _cpuWins++;
        }
        else if (_gpuAverageMilliseconds + _switchMarginMilliseconds < _cpuAverageMilliseconds)
        {
            _gpuWins++;
            _cpuWins = 0;
        }
        else if (_cpuAverageMilliseconds + _switchMarginMilliseconds < _gpuAverageMilliseconds)
        {
            _cpuWins++;
            _gpuWins = 0;
        }
        else
        {
            _gpuWins = 0;
            _cpuWins = 0;
        }

        if (!UseGpu && _gpuWins >= _requiredConsecutiveWins)
        {
            UseGpu = true;
            _gpuWins = 0;
        }
        else if (UseGpu && _cpuWins >= _requiredConsecutiveWins)
        {
            UseGpu = false;
            _cpuWins = 0;
        }
    }

    public void Reset()
    {
        UseGpu = false;
        _cpuAverageMilliseconds = 0;
        _gpuAverageMilliseconds = 0;
        _gpuWins = 0;
        _cpuWins = 0;
        _hasSample = false;
    }

    private static bool IsValidDuration(double value) =>
        double.IsFinite(value) && value >= 0;
}
