using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPathIteratorCompatibilityTests
{
    private static readonly SKPoint Sentinel = new(-99f, -99f);

    [Fact]
    public void RawIteratorPreservesNativeVerbsPointsAndConicWeight()
    {
        using var path = new SKPath();
        path.MoveTo(1f, 2f);
        path.LineTo(3f, 4f);
        path.ConicTo(5f, 8f, 9f, 10f, 0.5f);
        path.CubicTo(11f, 12f, 13f, 14f, 15f, 16f);
        path.Close();

        using var iterator = path.CreateRawIterator();
        var points = NewPoints();

        Assert.Equal(SKPathVerb.Move, iterator.Peek());
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(new SKPoint(1f, 2f), points[0]);
        Assert.Equal(Sentinel, points[1]);

        Assert.Equal(SKPathVerb.Line, iterator.Peek());
        Assert.Equal(SKPathVerb.Line, iterator.Next(Reset(points)));
        Assert.Equal(new SKPoint(1f, 2f), points[0]);
        Assert.Equal(new SKPoint(3f, 4f), points[1]);
        Assert.Equal(Sentinel, points[2]);

        Assert.Equal(SKPathVerb.Conic, iterator.Next(Reset(points)));
        Assert.Equal(
            new[]
            {
                new SKPoint(3f, 4f),
                new SKPoint(5f, 8f),
                new SKPoint(9f, 10f),
                Sentinel,
            },
            points);
        Assert.Equal(0.5f, iterator.ConicWeight());

        Assert.Equal(SKPathVerb.Cubic, iterator.Next(Reset(points)));
        Assert.Equal(
            new[]
            {
                new SKPoint(9f, 10f),
                new SKPoint(11f, 12f),
                new SKPoint(13f, 14f),
                new SKPoint(15f, 16f),
            },
            points);
        Assert.Equal(0.5f, iterator.ConicWeight());

        Assert.Equal(SKPathVerb.Close, iterator.Next(Reset(points)));
        Assert.All(points, point => Assert.Equal(Sentinel, point));
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));
        Assert.Equal(SKPathVerb.Done, iterator.Peek());
    }

    [Fact]
    public void RawOvalEnumeratesFourNativeRationalConics()
    {
        using var path = new SKPath();
        path.AddOval(new SKRect(5f, 10f, 45f, 30f));
        using var iterator = path.CreateRawIterator();
        var points = NewPoints();

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(new SKPoint(45f, 20f), points[0]);

        var expected = new[]
        {
            (new SKPoint(45f, 20f), new SKPoint(45f, 30f), new SKPoint(25f, 30f)),
            (new SKPoint(25f, 30f), new SKPoint(5f, 30f), new SKPoint(5f, 20f)),
            (new SKPoint(5f, 20f), new SKPoint(5f, 10f), new SKPoint(25f, 10f)),
            (new SKPoint(25f, 10f), new SKPoint(45f, 10f), new SKPoint(45f, 20f)),
        };

        foreach (var (start, control, end) in expected)
        {
            Assert.Equal(SKPathVerb.Conic, iterator.Next(Reset(points)));
            Assert.Equal(start, points[0]);
            Assert.Equal(control, points[1]);
            Assert.Equal(end, points[2]);
            Assert.Equal(MathF.Sqrt(0.5f), iterator.ConicWeight(), 6);
        }

        Assert.Equal(SKPathVerb.Close, iterator.Next(Reset(points)));
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));
    }

    [Fact]
    public void CookedIteratorAddsNativeClosingLine()
    {
        using var path = new SKPath();
        path.MoveTo(1f, 2f);
        path.LineTo(3f, 4f);
        path.Close();
        using var iterator = path.CreateIterator(forceClose: false);
        var points = NewPoints();

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.True(iterator.IsCloseContour());
        Assert.Equal(SKPathVerb.Line, iterator.Next(Reset(points)));
        Assert.False(iterator.IsCloseLine());
        Assert.Equal(SKPathVerb.Line, iterator.Next(Reset(points)));
        Assert.True(iterator.IsCloseLine());
        Assert.Equal(new SKPoint(3f, 4f), points[0]);
        Assert.Equal(new SKPoint(1f, 2f), points[1]);
        Assert.Equal(SKPathVerb.Close, iterator.Next(Reset(points)));
        Assert.Equal(new SKPoint(1f, 2f), points[0]);
        Assert.Equal(SKPathVerb.Done, iterator.Next(Reset(points)));
    }

    [Fact]
    public void CookedIteratorForceClosesOpenContour()
    {
        using var path = new SKPath();
        path.MoveTo(10f, 11f);
        path.LineTo(12f, 13f);
        using var iterator = path.CreateIterator(forceClose: true);
        var points = NewPoints();

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.True(iterator.IsCloseContour());
        Assert.Equal(SKPathVerb.Line, iterator.Next(Reset(points)));
        Assert.Equal(SKPathVerb.Line, iterator.Next(Reset(points)));
        Assert.True(iterator.IsCloseLine());
        Assert.Equal(new SKPoint(12f, 13f), points[0]);
        Assert.Equal(new SKPoint(10f, 11f), points[1]);
        Assert.Equal(SKPathVerb.Close, iterator.Next(Reset(points)));
        Assert.Equal(SKPathVerb.Done, iterator.Next(Reset(points)));
    }

    [Fact]
    public void IteratorsRequireExactlyFourPointSlots()
    {
        using var path = new SKPath();
        path.MoveTo(1f, 2f);
        using var raw = path.CreateRawIterator();
        using var cooked = path.CreateIterator(forceClose: false);

        Assert.Throws<ArgumentNullException>(() => raw.Next((SKPoint[])null!));
        Assert.Throws<ArgumentException>(() => raw.Next(Array.Empty<SKPoint>()));
        Assert.Throws<ArgumentException>(() => raw.Next(new SKPoint[3]));
        Assert.Throws<ArgumentException>(() => raw.Next(new SKPoint[5]));
        Assert.Throws<ArgumentException>(() => raw.Next(Span<SKPoint>.Empty));
        Assert.Throws<ArgumentException>(() => cooked.Next(new SKPoint[3]));

        Span<SKPoint> points = stackalloc SKPoint[4];
        points.Fill(Sentinel);
        Assert.Equal(SKPathVerb.Move, raw.Next(points));
        Assert.Equal(new SKPoint(1f, 2f), points[0]);
        Assert.Equal(Sentinel, points[1]);
    }

    [Fact]
    public void ConicMetadataSurvivesCopyAddAndTransform()
    {
        using var source = new SKPath();
        source.MoveTo(0f, 0f);
        source.ConicTo(2f, 4f, 6f, 8f, 0.25f);

        using var copy = new SKPath(source);
        using var added = new SKPath();
        added.AddPath(copy);
        added.Transform(SKMatrix.CreateTranslation(10f, 20f));

        Assert.Equal(SKPathSegmentMask.Conic, added.SegmentMasks);
        using var iterator = added.CreateRawIterator();
        var points = NewPoints();
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(SKPathVerb.Conic, iterator.Next(Reset(points)));
        Assert.Equal(0.25f, iterator.ConicWeight());
        Assert.Equal(new SKPoint(10f, 20f), points[0]);
        Assert.Equal(new SKPoint(12f, 24f), points[1]);
        Assert.Equal(new SKPoint(16f, 28f), points[2]);
    }

    private static SKPoint[] NewPoints() => Reset(new SKPoint[4]);

    private static SKPoint[] Reset(SKPoint[] points)
    {
        Array.Fill(points, Sentinel);
        return points;
    }
}
