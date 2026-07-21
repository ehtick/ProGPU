using ProGPU.Android;
using Xunit;

namespace ProGPU.Tests;

public sealed class AndroidScrollDeltaPolicyTests
{
    [Fact]
    public void NormalizedWheelAxesBecomeLogicalPixelsAndRemainPrecise()
    {
        AndroidScrollDelta result = AndroidScrollDeltaPolicy.Convert(
            density: 3f,
            horizontalScrollFactor: 150f,
            verticalScrollFactor: 192f,
            rawHorizontal: 1f,
            rawVertical: -2f,
            gestureHorizontalPixels: 0f,
            gestureVerticalPixels: 0f);

        Assert.Equal(50f, result.X);
        Assert.Equal(-128f, result.Y);
        Assert.True(result.IsPrecise);
    }

    [Fact]
    public void AndroidFourteenGestureDistanceOverridesLegacyAxisPerDimension()
    {
        AndroidScrollDelta result = AndroidScrollDeltaPolicy.Convert(
            density: 2f,
            horizontalScrollFactor: 100f,
            verticalScrollFactor: 100f,
            rawHorizontal: 2f,
            rawVertical: -3f,
            gestureHorizontalPixels: 24f,
            gestureVerticalPixels: -18f);

        Assert.Equal(12f, result.X);
        Assert.Equal(-9f, result.Y);
        Assert.True(result.IsPrecise);
    }

    [Fact]
    public void MissingGestureAxisFallsBackIndependently()
    {
        AndroidScrollDelta result = AndroidScrollDeltaPolicy.Convert(
            density: 2f,
            horizontalScrollFactor: 80f,
            verticalScrollFactor: 120f,
            rawHorizontal: 0.5f,
            rawVertical: -1f,
            gestureHorizontalPixels: 0f,
            gestureVerticalPixels: -20f);

        Assert.Equal(20f, result.X);
        Assert.Equal(-10f, result.Y);
    }

    [Fact]
    public void InvalidDensityAndAxisValuesAreContained()
    {
        AndroidScrollDelta result = AndroidScrollDeltaPolicy.Convert(
            density: float.NaN,
            horizontalScrollFactor: 80f,
            verticalScrollFactor: 120f,
            rawHorizontal: float.PositiveInfinity,
            rawVertical: 0f,
            gestureHorizontalPixels: 0f,
            gestureVerticalPixels: 0f);

        Assert.Equal(0f, result.X);
        Assert.Equal(0f, result.Y);
        Assert.False(result.IsPrecise);
    }
}
