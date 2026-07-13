using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasAnnotationCompatibilityTests
{
    [Fact]
    public void DataAnnotationOverloadsAreRasterNoOps()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        using var data = SKData.CreateCopy(new byte[] { 1, 2, 3 });

        canvas.DrawAnnotation(new SKRect(1f, 2f, 3f, 4f), "key", data);
        canvas.DrawAnnotation(new SKRect(1f, 2f, 3f, 4f), null!, null);
        canvas.DrawUrlAnnotation(new SKRect(1f, 2f, 3f, 4f), data);
        canvas.DrawUrlAnnotation(new SKRect(1f, 2f, 3f, 4f), (SKData?)null);
        canvas.DrawNamedDestinationAnnotation(new SKPoint(5f, 6f), data);
        canvas.DrawNamedDestinationAnnotation(new SKPoint(5f, 6f), (SKData?)null);
        canvas.DrawLinkDestinationAnnotation(new SKRect(1f, 2f, 3f, 4f), data);
        canvas.DrawLinkDestinationAnnotation(new SKRect(1f, 2f, 3f, 4f), (SKData?)null);

        Assert.Empty(context.Commands);
    }

    [Theory]
    [InlineData(null, "00")]
    [InlineData("", "00")]
    [InlineData("destination", "64657374696E6174696F6E00")]
    [InlineData("A\u00E9", "413F00")]
    public void StringAnnotationsReturnOwnedNullTerminatedAsciiData(
        string? value,
        string expectedHex)
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);

        using var url = canvas.DrawUrlAnnotation(SKRect.Empty, value!);
        using var named = canvas.DrawNamedDestinationAnnotation(SKPoint.Empty, value!);
        using var link = canvas.DrawLinkDestinationAnnotation(SKRect.Empty, value!);

        Assert.Equal(expectedHex, Convert.ToHexString(url.ToArray()));
        Assert.Equal(expectedHex, Convert.ToHexString(named.ToArray()));
        Assert.Equal(expectedHex, Convert.ToHexString(link.ToArray()));
        Assert.NotSame(url, named);
        Assert.NotSame(named, link);
        Assert.Empty(context.Commands);
    }
}
