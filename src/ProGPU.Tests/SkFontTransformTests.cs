using System.Numerics;
using System.Reflection;
using ProGPU.Scene;
using ProGPU.Vector;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkFontTransformTests
{
    [Fact]
    public void GetTextPathAppliesHorizontalFontScale()
    {
        using var normal = new SKFont(SKTypeface.Default, 40f);
        using var stretched = new SKFont(SKTypeface.Default, 40f, scaleX: 1.5f);
        using var normalPath = normal.GetTextPath("A");
        using var stretchedPath = stretched.GetTextPath("A");

        AssertNear(normalPath.Bounds.Left * 1.5f, stretchedPath.Bounds.Left);
        AssertNear(normalPath.Bounds.Right * 1.5f, stretchedPath.Bounds.Right);
        AssertNear(normalPath.Bounds.Top, stretchedPath.Bounds.Top);
        AssertNear(normalPath.Bounds.Bottom, stretchedPath.Bounds.Bottom);
    }

    [Fact]
    public void FontSkewChangesMeasuredBoundsButNotTextPath()
    {
        using var normal = new SKFont(SKTypeface.Default, 40f);
        using var skewed = new SKFont(SKTypeface.Default, 40f, scaleX: 1f, skewX: 0.25f);
        using var normalPath = normal.GetTextPath("A");
        using var skewedPath = skewed.GetTextPath("A");

        normal.MeasureText("A", out var normalBounds);
        skewed.MeasureText("A", out var skewedBounds);

        Assert.Equal(normalPath.Bounds, skewedPath.Bounds);
        Assert.True(skewedBounds.Left < normalBounds.Left);
        AssertNear(normalBounds.Right, skewedBounds.Right);
        Assert.Equal(MathF.Floor(normalPath.Bounds.Left) - 1f, normalBounds.Left);
        Assert.Equal(MathF.Floor(normalPath.Bounds.Top) - 1f, normalBounds.Top);
        Assert.Equal(MathF.Ceiling(normalPath.Bounds.Right) + 1f, normalBounds.Right);
        Assert.Equal(MathF.Ceiling(normalPath.Bounds.Bottom) + 1f, normalBounds.Bottom);
    }

    [Fact]
    public void GetGlyphWidthsAppliesHorizontalFontScale()
    {
        using var normal = new SKFont(SKTypeface.Default, 40f);
        using var stretched = new SKFont(SKTypeface.Default, 40f, scaleX: 1.5f);
        var glyph = SKTypeface.Default.Font.GetGlyphIndex('A');
        Span<ushort> glyphs = [glyph];
        Span<float> normalWidths = stackalloc float[1];
        Span<float> stretchedWidths = stackalloc float[1];
        Span<SKRect> normalBounds = stackalloc SKRect[1];
        Span<SKRect> stretchedBounds = stackalloc SKRect[1];

        normal.GetGlyphWidths(glyphs, normalWidths, normalBounds);
        stretched.GetGlyphWidths(glyphs, stretchedWidths, stretchedBounds);

        AssertNear(normalWidths[0] * 1.5f, stretchedWidths[0]);
        Assert.True(stretchedBounds[0].Width > normalBounds[0].Width);
        AssertNear(normalBounds[0].Top, stretchedBounds[0].Top);
        AssertNear(normalBounds[0].Bottom, stretchedBounds[0].Bottom);
    }

    [Fact]
    public void DrawTextCarriesFontTransformWithoutRescalingGlyphPositions()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 128f, 64f);
        using var font = new SKFont(SKTypeface.Default, 40f, scaleX: 1.5f, skewX: 0.25f)
        {
            Embolden = true
        };
        using var paint = new SKPaint { Color = SKColors.Black };

        canvas.DrawText("AA", 4f, 48f, font, paint);

        var command = Assert.Single(
            context.Commands,
            static command => command.Type == RenderCommandType.DrawGlyphRun);
        Assert.True(command.HasFontTransform);
        Assert.Equal(new Vector2(1.5f, 0.25f), command.FontTransform);
        Assert.True(command.IsBold);
        Assert.True(command.UseVectorGlyphRendering);
        Assert.Equal(new Vector2(4f, 48f), command.Position);
        Assert.Equal(Vector2.Zero, command.GlyphPositions![0]);

        var expectedAdvance = font.Typeface.Font.GetAdvanceWidth(
            command.GlyphIndices![0],
            font.Size) * font.ScaleX;
        AssertNear(expectedAdvance, command.GlyphPositions[1].X);
        AssertNear(0f, command.GlyphPositions[1].Y);
    }

    [Fact]
    public void StrokeTextFallbackAppliesFontSkewToScaledGlyphPath()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 128f, 64f);
        using var font = new SKFont(SKTypeface.Default, 40f, scaleX: 1.5f, skewX: 0.25f);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        canvas.DrawText("A", 5f, 48f, font, paint);

        var command = Assert.Single(
            context.Commands,
            static command => command.Type == RenderCommandType.DrawPath);
        Assert.True(command.Path!.TryGetBounds(out var actualMin, out var actualMax));

        using var expectedPath = font.GetTextPath("A");
        expectedPath.Transform(new SKMatrix
        {
            ScaleX = 1f,
            SkewX = font.SkewX,
            ScaleY = 1f,
            Persp2 = 1f
        });
        var expectedBounds = expectedPath.Bounds;
        AssertNear(expectedBounds.Left + 5f, actualMin.X);
        AssertNear(expectedBounds.Top + 48f, actualMin.Y);
        AssertNear(expectedBounds.Right + 5f, actualMax.X);
        AssertNear(expectedBounds.Bottom + 48f, actualMax.Y);
    }

    [Fact]
    public void CompositorFontTransformKeepsPlacementOutsideGlyphLocalScale()
    {
        var outline = new PathGeometry();
        var figure = new PathFigure(new Vector2(2f, 3f), isClosed: false);
        figure.Segments.Add(new LineSegment(new Vector2(4f, 5f)));
        outline.Figures.Add(figure);
        var method = typeof(Compositor).GetMethod(
            "CreatePositionedGlyphOutline",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var transformed = Assert.IsType<PathGeometry>(method.Invoke(
            null,
            new object[]
            {
                outline,
                2f,
                new Vector2(10f, 20f),
                -0.25f,
                0f,
                false,
                1.5f
            }));

        var transformedFigure = Assert.Single(transformed.Figures);
        AssertNear(14.5f, transformedFigure.StartPoint.X);
        AssertNear(14f, transformedFigure.StartPoint.Y);
        var line = Assert.IsType<LineSegment>(Assert.Single(transformedFigure.Segments));
        AssertNear(19.5f, line.Point.X);
        AssertNear(10f, line.Point.Y);

        var svgTransformed = Assert.IsType<PathGeometry>(method.Invoke(
            null,
            new object[]
            {
                outline,
                2f,
                new Vector2(10f, 20f),
                -0.25f,
                0f,
                true,
                1.5f
            }));

        var svgFigure = Assert.Single(svgTransformed.Figures);
        AssertNear(17.5f, svgFigure.StartPoint.X);
        AssertNear(26f, svgFigure.StartPoint.Y);
        var svgLine = Assert.IsType<LineSegment>(Assert.Single(svgFigure.Segments));
        AssertNear(24.5f, svgLine.Point.X);
        AssertNear(30f, svgLine.Point.Y);
    }

    private static void AssertNear(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.001f, expected + 0.001f);
}
