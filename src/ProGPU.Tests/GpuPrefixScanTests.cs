using ProGPU.Compute;
using Xunit;

namespace ProGPU.Tests;

public sealed class GpuPrefixScanTests
{
    [Fact]
    public void CpuOracleHandlesEmptyAndSingleValueInputs()
    {
        Assert.Equal(0u, GpuPrefixScan.ExclusiveScanCpu([], []));

        uint[] output = new uint[1];
        Assert.Equal(7u, GpuPrefixScan.ExclusiveScanCpu([7u], output));
        Assert.Equal([0u], output);
    }

    [Fact]
    public void CpuOraclePreservesStableExclusiveOrderForNonPowerOfTwoInput()
    {
        uint[] input = [3u, 0u, 2u, 5u, 1u, 4u, 0u];
        uint[] output = new uint[input.Length];

        uint total = GpuPrefixScan.ExclusiveScanCpu(input, output);

        Assert.Equal(15u, total);
        Assert.Equal([0u, 3u, 3u, 5u, 10u, 11u, 15u], output);
    }

    [Fact]
    public void CpuOracleSupportsInPlaceScanAndWebGpuUnsignedWraparound()
    {
        uint[] values = [uint.MaxValue, 2u, 4u];

        uint total = GpuPrefixScan.ExclusiveScanCpu(values, values);

        Assert.Equal(5u, total);
        Assert.Equal([0u, uint.MaxValue, 1u], values);
    }

    [Fact]
    public void CpuOracleRejectsShortOutput()
    {
        Assert.Throws<ArgumentException>(() =>
            GpuPrefixScan.ExclusiveScanCpu([1u, 2u], new uint[1]));
    }
}
