using ProGPU.iOS;
using Xunit;

namespace ProGPU.Tests;

public sealed class IosIndirectScrollPolicyTests
{
    [Theory]
    [InlineData(18f, true, 18f)]
    [InlineData(-3.5f, true, -3.5f)]
    [InlineData(18f, false, 1f)]
    [InlineData(-18f, false, -1f)]
    public void PreservesTrackpadPixelsAndNormalizesMouseNotches(
        float input,
        bool precise,
        float expected)
    {
        Assert.Equal(expected, IosIndirectScrollPolicy.NormalizeScrollDelta(input, precise));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    [InlineData(1.1f)]
    [InlineData(2f)]
    public void PinchWheelMappingRoundTripsRelativeScale(float scale)
    {
        float wheel = IosIndirectScrollPolicy.PinchScaleToWheelDelta(scale);
        Assert.Equal(scale, IosIndirectScrollPolicy.WheelDeltaToScale(wheel), 5);
    }
}
