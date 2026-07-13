using System;
using System.Numerics;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPointCompatibilityTests
{
    [Fact]
    public void PropertiesLengthsAndOffsetsMatchNative()
    {
        var point = new SKPoint(3f, 4f);
        Assert.Equal(3f, point.X);
        Assert.Equal(4f, point.Y);
        Assert.Equal(5f, point.Length);
        Assert.Equal(25f, point.LengthSquared);
        Assert.False(point.IsEmpty);
        Assert.True(SKPoint.Empty.IsEmpty);

        point.Offset(new SKPoint(2f, -1f));
        point.Offset(-3f, 4f);
        Assert.Equal(new SKPoint(2f, 7f), point);
    }

    [Fact]
    public void NormalizeAndDistanceMatchNativePrecision()
    {
        var normalized = SKPoint.Normalize(new SKPoint(3f, 4f));
        Assert.Equal(0.6f, normalized.X);
        Assert.Equal(0.8f, normalized.Y);
        Assert.Equal(5f, SKPoint.Distance(new SKPoint(3f, 4f), SKPoint.Empty));
        Assert.Equal(25f, SKPoint.DistanceSquared(new SKPoint(3f, 4f), SKPoint.Empty));

        var zero = SKPoint.Normalize(SKPoint.Empty);
        Assert.True(float.IsNaN(zero.X));
        Assert.True(float.IsNaN(zero.Y));
    }

    [Fact]
    public void ReflectPreservesSkiaCompatibilityFormula()
    {
        Assert.Equal(new SKPoint(-9f, 2f), SKPoint.Reflect(new SKPoint(1f, 2f), new SKPoint(1f, 0f)));
        Assert.Equal(new SKPoint(1f, -8f), SKPoint.Reflect(new SKPoint(1f, 2f), new SKPoint(0f, 1f)));
        Assert.Equal(new SKPointI(-9, 2), SKPointI.Reflect(new SKPointI(1, 2), new SKPointI(1, 0)));
    }

    [Fact]
    public void AddSubtractAndOperatorsAcceptAllNativeOffsetTypes()
    {
        var point = new SKPoint(10f, 20f);
        Assert.Equal(new SKPoint(11f, 22f), SKPoint.Add(point, new SKPoint(1f, 2f)));
        Assert.Equal(new SKPoint(13f, 24f), SKPoint.Add(point, new SKPointI(3, 4)));
        Assert.Equal(new SKPoint(15f, 26f), SKPoint.Add(point, new SKSize(5f, 6f)));
        Assert.Equal(new SKPoint(17f, 28f), SKPoint.Add(point, new SKSizeI(7, 8)));
        Assert.Equal(new SKPoint(9f, 18f), SKPoint.Subtract(point, new SKPoint(1f, 2f)));
        Assert.Equal(new SKPoint(7f, 16f), point - new SKPointI(3, 4));
        Assert.Equal(new SKPoint(5f, 14f), point - new SKSize(5f, 6f));
        Assert.Equal(new SKPoint(3f, 12f), point - new SKSizeI(7, 8));
    }

    [Fact]
    public void VectorConversionsRoundTripWithoutAllocation()
    {
        Vector2 vector = new SKPoint(3.5f, -2.25f);
        Assert.Equal(new Vector2(3.5f, -2.25f), vector);

        SKPoint point = vector;
        Assert.Equal(new SKPoint(3.5f, -2.25f), point);
    }

    [Fact]
    public void EqualityHashAndFormattingMatchValueSemantics()
    {
        var point = new SKPoint(2f, 3f);
        Assert.True(point == new SKPoint(2f, 3f));
        Assert.False(point != new SKPoint(2f, 3f));
        Assert.Equal(point.GetHashCode(), new SKPoint(2f, 3f).GetHashCode());
        Assert.Equal("{X=2, Y=3}", point.ToString());
    }
}
