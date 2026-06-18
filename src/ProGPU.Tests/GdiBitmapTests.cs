using System;
using Xunit;
using DrawingBitmap = System.Drawing.Bitmap;

namespace ProGPU.Tests;

public sealed class GdiBitmapTests
{
    [Theory]
    [InlineData(0, 1, "width")]
    [InlineData(-1, 1, "width")]
    [InlineData(1, 0, "height")]
    [InlineData(1, -1, "height")]
    public void BitmapConstructorRejectsNonPositiveDimensions(int width, int height, string parameterName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DrawingBitmap(width, height));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
