using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkRectICompatibilityTests
{
    [Fact]
    public void LocationSizeMidpointsAndStandardizationMatchNative()
    {
        var rect = new SKRectI(2, 3, 12, 23);
        Assert.Equal(10, rect.Width);
        Assert.Equal(20, rect.Height);
        Assert.Equal(7, rect.MidX);
        Assert.Equal(13, rect.MidY);
        Assert.Equal(new SKPointI(2, 3), rect.Location);
        Assert.Equal(new SKSizeI(10, 20), rect.Size);

        rect.Size = new SKSizeI(5, 6);
        Assert.Equal(new SKRectI(2, 3, 7, 9), rect);
        rect.Location = new SKPointI(10, 20);
        Assert.Equal(new SKRectI(10, 20, 15, 26), rect);

        Assert.Equal(new SKRectI(1, 2, 8, 9), new SKRectI(8, 9, 1, 2).Standardized);
        Assert.Equal(new SKRectI(1, 2, 8, 9), new SKRectI(1, 9, 8, 2).Standardized);
        Assert.True(SKRectI.Empty.IsEmpty);
        Assert.False(new SKRectI(1, 1, 1, 1).IsEmpty);
    }

    [Fact]
    public void AspectFitAndFillPreserveCenterAndFloorEdges()
    {
        var rect = new SKRectI(0, 0, 100, 50);
        Assert.Equal(new SKRectI(25, 0, 75, 50), rect.AspectFit(new SKSizeI(1, 1)));
        Assert.Equal(new SKRectI(0, -25, 100, 75), rect.AspectFill(new SKSizeI(1, 1)));

        var odd = new SKRectI(0, 0, 101, 51);
        Assert.Equal(new SKRectI(5, 0, 95, 51), odd.AspectFit(new SKSizeI(16, 9)));
        Assert.Equal(new SKRectI(0, -3, 101, 53), odd.AspectFill(new SKSizeI(16, 9)));
        Assert.Equal(new SKRectI(50, 25, 50, 25), odd.AspectFit(SKSizeI.Empty));
    }

    [Fact]
    public void RoundingModesHandlePositiveAndReversedExtents()
    {
        var value = new SKRect(1.2f, -2.8f, 5.7f, 8.1f);
        Assert.Equal(new SKRectI(2, -2, 6, 9), SKRectI.Ceiling(value));
        Assert.Equal(new SKRectI(1, -3, 6, 9), SKRectI.Ceiling(value, outwards: true));
        Assert.Equal(new SKRectI(1, -3, 5, 8), SKRectI.Floor(value));
        Assert.Equal(new SKRectI(2, -2, 5, 8), SKRectI.Floor(value, inwards: true));
        Assert.Equal(new SKRectI(1, -2, 5, 8), SKRectI.Truncate(value));

        var reversed = new SKRect(5.7f, 8.1f, 1.2f, -2.8f);
        Assert.Equal(new SKRectI(6, 9, 1, -3), SKRectI.Ceiling(reversed, outwards: true));
        Assert.Equal(new SKRectI(5, 8, 2, -2), SKRectI.Floor(reversed, inwards: true));
        Assert.Equal(new SKRectI(2, 2, -2, -2), SKRectI.Round(new SKRect(1.5f, 2.5f, -1.5f, -2.5f)));
        Assert.Throws<OverflowException>(() => SKRectI.Ceiling(new SKRect(float.MaxValue, 0f, 1f, 1f)));
    }

    [Fact]
    public void ContainmentAndIntersectionUseHalfOpenAndInclusiveContracts()
    {
        var rect = new SKRectI(0, 0, 10, 10);
        Assert.True(rect.Contains(0, 0));
        Assert.True(rect.Contains(new SKPointI(9, 9)));
        Assert.False(rect.Contains(10, 9));
        Assert.False(rect.Contains(9, 10));
        Assert.True(rect.Contains(new SKRectI(2, 3, 8, 9)));

        var touching = new SKRectI(10, 5, 20, 15);
        Assert.False(rect.IntersectsWith(touching));
        Assert.True(rect.IntersectsWithInclusive(touching));
        var boundary = SKRectI.Intersect(rect, touching);
        Assert.Equal(new SKRectI(10, 5, 10, 10), boundary);
        Assert.False(boundary.IsEmpty);
        Assert.Equal(SKRectI.Empty, SKRectI.Intersect(rect, new SKRectI(11, 0, 20, 10)));
    }

    [Fact]
    public void InflateOffsetIntersectAndUnionMutateByValue()
    {
        var original = new SKRectI(10, 20, 30, 40);
        Assert.Equal(new SKRectI(7, 16, 33, 44), SKRectI.Inflate(original, 3, 4));
        Assert.Equal(new SKRectI(10, 20, 30, 40), original);

        original.Inflate(new SKSizeI(2, 5));
        Assert.Equal(new SKRectI(8, 15, 32, 45), original);
        original.Offset(new SKPointI(-3, 7));
        Assert.Equal(new SKRectI(5, 22, 29, 52), original);

        original.Intersect(new SKRectI(0, 30, 20, 60));
        Assert.Equal(new SKRectI(5, 30, 20, 52), original);
        original.Union(new SKRectI(-4, 40, 8, 70));
        Assert.Equal(new SKRectI(-4, 30, 20, 70), original);
        Assert.Equal(new SKRectI(0, 0, 4, 5), SKRectI.Union(SKRectI.Empty, new SKRectI(2, 3, 4, 5)));
    }

    [Fact]
    public void CreationEqualityHashAndFormattingMatchValueSemantics()
    {
        var fromSize = SKRectI.Create(new SKSizeI(4, 5));
        var fromLocation = SKRectI.Create(new SKPointI(2, 3), new SKSizeI(4, 5));
        Assert.Equal(new SKRectI(0, 0, 4, 5), fromSize);
        Assert.Equal(new SKRectI(2, 3, 6, 8), fromLocation);
        Assert.Equal(SKRectI.Create(2, 3, 4, 5), fromLocation);
        Assert.True(fromLocation == new SKRectI(2, 3, 6, 8));
        Assert.False(fromLocation != new SKRectI(2, 3, 6, 8));
        Assert.Equal(fromLocation.GetHashCode(), new SKRectI(2, 3, 6, 8).GetHashCode());
        Assert.Equal("{Left=2,Top=3,Width=4,Height=5}", fromLocation.ToString());
    }
}
