using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkFontTextApiTests
{
    [Fact]
    public void UnicodeConversionCountsSupplementaryScalarsAndRejectsMalformedUtf16()
    {
        using var font = new SKFont(SKTypeface.Default, 40f);
        const string text = "A\U0001F600B";

        var glyphs = font.GetGlyphs(text);

        Assert.Equal(3, font.CountGlyphs(text));
        Assert.Equal(3, glyphs.Length);
        Assert.Equal(font.GetGlyph('A'), glyphs[0]);
        Assert.Equal(font.GetGlyph(0x1f600), glyphs[1]);
        Assert.Equal(font.GetGlyph('B'), glyphs[2]);

        var malformed = new string(new[] { 'A', '\ud800', 'B' });
        Assert.Equal(0, font.CountGlyphs(malformed));
        Assert.Empty(font.GetGlyphs(malformed));
        Assert.True(font.ContainsGlyphs(malformed));
        Assert.Equal(0f, font.MeasureText(malformed, out var bounds));
        Assert.True(bounds.IsEmpty);
    }

    [Fact]
    public void EncodedTextConversionMatchesUtf16GlyphSequence()
    {
        using var font = new SKFont(SKTypeface.Default, 40f);
        const string text = "A\U0001F600B";
        var expected = font.GetGlyphs(text);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var utf16 = MemoryMarshal.AsBytes(text.AsSpan()).ToArray();
        var utf32 = Encoding.UTF32.GetBytes(text);
        var glyphIds = MemoryMarshal.AsBytes(expected.AsSpan()).ToArray();

        Assert.Equal(expected, font.GetGlyphs(utf8, SKTextEncoding.Utf8));
        Assert.Equal(expected, font.GetGlyphs(utf16, SKTextEncoding.Utf16));
        Assert.Equal(expected, font.GetGlyphs(utf32, SKTextEncoding.Utf32));
        Assert.Equal(expected, font.GetGlyphs(glyphIds, SKTextEncoding.GlyphId));
        Assert.Equal(3, font.CountGlyphs(utf8, SKTextEncoding.Utf8));
        Assert.Equal(3, font.CountGlyphs(utf16, SKTextEncoding.Utf16));
        Assert.Equal(3, font.CountGlyphs(utf32, SKTextEncoding.Utf32));
        Assert.Equal(3, font.CountGlyphs(glyphIds, SKTextEncoding.GlyphId));

        Assert.Empty(font.GetGlyphs(
            new byte[] { 0x41, 0xf0, 0x28, 0x8c, 0x28, 0x42 },
            SKTextEncoding.Utf8));
        Assert.Empty(font.GetGlyphs(new byte[] { 0x41, 0x00, 0x42 }, SKTextEncoding.Utf16));
        Assert.Empty(font.GetGlyphs(new byte[] { 0x41, 0x00, 0x00 }, SKTextEncoding.Utf32));
    }

    [Fact]
    public void PositionsOffsetsAndWidthsUseOneAdvanceSequence()
    {
        using var font = new SKFont(SKTypeface.Default, 40f, scaleX: 1.25f);
        var glyphs = font.GetGlyphs("AV");
        var widths = font.GetGlyphWidths(glyphs, out var bounds);
        var offsets = font.GetGlyphOffsets(glyphs, 3.5f);
        var positions = font.GetGlyphPositions(glyphs, new SKPoint(3.5f, -2f));

        Assert.Equal(2, widths.Length);
        Assert.Equal(2, bounds.Length);
        AssertClose(3.5f, offsets[0]);
        AssertClose(3.5f + widths[0], offsets[1]);
        AssertClose(offsets[0], positions[0].X);
        AssertClose(offsets[1], positions[1].X);
        AssertClose(-2f, positions[0].Y);
        AssertClose(-2f, positions[1].Y);
        Assert.Equal(widths, font.GetGlyphWidths("AV"));

        var destination = new ushort[] { 999, 999, 999, 999 };
        font.GetGlyphs("AV", destination);
        Assert.Equal(glyphs[0], destination[0]);
        Assert.Equal(glyphs[1], destination[1]);
        Assert.Equal((ushort)999, destination[2]);
        Assert.Equal((ushort)999, destination[3]);

        Assert.Throws<ArgumentException>(() => font.GetGlyphs("AV", new ushort[1]));
        Assert.Throws<ArgumentException>(() =>
            font.GetGlyphPositions(glyphs, new SKPoint[1]));
        Assert.Throws<ArgumentException>(() =>
            font.GetGlyphWidths(glyphs, new float[1], new SKRect[1]));
    }

    [Fact]
    public void BreakTextReturnsConsumedCodeUnitsAndBytes()
    {
        using var font = new SKFont(SKTypeface.Default, 40f);
        const string text = "A\U0001F600B";
        var widths = font.GetGlyphWidths(text);
        var expectedWidth = widths[0] + widths[1];
        var maxWidth = expectedWidth + 0.001f;

        var utf16Count = font.BreakText(text, maxWidth, out var utf16Width);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var utf8Count = font.BreakText(
            utf8,
            SKTextEncoding.Utf8,
            maxWidth,
            out var utf8Width);

        Assert.Equal(3, utf16Count);
        Assert.Equal(5, utf8Count);
        AssertClose(expectedWidth, utf16Width);
        AssertClose(expectedWidth, utf8Width);
        Assert.Equal(text.Length, font.BreakText(text, float.NaN, out var fullWidth));
        AssertClose(widths.Sum(), fullWidth);
    }

    [Fact]
    public void PaintAwareGlyphBoundsUseTheFilledStrokeGeometry()
    {
        using var font = new SKFont(SKTypeface.Default, 40f, scaleX: 1.25f, skewX: 0.2f);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.StrokeAndFill,
            StrokeWidth = 4f,
            StrokeJoin = SKStrokeJoin.Miter,
        };
        var glyph = font.GetGlyph('g');
        using var glyphPath = font.GetGlyphPath(glyph);
        Assert.NotNull(glyphPath);
        using var fillPath = Assert.IsType<SKPath>(paint.GetFillPath(glyphPath));

        font.GetGlyphWidths(new[] { glyph }, out var bounds, paint);
        font.MeasureText("g", out var measuredBounds, paint);
        var pathBounds = fillPath.Bounds;
        var expected = new SKRect(
            MathF.Floor(pathBounds.Left),
            MathF.Floor(pathBounds.Top),
            MathF.Ceiling(pathBounds.Right),
            MathF.Ceiling(pathBounds.Bottom));

        Assert.Equal(expected, bounds[0]);
        Assert.Equal(expected, measuredBounds);
    }

    [Fact]
    public void GlyphPathCallbackUsesCanonicalPathAndSizeMatrix()
    {
        using var font = new SKFont(SKTypeface.Default, 40f, scaleX: 1.25f, skewX: 0.2f);
        var glyph = font.GetGlyph('g');
        SKPath? callbackPath = null;
        var callbackMatrix = SKMatrix.Identity;
        var callbackCount = 0;

        font.GetGlyphPaths(font.GetGlyphs(" g"), (path, matrix) =>
        {
            if (callbackCount++ == 0)
            {
                Assert.Null(path);
            }
            else
            {
                callbackPath = path == null ? null : new SKPath(path);
                callbackMatrix = matrix;
            }
        });

        Assert.Equal(2, callbackCount);
        Assert.NotNull(callbackPath);
        using (callbackPath)
        using (var expectedPath = font.GetGlyphPath(glyph))
        {
            callbackPath.Transform(callbackMatrix);
            AssertRectClose(expectedPath!.Bounds, callbackPath.Bounds);
        }
    }

    [Fact]
    public void TextOnPathMorphsGlyphContoursForEveryAlignment()
    {
        using var font = new SKFont(SKTypeface.Default, 32f, skewX: 0.15f);
        using var baseline = new SKPath();
        baseline.MoveTo(0f, 50f);
        baseline.CubicTo(60f, 5f, 140f, 95f, 220f, 50f);

        using var left = font.GetTextPathOnPath("Ag", baseline, SKTextAlign.Left);
        using var center = font.GetTextPathOnPath("Ag", baseline, SKTextAlign.Center);
        using var right = font.GetTextPathOnPath("Ag", baseline, SKTextAlign.Right);

        Assert.False(left.IsEmpty);
        Assert.False(center.IsEmpty);
        Assert.False(right.IsEmpty);
        Assert.True(left.Bounds.Left < center.Bounds.Left);
        Assert.True(center.Bounds.Left < right.Bounds.Left);
        Assert.True(left.Bounds.Top < left.Bounds.Bottom);
        Assert.True(center.Bounds.Top < center.Bounds.Bottom);
        Assert.True(right.Bounds.Top < right.Bounds.Bottom);
    }

    [Fact]
    public void SpacingMatchesReturnedFontMetrics()
    {
        using var defaultFont = new SKFont();
        using var nullTypefaceFont = new SKFont(null);
        using var font = new SKFont(SKTypeface.Default, 40f);

        var spacing = font.GetFontMetrics(out var metrics);

        Assert.False(defaultFont.Subpixel);
        Assert.True(defaultFont.BaselineSnap);
        Assert.Same(SKTypeface.Default.Font, nullTypefaceFont.Typeface.Font);
        nullTypefaceFont.Typeface = null!;
        Assert.Same(SKTypeface.Default.Font, nullTypefaceFont.Typeface.Font);
        AssertClose(metrics.Descent - metrics.Ascent + metrics.Leading, spacing);
        AssertClose(spacing, font.Spacing);
    }

    private static void AssertRectClose(SKRect expected, SKRect actual)
    {
        AssertClose(expected.Left, actual.Left);
        AssertClose(expected.Top, actual.Top);
        AssertClose(expected.Right, actual.Right);
        AssertClose(expected.Bottom, actual.Bottom);
    }

    private static void AssertClose(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.001f, expected + 0.001f);
}
