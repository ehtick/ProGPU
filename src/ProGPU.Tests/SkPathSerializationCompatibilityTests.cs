using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPathSerializationCompatibilityTests
{
    private static readonly SKPoint P0 = new(1f, 2f);
    private static readonly SKPoint P1 = new(3f, 8f);
    private static readonly SKPoint P2 = new(9f, 10f);

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 9)]
    [InlineData(3, 17)]
    public void ConicConversionUsesNativePowerOfTwoInventory(int pow2, int pointCount)
    {
        var points = SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, pow2);

        Assert.Equal(pointCount, points.Length);
        Assert.Equal(P0, points[0]);
        Assert.Equal(P2, points[^1]);
    }

    [Fact]
    public void ConicConversionMatchesNativeHomogeneousSubdivision()
    {
        var points = SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, 2);
        var expected = new[]
        {
            new SKPoint(1f, 2f),
            new SKPoint(1.309401f, 2.928203f),
            new SKPoint(2.2025652f, 4.1786327f),
            new SKPoint(3.0957294f, 5.429063f),
            new SKPoint(4.3333335f, 6.666667f),
            new SKPoint(5.570938f, 7.9042716f),
            new SKPoint(6.8213673f, 8.797436f),
            new SKPoint(8.071796f, 9.690599f),
            new SKPoint(9f, 10f),
        };

        Assert.Equal(expected.Length, points.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.InRange(MathF.Abs(expected[index].X - points[index].X), 0f, 0.000002f);
            Assert.InRange(MathF.Abs(expected[index].Y - points[index].Y), 0f, 0.000002f);
        }
    }

    [Fact]
    public void ConicConversionOverloadsReturnSegmentCountAndPreserveTail()
    {
        var destination = Enumerable.Repeat(new SKPoint(-99f, -99f), 12).ToArray();
        var segmentCount = SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, destination, 2);

        Assert.Equal(4, segmentCount);
        Assert.Equal(P0, destination[0]);
        Assert.Equal(P2, destination[8]);
        Assert.Equal(new SKPoint(-99f, -99f), destination[9]);

        var allocatedCount = SKPath.ConvertConicToQuads(
            P0,
            P1,
            P2,
            0.5f,
            out var allocated,
            2);
        Assert.Equal(4, allocatedCount);
        Assert.Equal(9, allocated.Length);
        Assert.Equal(destination.AsSpan(0, 9).ToArray(), allocated);
    }

    [Fact]
    public void ConicConversionRejectsUnsafeManagedBufferRequests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, 21));
        Assert.Throws<ArgumentNullException>(() =>
            SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, null!, 1));
        Assert.Throws<ArgumentException>(() =>
            SKPath.ConvertConicToQuads(P0, P1, P2, 0.5f, new SKPoint[4], 1));
    }

    [Fact]
    public void SvgPathDataMatchesNativeCookedIteratorFormatting()
    {
        using var path = new SKPath();
        path.MoveTo(1.25f, 2.5f);
        path.LineTo(3f, 4f);
        path.QuadTo(5f, 6f, 7f, 8f);
        path.ConicTo(9f, 10f, 11f, 12f, 0.5f);
        path.CubicTo(13f, 14f, 15f, 16f, 17f, 18f);
        path.Close();

        Assert.Equal(
            "M1.25 2.5L3 4Q5 6 7 8Q9 10 11 12C13 14 15 16 17 18L1.25 2.5Z",
            path.ToSvgPathData());
    }

    [Fact]
    public void EmptySvgPathDataIsEmpty()
    {
        using var path = new SKPath();

        Assert.Equal(string.Empty, path.ToSvgPathData());
    }
}
