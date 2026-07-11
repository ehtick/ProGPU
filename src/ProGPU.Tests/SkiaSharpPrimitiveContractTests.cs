using System.Numerics;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkiaSharpPrimitiveContractTests
{
    [Fact]
    public void PointISupportsSkiaSharpValueOperations()
    {
        var point = new SKPointI(3, 4);

        Assert.Equal(5, point.Length);
        Assert.Equal(25, point.LengthSquared);
        Assert.Equal(new SKPointI(5, 7), point + new SKSizeI(2, 3));
        Assert.Equal(5f, SKPointI.Distance(point, new SKPointI(0, 0)));
        Assert.Equal(new Vector2(3, 4), (Vector2)point);

        point.Offset(-3, -4);
        Assert.True(point.IsEmpty);
    }

    [Fact]
    public void Point3SupportsSkiaSharpValueOperations()
    {
        var point = new SKPoint3(1f, 2f, 3f);
        var offset = new SKPoint3(4f, 5f, 6f);

        Assert.Equal(new SKPoint3(5f, 7f, 9f), point + offset);
        Assert.Equal(new Vector3(1f, 2f, 3f), (Vector3)point);
        Assert.False(point.IsEmpty);
        Assert.True(SKPoint3.Empty.IsEmpty);
    }

    [Fact]
    public void SvgEnumsPreserveSkiaSharpNumericValues()
    {
        Assert.Equal(0, (int)SKTextEncoding.Utf8);
        Assert.Equal(3, (int)SKTextEncoding.GlyphId);
        Assert.Equal(0, (int)SKColorChannel.R);
        Assert.Equal(3, (int)SKColorChannel.A);
    }

    [Fact]
    public void TypefaceFallbackPreservesRequestedStyleMetadata()
    {
        using var typeface = SKTypeface.FromFamilyName(
            "ProGPU_Missing_Test_Family",
            new SKFontStyle(SKFontStyleWeight.Bold, SKFontStyleWidth.Condensed, SKFontStyleSlant.Italic));

        Assert.Equal((int)SKFontStyleWeight.Bold, typeface.FontWeight);
        Assert.Equal((int)SKFontStyleWidth.Condensed, typeface.FontWidth);
        Assert.Equal(SKFontStyleSlant.Italic, typeface.FontSlant);
    }

    [Fact]
    public void GenericSansSerifMatchesThePlatformDefaultTypeface()
    {
        using var generic = SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal);

        Assert.Same(SKTypeface.Default.Font, generic.Font);
        Assert.Equal(SKTypeface.Default.FamilyName, generic.FamilyName);
        Assert.NotEqual("Default", generic.FamilyName);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("Helvetica", generic.FamilyName);
        }
    }

    [Fact]
    public void FontMetricsKeepGlobalBoundsDistinctFromLineMetrics()
    {
        using var font = new SKFont(SKTypeface.Default, 24f);

        var metrics = font.Metrics;

        Assert.True(metrics.Top <= metrics.Ascent);
        Assert.True(metrics.Bottom >= metrics.Descent);
        Assert.True(metrics.Top < metrics.Ascent || metrics.Bottom > metrics.Descent);
        Assert.True(metrics.UnderlineThickness > 0f);
        Assert.True(metrics.UnderlinePosition >= 0f);
        Assert.True(metrics.StrikeoutThickness > 0f);
        Assert.True(metrics.StrikeoutPosition <= 0f);
    }
}
