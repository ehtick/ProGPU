using System.Numerics;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasShapeOverloadCompatibilityTests
{
    [Fact]
    public void PointLineOverloadDelegatesToOneRetainedPath()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 20f, 20f));
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke };

        canvas.DrawLine(new SKPoint(2f, 3f), new SKPoint(11f, 13f), paint);
        using var picture = recorder.EndRecording();

        var command = Assert.Single(picture.Picture.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.True(command.Path!.TryGetBounds(out var min, out var max));
        Assert.Equal(new Vector2(2f, 3f), min);
        Assert.Equal(new Vector2(11f, 13f), max);
    }

    [Fact]
    public void RoundRectOverloadsPreserveBoundsAndRadii()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 30f, 30f));
        using var paint = new SKPaint();

        canvas.DrawRoundRect(2f, 3f, 10f, 12f, 4f, 5f, paint);
        canvas.DrawRoundRect(new SKRect(4f, 6f, 20f, 24f), new SKSize(7f, 8f), paint);
        using var picture = recorder.EndRecording();

        Assert.Collection(
            picture.Picture.Commands,
            command => AssertRoundRect(command, new Rect(2f, 3f, 10f, 12f), 4f, 5f),
            command => AssertRoundRect(command, new Rect(4f, 6f, 16f, 18f), 7f, 8f));
    }

    [Fact]
    public void OvalAndCircleOverloadsPreserveCentersAndRadii()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 30f, 30f));
        using var paint = new SKPaint();

        canvas.DrawOval(12f, 14f, 5f, 7f, paint);
        canvas.DrawOval(new SKPoint(9f, 11f), new SKSize(3f, 4f), paint);
        canvas.DrawCircle(new SKPoint(6f, 8f), 2.5f, paint);
        using var picture = recorder.EndRecording();

        Assert.Collection(
            picture.Picture.Commands,
            command => AssertEllipse(command, new Vector2(12f, 14f), 5f, 7f),
            command => AssertEllipse(command, new Vector2(9f, 11f), 3f, 4f),
            command =>
            {
                Assert.Equal(RenderCommandType.DrawCircle, command.Type);
                Assert.Equal(new Vector2(6f, 8f), command.Position2);
                Assert.Equal(2.5f, command.RadiusX);
            });
    }

    private static void AssertRoundRect(RenderCommand command, Rect rect, float radiusX, float radiusY)
    {
        Assert.Equal(RenderCommandType.DrawRoundedRect, command.Type);
        Assert.Equal(rect.X, command.Rect.X);
        Assert.Equal(rect.Y, command.Rect.Y);
        Assert.Equal(rect.Width, command.Rect.Width);
        Assert.Equal(rect.Height, command.Rect.Height);
        Assert.Equal(radiusX, command.RadiusX);
        Assert.Equal(radiusY, command.RadiusY);
    }

    private static void AssertEllipse(RenderCommand command, Vector2 center, float radiusX, float radiusY)
    {
        Assert.Equal(RenderCommandType.DrawEllipse, command.Type);
        Assert.Equal(center, command.Position2);
        Assert.Equal(radiusX, command.RadiusX);
        Assert.Equal(radiusY, command.RadiusY);
    }
}
