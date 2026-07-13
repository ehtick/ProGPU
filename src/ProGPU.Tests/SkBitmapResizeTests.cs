using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkBitmapResizeTests
{
    [Fact]
    public void ResizeNearestUsesNativeHalfPixelTieRule()
    {
        using var source = new SKBitmap(new SKImageInfo(4, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        source.SetPixel(0, 0, new SKColor(10, 0, 0, 255));
        source.SetPixel(1, 0, new SKColor(20, 0, 0, 255));
        source.SetPixel(2, 0, new SKColor(30, 0, 0, 255));
        source.SetPixel(3, 0, new SKColor(40, 0, 0, 255));

        using var resized = source.Resize(
            new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

        Assert.NotNull(resized);
        Assert.Equal((byte)10, resized.GetPixel(0, 0).Red);
        Assert.Equal((byte)30, resized.GetPixel(1, 0).Red);
    }

    [Fact]
    public void ResizeLinearMatchesNativeStraightAlphaInterpolation()
    {
        using var source = CreateFourColorBitmap(SKAlphaType.Unpremul);

        using var resized = source.Resize(
            new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

        Assert.NotNull(resized);
        Assert.Equal(new SKColor(128, 128, 0, 192), resized.GetPixel(1, 0));
        Assert.Equal(new SKColor(128, 128, 128, 112), resized.GetPixel(1, 1));
        Assert.Equal(new SKColor(128, 128, 255, 32), resized.GetPixel(1, 2));
    }

    [Fact]
    public void ResizeAnisotropicUsesLinearCpuFallback()
    {
        using var source = CreateFourColorBitmap(SKAlphaType.Unpremul);
        using var linear = source.Resize(
            new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear));
        using var anisotropic = source.Resize(
            new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(8));

        Assert.NotNull(linear);
        Assert.NotNull(anisotropic);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Assert.Equal(linear.GetPixel(x, y), anisotropic.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void ResizeCubicPreservesMitchellAndCatmullRomKernels()
    {
        using var source = CreateFourColorBitmap(SKAlphaType.Unpremul);
        using var mitchell = source.Resize(
            new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var catmullRom = source.Resize(
            new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKCubicResampler.CatmullRom));

        Assert.NotNull(mitchell);
        Assert.NotNull(catmullRom);
        AssertColorNear(new SKColor(128, 127, 0, 193), mitchell.GetPixel(1, 0), tolerance: 1);
        AssertColorNear(new SKColor(128, 128, 0, 201), catmullRom.GetPixel(1, 0), tolerance: 1);
        Assert.NotEqual(mitchell.GetPixel(1, 0).Alpha, catmullRom.GetPixel(1, 0).Alpha);
    }

    [Fact]
    public void ResizePremultipliedBitmapInterpolatesStoredChannels()
    {
        using var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        source.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        source.SetPixel(1, 0, new SKColor(0, 255, 0, 0));

        using var resized = source.Resize(
            new SKImageInfo(3, 1, SKColorType.Rgba8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

        Assert.NotNull(resized);
        Assert.Equal(new SKColor(255, 0, 0, 128), resized.GetPixel(1, 0));
    }

    [Fact]
    public void ResizeReadsPaddedBgraRowsAndWritesRgb565()
    {
        var sourcePixels = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.Copy(
                new byte[]
                {
                    0, 0, 255, 255, 0, 255, 0, 255, 9, 9, 9, 9,
                    255, 0, 0, 255, 255, 255, 255, 255, 8, 8, 8, 8
                },
                0,
                sourcePixels,
                24);
            using var source = new SKBitmap();
            source.InstallPixels(
                new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                sourcePixels,
                rowBytes: 12);

            using var resized = source.Resize(
                new SKImageInfo(2, 2, SKColorType.Rgb565, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

            Assert.NotNull(resized);
            Assert.Equal(SKColors.Red, resized.GetPixel(0, 0));
            Assert.Equal(SKColors.Lime, resized.GetPixel(1, 0));
            Assert.Equal(SKColors.Blue, resized.GetPixel(0, 1));
            Assert.Equal(SKColors.White, resized.GetPixel(1, 1));
        }
        finally
        {
            Marshal.FreeHGlobal(sourcePixels);
        }
    }

    [Fact]
    public void ResizeRejectsEmptyOrUnsupportedDestination()
    {
        using var source = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        Assert.Null(source.Resize(
            new SKImageInfo(0, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            sampling));
        Assert.Null(source.Resize(
            new SKImageInfo(2, 2, SKColorType.Alpha8, SKAlphaType.Unpremul),
            sampling));
    }

    private static SKBitmap CreateFourColorBitmap(SKAlphaType alphaType)
    {
        var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, alphaType));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bitmap.SetPixel(1, 0, new SKColor(0, 255, 0, 128));
        bitmap.SetPixel(0, 1, new SKColor(0, 0, 255, 64));
        bitmap.SetPixel(1, 1, new SKColor(255, 255, 255, 0));
        return bitmap;
    }

    private static void AssertColorNear(SKColor expected, SKColor actual, int tolerance)
    {
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Alpha - expected.Alpha), 0, tolerance);
    }
}
