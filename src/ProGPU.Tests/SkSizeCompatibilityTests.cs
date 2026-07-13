using System;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkSizeCompatibilityTests
{
    [Fact]
    public void PropertiesConstructorsAndEmptyStateMatchNative()
    {
        var size = new SKSize(new SKPoint(3.5f, 4.25f));
        Assert.Equal(3.5f, size.Width);
        Assert.Equal(4.25f, size.Height);
        Assert.False(size.IsEmpty);
        Assert.True(SKSize.Empty.IsEmpty);

        size.Width = 7f;
        size.Height = 8f;
        Assert.Equal(new SKSize(7f, 8f), size);

        var integer = new SKSizeI(new SKPointI(3, 4));
        Assert.Equal(new SKSizeI(3, 4), integer);
        Assert.False(integer.IsEmpty);
        Assert.True(SKSizeI.Empty.IsEmpty);
    }

    [Fact]
    public void PointConversionsMatchNativeDirectionality()
    {
        var size = new SKSize(3.5f, -2.25f);
        Assert.Equal(new SKPoint(3.5f, -2.25f), size.ToPoint());
        Assert.Equal(new SKPoint(3.5f, -2.25f), (SKPoint)size);

        var integer = new SKSizeI(3, -2);
        Assert.Equal(new SKPointI(3, -2), integer.ToPointI());
        Assert.Equal(new SKPointI(3, -2), (SKPointI)integer);

        SKSize widened = integer;
        Assert.Equal(new SKSize(3f, -2f), widened);
    }

    [Fact]
    public void FloatToIntegerConversionTruncatesInCheckedContext()
    {
        Assert.Equal(new SKSizeI(3, -2), new SKSize(3.9f, -2.9f).ToSizeI());
        Assert.Throws<OverflowException>(() => new SKSize(float.MaxValue, 1f).ToSizeI());
        Assert.Throws<OverflowException>(() => new SKSize(float.NaN, 1f).ToSizeI());
    }

    [Fact]
    public void ArithmeticMethodsAndOperatorsMatchNative()
    {
        var first = new SKSize(10f, 20f);
        var second = new SKSize(3f, 4f);
        Assert.Equal(new SKSize(13f, 24f), SKSize.Add(first, second));
        Assert.Equal(new SKSize(7f, 16f), SKSize.Subtract(first, second));
        Assert.Equal(new SKSize(13f, 24f), first + second);
        Assert.Equal(new SKSize(7f, 16f), first - second);

        var firstI = new SKSizeI(10, 20);
        var secondI = new SKSizeI(3, 4);
        Assert.Equal(new SKSizeI(13, 24), SKSizeI.Add(firstI, secondI));
        Assert.Equal(new SKSizeI(7, 16), SKSizeI.Subtract(firstI, secondI));
        Assert.Equal(new SKSizeI(13, 24), firstI + secondI);
        Assert.Equal(new SKSizeI(7, 16), firstI - secondI);
    }

    [Fact]
    public void EqualityAndHashCodesUseBothDimensions()
    {
        var size = new SKSize(2f, 3f);
        Assert.True(size == new SKSize(2f, 3f));
        Assert.False(size != new SKSize(2f, 3f));
        Assert.NotEqual(size, new SKSize(3f, 2f));
        Assert.Equal(size.GetHashCode(), new SKSize(2f, 3f).GetHashCode());

        var integer = new SKSizeI(2, 3);
        Assert.True(integer == new SKSizeI(2, 3));
        Assert.False(integer != new SKSizeI(2, 3));
        Assert.NotEqual(integer, new SKSizeI(3, 2));
        Assert.Equal(integer.GetHashCode(), new SKSizeI(2, 3).GetHashCode());
    }

    [Fact]
    public void FormattingMatchesNativeValueContract()
    {
        Assert.Equal("{Width=2.5, Height=3.25}", new SKSize(2.5f, 3.25f).ToString());
        Assert.Equal("{Width=2, Height=3}", new SKSizeI(2, 3).ToString());
    }
}
