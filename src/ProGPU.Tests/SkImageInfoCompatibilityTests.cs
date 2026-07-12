using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkImageInfoCompatibilityTests
{
    [Fact]
    public void DefaultsMatchNativePlatformPixelOrder()
    {
        var info = new SKImageInfo(3, 2);

        Assert.Equal(SKColorType.Rgba8888, SKImageInfo.PlatformColorType);
        Assert.Equal(24, SKImageInfo.PlatformColorAlphaShift);
        Assert.Equal(0, SKImageInfo.PlatformColorRedShift);
        Assert.Equal(8, SKImageInfo.PlatformColorGreenShift);
        Assert.Equal(16, SKImageInfo.PlatformColorBlueShift);
        Assert.Equal(SKColorType.Rgba8888, info.ColorType);
        Assert.Equal(SKAlphaType.Premul, info.AlphaType);
        Assert.Null(info.ColorSpace);
        Assert.True(SKImageInfo.Empty.IsEmpty);
    }

    [Fact]
    public void DerivedSizesPreserveCheckedAndWideContracts()
    {
        var rgba = new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var unknown = new SKImageInfo(3, 2, SKColorType.Unknown, SKAlphaType.Unknown);

        Assert.Equal(4, rgba.BytesPerPixel);
        Assert.Equal(2, rgba.BitShiftPerPixel);
        Assert.Equal(32, rgba.BitsPerPixel);
        Assert.Equal(12, rgba.RowBytes);
        Assert.Equal(12L, rgba.RowBytes64);
        Assert.Equal(24, rgba.BytesSize);
        Assert.Equal(24L, rgba.BytesSize64);
        Assert.Equal(new SKSizeI(3, 2), rgba.Size);
        Assert.Equal(SKRectI.Create(3, 2), rgba.Rect);
        Assert.True(rgba.IsOpaque);
        Assert.False(rgba.IsEmpty);
        Assert.Equal(0, unknown.BytesPerPixel);
        Assert.Throws<OverflowException>(() =>
            new SKImageInfo(int.MaxValue, 2, SKColorType.Rgba8888).BytesSize);
        Assert.Equal((long)int.MaxValue * 8, new SKImageInfo(
            int.MaxValue,
            2,
            SKColorType.Rgba8888).BytesSize64);
    }

    [Fact]
    public void WithMethodsAndEqualityCopyOnlyRequestedMetadata()
    {
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        var original = new SKImageInfo(
            10,
            20,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul,
            colorSpace);

        Assert.Equal(original, original.WithSize(10, 20));
        Assert.NotEqual(original, original.WithSize(new SKSizeI(11, 20)));
        Assert.Equal(SKColorType.RgbaF16, original.WithColorType(SKColorType.RgbaF16).ColorType);
        Assert.Equal(SKAlphaType.Opaque, original.WithAlphaType(SKAlphaType.Opaque).AlphaType);
        Assert.Null(original.WithColorSpace(null).ColorSpace);
        Assert.Equal(original.GetHashCode(), original.WithSize(10, 20).GetHashCode());
        Assert.Equal(10, original.Width);
        Assert.Equal(20, original.Height);
    }

    [Fact]
    public void ColorTypeValuesAndStorageMatchNative()
    {
        var expected = new (SKColorType Type, int Value, int Bytes)[]
        {
            (SKColorType.Unknown, 0, 0),
            (SKColorType.Alpha8, 1, 1),
            (SKColorType.Rgb565, 2, 2),
            (SKColorType.Argb4444, 3, 2),
            (SKColorType.Rgba8888, 4, 4),
            (SKColorType.Rgb888x, 5, 4),
            (SKColorType.Bgra8888, 6, 4),
            (SKColorType.Rgba1010102, 7, 4),
            (SKColorType.Rgb101010x, 8, 4),
            (SKColorType.Gray8, 9, 1),
            (SKColorType.RgbaF16, 10, 8),
            (SKColorType.RgbaF16Clamped, 11, 8),
            (SKColorType.RgbaF32, 12, 16),
            (SKColorType.Rg88, 13, 2),
            (SKColorType.AlphaF16, 14, 2),
            (SKColorType.RgF16, 15, 4),
            (SKColorType.Alpha16, 16, 2),
            (SKColorType.Rg1616, 17, 4),
            (SKColorType.Rgba16161616, 18, 8),
            (SKColorType.Bgra1010102, 19, 4),
            (SKColorType.Bgr101010x, 20, 4),
            (SKColorType.Bgr101010xXR, 21, 4),
            (SKColorType.Srgba8888, 22, 4),
            (SKColorType.R8Unorm, 23, 1),
            (SKColorType.Rgba10x6, 24, 8),
            (SKColorType.Bgra10101010XR, 25, 8),
            (SKColorType.RgbF16F16F16x, 26, 8),
            (SKColorType.R16Unorm, 27, 2),
            (SKColorType.RF16, 28, 2),
        };

        Assert.Equal(expected.Length, Enum.GetValues<SKColorType>().Length);
        foreach (var (type, value, bytes) in expected)
        {
            var info = new SKImageInfo(1, 1, type, SKAlphaType.Premul);
            Assert.Equal(value, (int)type);
            Assert.Equal(bytes, info.BytesPerPixel);
            Assert.Equal(bytes switch
            {
                2 => 1,
                4 => 2,
                8 => 3,
                16 => 4,
                _ => 0,
            }, info.BitShiftPerPixel);
        }
    }

    [Fact]
    public void CompatibilityEnumsMatchNativeMetadata()
    {
        Assert.Equal(
            new[]
            {
                SKEncodedImageFormat.Bmp,
                SKEncodedImageFormat.Gif,
                SKEncodedImageFormat.Ico,
                SKEncodedImageFormat.Jpeg,
                SKEncodedImageFormat.Png,
                SKEncodedImageFormat.Wbmp,
                SKEncodedImageFormat.Webp,
                SKEncodedImageFormat.Pkm,
                SKEncodedImageFormat.Ktx,
                SKEncodedImageFormat.Astc,
                SKEncodedImageFormat.Dng,
                SKEncodedImageFormat.Heif,
                SKEncodedImageFormat.Avif,
                SKEncodedImageFormat.Jpegxl,
            },
            Enum.GetValues<SKEncodedImageFormat>());

        var assembly = typeof(SKImageInfo).Assembly;
        var filterQuality = assembly.GetType("SkiaSharp.SKFilterQuality", throwOnError: true)!;
        Assert.Equal(new[] { "None", "Low", "Medium", "High" }, Enum.GetNames(filterQuality));
        Assert.Equal(new[] { 0, 1, 2, 3 }, GetEnumValues(filterQuality));
        var obsolete = filterQuality.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.Equal("Use SKSamplingOptions instead.", obsolete.Message);
        Assert.True(obsolete.IsError);

        var allocFlags = assembly.GetType("SkiaSharp.SKBitmapAllocFlags", throwOnError: true)!;
        Assert.NotNull(allocFlags.GetCustomAttribute<FlagsAttribute>());
        Assert.Equal(new[] { "None", "ZeroPixels" }, Enum.GetNames(allocFlags));
        Assert.Equal(new[] { 0, 1 }, GetEnumValues(allocFlags));
    }

    private static int[] GetEnumValues(Type enumType) =>
        Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32).ToArray();
}
