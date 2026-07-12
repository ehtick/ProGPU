using ProGPU.Vector;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPathTopologyCompatibilityTests
{
    [Fact]
    public void RelativeCommandsAfterClosePreserveNativePointAndVerbTopology()
    {
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.MoveTo(1, 2);
        path.LineTo(3, 4);
        path.Close();
        path.RLineTo(9, 10);

        Assert.Equal(5, path.VerbCount);
        Assert.Equal(4, path.PointCount);
        Assert.Equal(SKPathSegmentMask.Line, path.SegmentMasks);
        Assert.Equal(SKPathConvexity.Concave, path.Convexity);
        AssertPoint(path.LastPoint, 10, 12);
        AssertPoints(
            path.Points,
            new SKPoint(1, 2),
            new SKPoint(3, 4),
            new SKPoint(1, 2),
            new SKPoint(10, 12));
        Assert.Equal("M1 2L3 4L1 2ZM1 2L10 12", path.ToSvgPathData());

        path.Rewind();

        Assert.True(path.IsEmpty);
        Assert.Equal(SKPathFillType.Winding, path.FillType);
    }

    [Fact]
    public void IndexedRectAndRoundRectExposeNativePrimitiveQueries()
    {
        using var path = new SKPath();
        path.AddRect(new SKRect(1, 2, 5, 8), SKPathDirection.Clockwise, 1);

        Assert.True(path.IsRect);
        Assert.False(path.IsOval);
        Assert.Equal(5, path.VerbCount);
        Assert.Equal(4, path.PointCount);
        Assert.Equal(3, path.Geometry.Figures[0].Segments.Count);
        AssertPoint(path.LastPoint, 1, 2);
        Assert.Equal(new SKRect(1, 2, 5, 8), path.GetRect(out var isClosed, out var direction));
        Assert.True(isClosed);
        Assert.Equal(SKPathDirection.Clockwise, direction);

        path.Reset();
        using var roundRect = new SKRoundRect(new SKRect(0, 0, 20, 10), 2, 3);
        path.AddRoundRect(roundRect, SKPathDirection.Clockwise);

        Assert.True(path.IsRoundRect);
        Assert.False(path.IsRect);
        Assert.Equal(10, path.VerbCount);
        Assert.Equal(13, path.PointCount);
        Assert.Equal(SKPathSegmentMask.Line | SKPathSegmentMask.Conic, path.SegmentMasks);
        AssertPoint(path.Points[0], 0, 7);
        using var queried = path.GetRoundRect();
        Assert.Equal(new SKRect(0, 0, 20, 10), queried.Rect);
        Assert.All(queried.Radii, radius => AssertPoint(radius, 2, 3));
    }

    [Fact]
    public void RawIteratorEnumeratesOvalAsFourWeightedConics()
    {
        using var path = new SKPath();
        path.AddOval(new SKRect(0, 0, 20, 10), SKPathDirection.CounterClockwise);

        Assert.True(path.IsOval);
        Assert.Equal(6, path.VerbCount);
        Assert.Equal(9, path.PointCount);
        Assert.Equal(SKPathSegmentMask.Conic, path.SegmentMasks);
        Assert.Equal(new SKRect(0, 0, 20, 10), path.GetOvalBounds());

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        Assert.Equal(SKPathVerb.Move, iterator.Peek());
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        AssertPoint(points[0], 20, 5);

        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(SKPathVerb.Conic, iterator.Peek());
            Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
            Assert.InRange(iterator.ConicWeight(), 0.7071067f, 0.7071069f);
        }

        Array.Fill(points, new SKPoint(99, 99));
        Assert.Equal(SKPathVerb.Close, iterator.Next(points));
        Assert.All(points, point => AssertPoint(point, 99, 99));
        Assert.Equal(SKPathVerb.Done, iterator.Next(points));
    }

    [Fact]
    public void IteratorSynthesizesCloseLineOnlyWhenRequested()
    {
        using var path = new SKPath();
        path.MoveTo(1, 2);
        path.LineTo(3, 4);
        var points = new SKPoint[4];

        using (var iterator = path.CreateIterator(forceClose: false))
        {
            Assert.Equal(SKPathVerb.Move, iterator.Next(points));
            Assert.Equal(SKPathVerb.Line, iterator.Next(points));
            Assert.False(iterator.IsCloseLine());
            Assert.Equal(SKPathVerb.Done, iterator.Next(points));
        }

        using (var iterator = path.CreateIterator(forceClose: true))
        {
            Assert.Equal(SKPathVerb.Move, iterator.Next(points));
            Assert.Equal(SKPathVerb.Line, iterator.Next(points));
            Assert.Equal(SKPathVerb.Line, iterator.Next(points));
            Assert.True(iterator.IsCloseLine());
            AssertPoint(points[0], 3, 4);
            AssertPoint(points[1], 1, 2);
            Assert.Equal(SKPathVerb.Close, iterator.Next(points));
            Assert.True(iterator.IsCloseLine());
            Assert.Equal(SKPathVerb.Done, iterator.Next(points));
        }

        path.Reset();
        path.MoveTo(5, 6);
        using var moveOnly = path.CreateIterator(forceClose: true);
        Assert.Equal(SKPathVerb.Done, moveOnly.Next(points));
    }

    [Fact]
    public void RationalConicsRetainNativeVerbsAndSubdivisionResults()
    {
        using var path = new SKPath();
        path.MoveTo(0, 0);
        path.ConicTo(10, 20, 30, 40, 0.5f);

        Assert.Equal(2, path.VerbCount);
        Assert.Equal(3, path.PointCount);
        Assert.Equal(SKPathSegmentMask.Conic, path.SegmentMasks);
        AssertPoints(path.Points, new SKPoint(0, 0), new SKPoint(10, 20), new SKPoint(30, 40));
        Assert.Equal(16, path.Geometry.Figures[0].Segments.Count);

        using (var iterator = path.CreateRawIterator())
        {
            var points = new SKPoint[4];
            Assert.Equal(SKPathVerb.Move, iterator.Next(points));
            Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
            Assert.Equal(0.5f, iterator.ConicWeight());
            AssertPoint(points[0], 0, 0);
            AssertPoint(points[1], 10, 20);
            AssertPoint(points[2], 30, 40);
        }

        using var reversed = new SKPath();
        reversed.AddPathReverse(path);
        Assert.Equal(SKPathSegmentMask.Conic, reversed.SegmentMasks);
        AssertPoints(
            reversed.Points,
            new SKPoint(30, 40),
            new SKPoint(10, 20),
            new SKPoint(0, 0));

        var converted = SKPath.ConvertConicToQuads(
            new SKPoint(0, 0),
            new SKPoint(10, 20),
            new SKPoint(30, 40),
            0.5f,
            2);
        AssertPoints(
            converted,
            new SKPoint(0, 0),
            new SKPoint(1.5470054f, 3.0940108f),
            new SKPoint(5.1196613f, 8.452995f),
            new SKPoint(8.692318f, 13.811979f),
            new SKPoint(13.333334f, 20),
            new SKPoint(17.97435f, 26.188023f),
            new SKPoint(22.44017f, 31.547007f),
            new SKPoint(26.90599f, 36.90599f),
            new SKPoint(30, 40));
    }

    [Fact]
    public void AnalyticArcEnumerationMatchesNativeQuarterConics()
    {
        using var path = new SKPath();
        path.AddArc(new SKRect(0, 0, 20, 10), 30, 200);

        Assert.Equal(4, path.VerbCount);
        Assert.Equal(7, path.PointCount);
        Assert.Equal(SKPathSegmentMask.Conic, path.SegmentMasks);
        Assert.Equal(SKPathConvexity.Concave, path.Convexity);

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        AssertPoint(points[0], 18.660254f, 7.5f, 0.0001f);
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        AssertPoint(points[1], 13.660254f, 11.830127f, 0.0002f);
        AssertPoint(points[2], 5, 9.330127f, 0.0002f);
        Assert.InRange(iterator.ConicWeight(), 0.7071067f, 0.7071069f);
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        Assert.InRange(iterator.ConicWeight(), 0.9848076f, 0.9848078f);
        AssertPoint(points[2], 3.572125f, 1.1697774f, 0.0002f);
    }

    [Fact]
    public void DestinationTransformsPreserveConicMetadataAndSourceOwnership()
    {
        using var source = new SKPath();
        source.MoveTo(0, 0);
        source.ConicTo(10, 20, 30, 40, 0.5f);
        using var destination = new SKPath();
        var matrix = SKMatrix.CreateTranslation(100, 200);

        source.Transform(in matrix, destination);

        AssertPoints(source.Points, new SKPoint(0, 0), new SKPoint(10, 20), new SKPoint(30, 40));
        AssertPoints(
            destination.Points,
            new SKPoint(100, 200),
            new SKPoint(110, 220),
            new SKPoint(130, 240));
        Assert.Equal(SKPathSegmentMask.Conic, destination.SegmentMasks);

        destination.Offset(5, 6);
        AssertPoints(
            destination.Points,
            new SKPoint(105, 206),
            new SKPoint(115, 226),
            new SKPoint(135, 246));
    }

    [Fact]
    public void ExtendAfterClosedContourStartsAtClosedOriginAndMergesFirstSourceContour()
    {
        using var source = SKPath.ParseSvgPathData("M0 0 L10 0 L10 10 Z M20 20 L30 20");
        using var destination = new SKPath();
        destination.MoveTo(-1, -2);
        destination.Close();

        destination.AddPath(source, SKPathAddMode.Extend);

        Assert.Equal(3, destination.Geometry.Figures.Count);
        Assert.Equal(9, destination.VerbCount);
        Assert.Equal(7, destination.PointCount);
        Assert.Equal(
            "M-1 -2ZM-1 -2L0 0L10 0L10 10L-1 -2ZM20 20L30 20",
            destination.ToSvgPathData());
    }

    [Fact]
    public void SimplifyWindingAndOpBuilderReturnIndependentPathsWithoutGpuInitialization()
    {
        using var source = new SKPath { FillType = SKPathFillType.EvenOdd };
        source.AddRect(new SKRect(0, 0, 10, 10));
        using var simplified = source.Simplify();
        using var winding = source.ToWinding();
        using var builder = new SKPath.OpBuilder();
        using var resolved = new SKPath();

        builder.Add(source, SKPathOp.Union);

        Assert.True(builder.Resolve(resolved));
        Assert.Equal(SKPathFillType.EvenOdd, simplified.FillType);
        Assert.Equal(SKPathFillType.Winding, winding.FillType);
        Assert.Equal(source.Points, resolved.Points);
        resolved.Offset(10, 20);
        Assert.NotEqual(source.Bounds, resolved.Bounds);
    }

    private static void AssertPoints(SKPoint[] actual, params SKPoint[] expected)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            AssertPoint(actual[index], expected[index].X, expected[index].Y, 0.0002f);
        }
    }

    private static void AssertPoint(SKPoint actual, float x, float y, float tolerance = 0.00001f)
    {
        Assert.InRange(actual.X, x - tolerance, x + tolerance);
        Assert.InRange(actual.Y, y - tolerance, y + tolerance);
    }
}
