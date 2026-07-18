using ProGPU.Compute;
using Xunit;

namespace ProGPU.Tests;

public sealed class GpuCompactionRouterTests
{
    [Fact]
    public void RouterRequiresRepeatedMeasuredWinsAndMinimumItemCount()
    {
        var router = new GpuCompactionRouter(
            minimumGpuItemCount: 100,
            requiredConsecutiveWins: 3,
            switchMarginMilliseconds: 0.01,
            smoothingFactor: 1);

        router.RecordSample(99, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        router.RecordSample(99, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        router.RecordSample(99, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        Assert.False(router.UseGpu);

        router.RecordSample(100, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        router.RecordSample(100, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        Assert.False(router.UseGpu);
        router.RecordSample(100, cpuMilliseconds: 1, gpuMilliseconds: 0.1);
        Assert.True(router.UseGpu);
        Assert.True(router.ShouldUseGpu(100));
        Assert.False(router.ShouldUseGpu(50));
    }

    [Fact]
    public void RouterUsesHysteresisToReturnToCpu()
    {
        var router = new GpuCompactionRouter(1, 2, 0.01, 1);
        router.RecordSample(10, 1, 0.1);
        router.RecordSample(10, 1, 0.1);
        Assert.True(router.UseGpu);

        router.RecordSample(10, 0.1, 1);
        Assert.True(router.UseGpu);
        router.RecordSample(10, 0.1, 1);
        Assert.False(router.UseGpu);
    }

    [Fact]
    public void RouterIgnoresInvalidTimingSamples()
    {
        var router = new GpuCompactionRouter(1, 1, 0, 1);
        router.RecordSample(10, double.NaN, 0);
        router.RecordSample(10, 1, double.PositiveInfinity);
        Assert.False(router.UseGpu);
        Assert.Equal(0, router.CpuAverageMilliseconds);
        Assert.Equal(0, router.GpuAverageMilliseconds);
    }
}
