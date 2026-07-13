using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasClipBoundsCompatibilityTests
{
    [Fact]
    public void InitialClipMatchesNativeDeviceAndOutsetLocalBounds()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);

        Assert.True(canvas.GetDeviceClipBounds(out var device));
        Assert.Equal(new SKRectI(0, 0, 100, 80), device);
        Assert.Equal(device, canvas.DeviceClipBounds);
        Assert.True(canvas.GetLocalClipBounds(out var local));
        Assert.Equal(new SKRect(-1f, -1f, 101f, 81f), local);
        Assert.Equal(local, canvas.LocalClipBounds);
        Assert.False(canvas.IsClipEmpty);
        Assert.True(canvas.IsClipRect);
    }

    [Fact]
    public void RectangleClipUsesNativeRoundingAndCurrentLocalMatrix()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);

        canvas.ClipRect(new SKRect(10.25f, 20.75f, 50.5f, 60.25f));
        Assert.Equal(new SKRectI(10, 21, 51, 60), canvas.DeviceClipBounds);
        Assert.Equal(new SKRect(9f, 20f, 52f, 61f), canvas.LocalClipBounds);

        canvas.Save();
        canvas.Translate(5f, 7f);
        Assert.Equal(new SKRect(4f, 13f, 47f, 54f), canvas.LocalClipBounds);
        canvas.ClipRect(new SKRect(10f, 10f, 20f, 20f));
        Assert.Equal(new SKRectI(15, 21, 25, 27), canvas.DeviceClipBounds);
        Assert.Equal(new SKRect(9f, 13f, 21f, 21f), canvas.LocalClipBounds);

        canvas.Restore();
        Assert.Equal(new SKRectI(10, 21, 51, 60), canvas.DeviceClipBounds);
        Assert.Equal(new SKRect(9f, 20f, 52f, 61f), canvas.LocalClipBounds);
    }

    [Fact]
    public void DifferenceAndEmptyClipsMatchNativeClassification()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        canvas.ClipRect(new SKRect(10.25f, 20.75f, 50.5f, 60.25f));

        canvas.Save();
        canvas.ClipRect(
            new SKRect(20f, 30f, 30f, 40f),
            SKClipOperation.Difference);
        Assert.Equal(new SKRectI(10, 21, 51, 60), canvas.DeviceClipBounds);
        Assert.False(canvas.IsClipEmpty);
        Assert.False(canvas.IsClipRect);
        canvas.Restore();

        canvas.ClipRect(new SKRect(200f, 200f, 300f, 300f));
        Assert.False(canvas.GetDeviceClipBounds(out var device));
        Assert.Equal(SKRectI.Empty, device);
        Assert.False(canvas.GetLocalClipBounds(out var local));
        Assert.Equal(SKRect.Empty, local);
        Assert.True(canvas.IsClipEmpty);
        Assert.False(canvas.IsClipRect);
    }

    [Fact]
    public void NonRectangularPathUsesOutwardDeviceBounds()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        using var path = new SKPath();
        path.MoveTo(20.25f, 20.75f);
        path.LineTo(40.25f, 20.75f);
        path.LineTo(30.25f, 40.75f);
        path.Close();

        canvas.ClipPath(path);

        Assert.Equal(new SKRectI(20, 20, 41, 41), canvas.DeviceClipBounds);
        Assert.Equal(new SKRect(19f, 19f, 42f, 42f), canvas.LocalClipBounds);
        Assert.False(canvas.IsClipRect);
    }

    [Fact]
    public void LocalBoundsOutsetOneDevicePixelBeforeInverseMapping()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);

        canvas.Scale(2f);
        Assert.Equal(new SKRect(-0.5f, -0.5f, 50.5f, 40.5f), canvas.LocalClipBounds);
        canvas.ClipRect(new SKRect(10.25f, 10.25f, 20.25f, 20.25f));
        Assert.Equal(new SKRectI(21, 21, 41, 41), canvas.DeviceClipBounds);
        Assert.Equal(new SKRect(10f, 10f, 21f, 21f), canvas.LocalClipBounds);
    }

    [Fact]
    public void QuickRejectUsesActiveDeviceClipBounds()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        canvas.ClipRect(new SKRect(10f, 10f, 20f, 20f));

        Assert.False(canvas.QuickReject(new SKRect(15f, 15f, 18f, 18f)));
        Assert.True(canvas.QuickReject(new SKRect(30f, 30f, 40f, 40f)));
    }
}
