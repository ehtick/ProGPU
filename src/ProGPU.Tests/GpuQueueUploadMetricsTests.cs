namespace ProGPU.Tests;

using ProGPU.Backend;
using Xunit;

public class GpuQueueUploadMetricsTests
{
    [Fact]
    public void SnapshotDifferenceReportsOnlyWritesInMeasuredInterval()
    {
        var start = new GpuQueueUploadMetrics(7, 1_024, 3, 2_048);
        var end = new GpuQueueUploadMetrics(12, 5_120, 5, 10_240);

        var delta = end - start;

        Assert.Equal(5, delta.BufferWriteCount);
        Assert.Equal(4_096, delta.BufferBytes);
        Assert.Equal(2, delta.TextureWriteCount);
        Assert.Equal(8_192, delta.TextureBytes);
        Assert.Equal(7, delta.TotalWriteCount);
        Assert.Equal(12_288, delta.TotalBytes);
    }

    [Fact]
    public void SnapshotDifferenceClampsCounterResetToZero()
    {
        var start = new GpuQueueUploadMetrics(10, 100, 8, 80);
        var end = new GpuQueueUploadMetrics(1, 10, 2, 20);

        Assert.Equal(default, end - start);
    }
}
