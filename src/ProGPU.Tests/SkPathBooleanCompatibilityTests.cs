using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPathBooleanCompatibilityTests
{
    [Fact]
    public void ToWindingOrientsEvenOddHoleOppositeOuterContour()
    {
        using var source = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        source.AddRect(new SKRect(0f, 0f, 100f, 100f), SKPathDirection.Clockwise);
        source.AddRect(new SKRect(20f, 20f, 80f, 80f), SKPathDirection.Clockwise);

        using var winding = source.ToWinding();

        Assert.Equal(SKPathFillType.Winding, winding.FillType);
        Assert.Equal(2, winding.Geometry.Figures.Count);
        Assert.True(winding.Contains(10f, 10f));
        Assert.False(winding.Contains(50f, 50f));
        Assert.True(GetSignedArea(winding.Geometry.Figures[0]) > 0f);
        Assert.True(GetSignedArea(winding.Geometry.Figures[1]) < 0f);
    }

    [Fact]
    public void SimplifySplitsSelfIntersectingBowtieIntoFilledFaces()
    {
        using var source = new SKPath();
        source.MoveTo(0f, 0f);
        source.LineTo(100f, 100f);
        source.LineTo(0f, 100f);
        source.LineTo(100f, 0f);
        source.Close();

        using var simplified = source.Simplify();

        Assert.Equal(SKPathFillType.EvenOdd, simplified.FillType);
        Assert.Equal(2, simplified.Geometry.Figures.Count);
        Assert.True(simplified.Contains(50f, 20f));
        Assert.True(simplified.Contains(50f, 80f));
        Assert.False(simplified.Contains(10f, 50f));
        Assert.Equal(new SKRect(0f, 0f, 100f, 100f), simplified.Bounds);
    }

    [Fact]
    public void SimplifyRemovesOverlappingRectangleInteriorEdges()
    {
        using var source = new SKPath();
        source.AddRect(new SKRect(0f, 0f, 60f, 60f));
        source.AddRect(new SKRect(40f, 0f, 100f, 60f));

        using var simplified = source.Simplify();

        Assert.Equal(SKPathFillType.EvenOdd, simplified.FillType);
        Assert.Single(simplified.Geometry.Figures);
        Assert.Equal(new SKRect(0f, 0f, 100f, 60f), simplified.Bounds);
        Assert.True(simplified.Contains(20f, 30f));
        Assert.True(simplified.Contains(50f, 30f));
        Assert.True(simplified.Contains(80f, 30f));
    }

    [Fact]
    public void BooleanResultOverloadsSupportAliasingAndNullResult()
    {
        using var path = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        path.AddRect(new SKRect(0f, 0f, 100f, 100f));
        path.AddRect(new SKRect(20f, 20f, 80f, 80f));

        Assert.False(path.Simplify(null!));
        Assert.False(path.ToWinding(null!));
        Assert.True(path.ToWinding(path));
        Assert.Equal(SKPathFillType.Winding, path.FillType);
        Assert.False(path.Contains(50f, 50f));
    }

    [Fact]
    public void InstanceOpOverloadUsesExistingSolverAndSupportsAliasedResult()
    {
        using var path = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        path.AddRect(new SKRect(0f, 0f, 10f, 10f));
        using var empty = new SKPath();

        Assert.True(path.Op(empty, SKPathOp.Union, path));
        Assert.Equal(SKPathFillType.EvenOdd, path.FillType);
        Assert.Equal(new SKRect(0f, 0f, 10f, 10f), path.Bounds);
        Assert.False(path.Op(empty, SKPathOp.Union, null!));
    }

    [Fact]
    public void CurveFallbackPreservesExactSegmentsWithoutGpuInitialization()
    {
        using var source = new SKPath
        {
            FillType = SKPathFillType.EvenOdd,
        };
        source.MoveTo(0f, 0f);
        source.ConicTo(5f, 10f, 10f, 0f, 0.5f);
        source.Close();

        using var simplified = source.Simplify();
        using var winding = source.ToWinding();

        Assert.Equal(SKPathSegmentMask.Conic, simplified.SegmentMasks);
        Assert.Equal(SKPathFillType.EvenOdd, simplified.FillType);
        Assert.Equal(SKPathSegmentMask.Conic, winding.SegmentMasks);
        Assert.Equal(SKPathFillType.Winding, winding.FillType);
        using var iterator = winding.CreateRawIterator();
        var points = new SKPoint[4];
        Assert.Equal(SKPathVerb.Move, iterator.Next(points));
        Assert.Equal(SKPathVerb.Conic, iterator.Next(points));
        Assert.Equal(0.5f, iterator.ConicWeight());
    }

    private static float GetSignedArea(ProGPU.Vector.PathFigure figure)
    {
        var area = 0f;
        var previous = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            var point = Assert.IsType<ProGPU.Vector.LineSegment>(segment).Point;
            area += previous.X * point.Y - previous.Y * point.X;
            previous = point;
        }
        area += previous.X * figure.StartPoint.Y - previous.Y * figure.StartPoint.X;
        return area * 0.5f;
    }
}
