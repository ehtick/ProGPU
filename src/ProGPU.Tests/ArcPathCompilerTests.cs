using System;
using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public class ArcPathCompilerTests
{
    [Fact]
    public void ArcSegmentBoundsIncludeExactExtrema()
    {
        var path = CreatePartialCircleArcPath();
        var figure = path.Figures[0];
        var arc = Assert.IsType<ArcSegment>(figure.Segments[0]);

        Assert.True(ArcSegmentGeometry.TryGetArcBounds(figure.StartPoint, arc, out var min, out var max));

        AssertClose(-3.09017f, min.X);
        AssertClose(0f, min.Y);
        AssertClose(10f, max.X);
        AssertClose(10f, max.Y);

        float sampledMaxY = figure.StartPoint.Y;
        for (int step = 1; step < 8; step++)
        {
            float theta = 0.6f * MathF.PI * step / 8f;
            sampledMaxY = MathF.Max(sampledMaxY, 10f * MathF.Sin(theta));
        }

        Assert.True(sampledMaxY < 9.999f);
    }

    [Fact]
    public void SubArcSegmentPreservesOriginalEllipseAndSweep()
    {
        var start = new Vector2(10f, 0f);
        var arc = new ArcSegment(
            new Vector2(-10f, 0f),
            new Vector2(10f, 10f),
            rotationAngle: 0f,
            isLargeArc: false,
            SweepDirection.Clockwise);

        Assert.True(ArcSegmentGeometry.TryCreateSubArcSegment(
            start,
            arc,
            0.5f,
            1.0f,
            out var subStart,
            out var subArc));

        AssertClose(0f, subStart.X);
        AssertClose(10f, subStart.Y);
        AssertClose(-10f, subArc.Point.X);
        AssertClose(0f, subArc.Point.Y);
        AssertClose(10f, subArc.Size.X);
        AssertClose(10f, subArc.Size.Y);
        Assert.False(subArc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, subArc.SweepDirection);
    }

    [Fact]
    public void SubArcSegmentSetsLargeArcForLongSpans()
    {
        var start = new Vector2(10f, 0f);
        var arc = new ArcSegment(
            new Vector2(0f, 10f),
            new Vector2(10f, 10f),
            rotationAngle: 0f,
            isLargeArc: true,
            SweepDirection.Clockwise);

        Assert.True(ArcSegmentGeometry.TryCreateSubArcSegment(
            start,
            arc,
            0.0f,
            0.75f,
            out var subStart,
            out var subArc));

        AssertClose(start, subStart);
        Assert.True(subArc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, subArc.SweepDirection);
    }

    [Fact]
    public void PathAtlasCompilerPreservesNativeArcSegmentAndExactBounds()
    {
        var (records, segments) = PathAtlas.CompilePath(
            CreatePartialCircleArcPath(),
            out float minX,
            out float minY,
            out float maxX,
            out float maxY);

        var record = Assert.Single(records);
        var segment = Assert.Single(segments);

        Assert.Equal(0u, record.StartSegment);
        Assert.Equal(1u, record.SegmentCount);
        Assert.Equal(3u, segment.SegmentType);
        AssertClose(0f, segment.P2.X);
        AssertClose(0f, segment.P2.Y);
        AssertClose(10f, segment.P3.X);
        AssertClose(10f, segment.P3.Y);
        AssertClose(-3.09017f, minX);
        AssertClose(0f, minY);
        AssertClose(10f, maxX);
        AssertClose(10f, maxY);
        AssertClose(minX, record.MinX);
        AssertClose(minY, record.MinY);
        AssertClose(maxX, record.MaxX);
        AssertClose(maxY, record.MaxY);
    }

    [Fact]
    public void PathOperationCompilerPreservesNativeArcSegmentAndExactBounds()
    {
        var (records, segments) = PathOpGeometrySolver.CompilePath(
            CreatePartialCircleArcPath(),
            out float minX,
            out float minY,
            out float maxX,
            out float maxY);

        var record = Assert.Single(records);
        var segment = Assert.Single(segments);

        Assert.Equal(0u, record.StartSegment);
        Assert.Equal(1u, record.SegmentCount);
        Assert.Equal(3u, segment.SegmentType);
        AssertClose(0f, segment.P2.X);
        AssertClose(0f, segment.P2.Y);
        AssertClose(10f, segment.P3.X);
        AssertClose(10f, segment.P3.Y);
        AssertClose(-3.09017f, minX);
        AssertClose(0f, minY);
        AssertClose(10f, maxX);
        AssertClose(10f, maxY);
        AssertClose(minX, record.MinX);
        AssertClose(minY, record.MinY);
        AssertClose(maxX, record.MaxX);
        AssertClose(maxY, record.MaxY);
    }

    private static PathGeometry CreatePartialCircleArcPath()
    {
        const float radius = 10f;
        const float endTheta = 0.6f * MathF.PI;

        var path = new PathGeometry();
        var figure = new PathFigure(new Vector2(radius, 0f));
        figure.Segments.Add(new ArcSegment(
            new Vector2(radius * MathF.Cos(endTheta), radius * MathF.Sin(endTheta)),
            new Vector2(radius, radius),
            rotationAngle: 0f,
            isLargeArc: false,
            SweepDirection.Clockwise));
        path.Figures.Add(figure);
        return path;
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.InRange(actual, expected - 0.001f, expected + 0.001f);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        AssertClose(expected.X, actual.X);
        AssertClose(expected.Y, actual.Y);
    }
}
