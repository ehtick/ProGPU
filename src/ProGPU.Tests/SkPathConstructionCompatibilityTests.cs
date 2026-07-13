using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPathConstructionCompatibilityTests
{
    private static readonly SKRect Oval = new(10f, 20f, 50f, 40f);

    [Fact]
    public void AddArcQuarterMatchesNativeRationalConic()
    {
        using var path = new SKPath();
        path.AddArc(Oval, 0f, 90f);
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        AssertNear(new SKPoint(50f, 30f), points[0]);
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        AssertNear(new SKPoint(50f, 30f), points[0]);
        AssertNear(new SKPoint(50f, 40f), points[1]);
        AssertNear(new SKPoint(30f, 40f), points[2]);
        Assert.Equal(MathF.Sqrt(0.5f), iterator.ConicWeight(), 6);
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));
    }

    [Fact]
    public void AddArcFullSweepBuildsClosedOval()
    {
        using var path = new SKPath();
        path.AddArc(Oval, 0f, 360f);
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        for (var conic = 0; conic < 4; conic++)
        {
            Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
            Assert.Equal(MathF.Sqrt(0.5f), iterator.ConicWeight(), 6);
        }
        Assert.Equal(SKPathVerb.Close, iterator.Next(points));
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));
        Assert.True(path.IsOval);
        Assert.Equal(Oval, path.GetOvalBounds());
    }

    [Fact]
    public void AddArcNegativeSweepReversesDirectionAndZeroSweepDoesNothing()
    {
        using var negative = new SKPath();
        negative.AddArc(Oval, 0f, -90f);
        using var iterator = negative.CreateRawIterator();
        var points = new SKPoint[4];
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        AssertNear(new SKPoint(50f, 20f), points[1]);
        AssertNear(new SKPoint(30f, 20f), points[2]);

        using var zero = new SKPath();
        zero.AddArc(Oval, 30f, 0f);
        Assert.True(zero.IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OvalArcToPreservesNativeConnectorPolicy(bool forceMove)
    {
        using var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.ArcTo(Oval, 0f, 90f, forceMove);
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        AssertNear(forceMove ? new SKPoint(50f, 30f) : new SKPoint(0f, 0f), points[0]);
        if (!forceMove)
        {
            Assert.Equal(SKPathVerb.Line, iterator.Next(points));
            AssertNear(new SKPoint(50f, 30f), points[1]);
        }
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        AssertNear(new SKPoint(30f, 40f), points[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullOvalArcToStopsAfterConnector(bool forceMove)
    {
        using var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.ArcTo(Oval, 0f, 360f, forceMove);

        Assert.Equal(forceMove ? 1 : 2, path.VerbCount);
        Assert.Equal(new SKPoint(50f, 30f), path.LastPoint);
        Assert.Equal(
            forceMove ? (SKPathSegmentMask)0 : SKPathSegmentMask.Line,
            path.SegmentMasks);
    }

    [Fact]
    public void TangentArcMatchesNativeLineAndQuarterConic()
    {
        using var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.ArcTo(new SKPoint(10f, 0f), new SKPoint(10f, 10f), 5f);
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(SKPathVerb.Line, iterator.Next(points));
        AssertNear(new SKPoint(5f, 0f), points[1]);
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        AssertNear(new SKPoint(10f, 0f), points[1]);
        AssertNear(new SKPoint(10f, 5f), points[2]);

        using var zeroRadius = new SKPath();
        zeroRadius.MoveTo(0f, 0f);
        zeroRadius.ArcTo(10f, 0f, 10f, 10f, 0f);
        Assert.Equal(new[] { new SKPoint(0f, 0f), new SKPoint(10f, 0f) }, zeroRadius.Points);
        Assert.Equal(SKPathSegmentMask.Line, zeroRadius.SegmentMasks);
    }

    [Fact]
    public void IndexedRectRotatesNativeCornerOrder()
    {
        var rect = new SKRect(10f, 20f, 50f, 70f);
        var corners = new[]
        {
            new SKPoint(10f, 20f),
            new SKPoint(50f, 20f),
            new SKPoint(50f, 70f),
            new SKPoint(10f, 70f),
        };

        foreach (var direction in new[] { SKPathDirection.Clockwise, SKPathDirection.CounterClockwise })
        {
            var step = direction == SKPathDirection.Clockwise ? 1 : -1;
            for (uint startIndex = 0; startIndex < 4; startIndex++)
            {
                using var path = new SKPath();
                path.AddRect(rect, direction, startIndex);
                Assert.Equal(4, path.PointCount);
                for (var point = 0; point < 4; point++)
                {
                    var index = ((int)startIndex + step * point + 16) % 4;
                    Assert.Equal(corners[index], path[point]);
                }
                Assert.Equal(rect, path.GetRect(out var closed, out var actualDirection));
                Assert.True(closed);
                Assert.Equal(direction, actualDirection);
            }
        }
    }

    [Fact]
    public void IndexedRoundRectRotatesEightNativeBoundaryPositions()
    {
        using var roundRect = new SKRoundRect(new SKRect(0f, 0f, 100f, 50f), 10f, 5f);
        var boundary = new[]
        {
            new SKPoint(10f, 0f),
            new SKPoint(90f, 0f),
            new SKPoint(100f, 5f),
            new SKPoint(100f, 45f),
            new SKPoint(90f, 50f),
            new SKPoint(10f, 50f),
            new SKPoint(0f, 45f),
            new SKPoint(0f, 5f),
        };

        foreach (var direction in new[] { SKPathDirection.Clockwise, SKPathDirection.CounterClockwise })
        {
            for (uint startIndex = 0; startIndex < 8; startIndex++)
            {
                using var path = new SKPath();
                path.AddRoundRect(roundRect, direction, startIndex);
                Assert.Equal(boundary[startIndex], path[0]);
                var finalEdgeIsArc = direction == SKPathDirection.Clockwise
                    ? (startIndex & 1u) == 0
                    : (startIndex & 1u) != 0;
                Assert.Equal(finalEdgeIsArc ? 10 : 9, path.VerbCount);

                using var iterator = path.CreateRawIterator();
                var points = new SKPoint[4];
                Assert.Equal(SKPathVerb.Move, iterator.Next(points));
                var firstEdgeIsArc = direction == SKPathDirection.Clockwise
                    ? (startIndex & 1u) != 0
                    : (startIndex & 1u) == 0;
                Assert.Equal(
                    firstEdgeIsArc ? SKPathVerb.Conic : SKPathVerb.Line,
                    iterator.Next(points));
            }
        }
    }

    [Fact]
    public void IndexedShapesRejectOutOfRangeStarts()
    {
        using var path = new SKPath();
        using var roundRect = new SKRoundRect(new SKRect(0f, 0f, 10f, 10f), 1f, 1f);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            path.AddRect(new SKRect(0f, 0f, 10f, 10f), SKPathDirection.Clockwise, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            path.AddRoundRect(roundRect, SKPathDirection.Clockwise, 8));
    }

    [Fact]
    public void AddPathReverseReversesContourAndCurveOrder()
    {
        using var source = new SKPath();
        source.MoveTo(1f, 2f);
        source.LineTo(3f, 4f);
        source.QuadTo(5f, 6f, 7f, 8f);
        source.ConicTo(9f, 10f, 11f, 12f, 0.5f);
        source.CubicTo(13f, 14f, 15f, 16f, 17f, 18f);
        source.Close();
        source.MoveTo(20f, 21f);
        source.LineTo(22f, 23f);
        using var target = new SKPath();

        target.AddPathReverse(source);

        using var iterator = target.CreateRawIterator();
        var points = new SKPoint[4];
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(new SKPoint(22f, 23f), points[0]);
        Assert.Equal(SKPathVerb.Line, iterator.Next(points));
        Assert.Equal(new SKPoint(20f, 21f), points[1]);
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(new SKPoint(17f, 18f), points[0]);
        Assert.Equal(SKPathVerb.Cubic, iterator.Next(points));
        Assert.Equal(new SKPoint(15f, 16f), points[1]);
        Assert.Equal(new SKPoint(13f, 14f), points[2]);
        Assert.Equal(new SKPoint(11f, 12f), points[3]);
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        Assert.Equal(new SKPoint(9f, 10f), points[1]);
        Assert.Equal(new SKPoint(7f, 8f), points[2]);
        Assert.Equal(0.5f, iterator.ConicWeight());
        Assert.Equal(SKPathVerb.Quad, iterator.Next(points));
        Assert.Equal(new SKPoint(5f, 6f), points[1]);
        Assert.Equal(new SKPoint(3f, 4f), points[2]);
        Assert.Equal(SKPathVerb.Line, iterator.Next(points));
        Assert.Equal(new SKPoint(1f, 2f), points[1]);
        Assert.Equal(SKPathVerb.Close, iterator.Next(points));
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));

        Assert.Equal(new SKPoint(1f, 2f), source[0]);
        Assert.Equal(SKPathSegmentMask.Line |
                     SKPathSegmentMask.Quad |
                     SKPathSegmentMask.Conic |
                     SKPathSegmentMask.Cubic,
            target.SegmentMasks);
    }

    private static void AssertNear(SKPoint expected, SKPoint actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 0.00001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 0.00001f);
    }
}
