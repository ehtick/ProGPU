using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasImageOverloadCompatibilityTests
{
    [Fact]
    public void PositionedImageOverloadsValidateImageBeforeTextureAccess()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 10f, 10f));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

#pragma warning disable CS0618
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, 1f, 2f));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, new SKPoint(1f, 2f)));
#pragma warning restore CS0618
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, 1f, 2f, sampling));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, new SKPoint(1f, 2f), sampling));
    }

    [Fact]
    public void RectangleImageOverloadsValidateImageBeforeTextureAccess()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 10f, 10f));
        var source = new SKRect(0f, 0f, 1f, 1f);
        var destination = new SKRect(2f, 3f, 7f, 9f);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

#pragma warning disable CS0618
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, destination));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, source, destination));
#pragma warning restore CS0618
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, destination, sampling));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawImage(null!, source, destination, sampling));
    }

    [Fact]
    public void LegacyFilterQualityMapsToNativeSamplingOptions()
    {
        using var paint = new SKPaint();

        SetLegacyFilterQuality(paint, 0);
        Assert.Equal(
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            paint.GetLegacyFilterQualitySampling());

        SetLegacyFilterQuality(paint, 1);
        Assert.Equal(
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            paint.GetLegacyFilterQualitySampling());

        SetLegacyFilterQuality(paint, 2);
        Assert.Equal(
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint.GetLegacyFilterQualitySampling());

        SetLegacyFilterQuality(paint, 3);
        Assert.Equal(
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            paint.GetLegacyFilterQualitySampling());
    }

    private static void SetLegacyFilterQuality(SKPaint paint, int value) =>
        typeof(SKPaint)
            .GetField("_filterQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(paint, value);
}
