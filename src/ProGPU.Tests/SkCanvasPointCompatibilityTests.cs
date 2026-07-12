using ProGPU.Scene;
using ProGPU.Vector;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasPointCompatibilityTests
{
    [Fact]
    public void PointModeValuesMatchNative()
    {
        Assert.Equal(0, (int)SKPointMode.Points);
        Assert.Equal(1, (int)SKPointMode.Lines);
        Assert.Equal(2, (int)SKPointMode.Polygon);
    }

    [Fact]
    public void DrawPointsValidatesNativeArguments()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 10f, 10f));
        using var paint = new SKPaint();

        Assert.Throws<ArgumentNullException>(() => canvas.DrawPoints(SKPointMode.Points, null!, paint));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawPoints(SKPointMode.Points, [], null!));
    }

    [Fact]
    public void PointModeRecordsOneBatchedStrokeWithoutMutatingPaint()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 20f, 20f));
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Fill,
            StrokeWidth = 4f,
            StrokeCap = SKStrokeCap.Round,
        };

        canvas.DrawPoints(SKPointMode.Points, [new SKPoint(2f, 3f), new SKPoint(7f, 11f)], paint);
        using var picture = recorder.EndRecording();

        Assert.Equal(SKPaintStyle.Fill, paint.Style);
        var command = Assert.Single(picture.Picture.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Path);
        AssertPathBounds(command.Path!, new SKRect(2f, 3f, 7f, 11f));
    }

    [Fact]
    public void LineAndPolygonModesUseNativeSegmentGrouping()
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill };
        using var lineRecorder = new SKPictureRecorder();
        var lineCanvas = lineRecorder.BeginRecording(new SKRect(0f, 0f, 20f, 20f));
        lineCanvas.DrawPoints(
            SKPointMode.Lines,
            [new SKPoint(1f, 2f), new SKPoint(5f, 6f), new SKPoint(19f, 19f)],
            paint);
        using var linePicture = lineRecorder.EndRecording();
        var lineCommand = Assert.Single(linePicture.Picture.Commands);
        AssertPathBounds(lineCommand.Path!, new SKRect(1f, 2f, 5f, 6f));

        using var polygonRecorder = new SKPictureRecorder();
        var polygonCanvas = polygonRecorder.BeginRecording(new SKRect(0f, 0f, 20f, 20f));
        polygonCanvas.DrawPoints(
            SKPointMode.Polygon,
            [new SKPoint(1f, 2f), new SKPoint(5f, 6f), new SKPoint(9f, 3f)],
            paint);
        using var polygonPicture = polygonRecorder.EndRecording();
        var polygonCommand = Assert.Single(polygonPicture.Picture.Commands);
        AssertPathBounds(polygonCommand.Path!, new SKRect(1f, 2f, 9f, 6f));
    }

    [Fact]
    public void ColorPointOverloadUsesSourceBlendMode()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 20f, 20f));

        canvas.DrawPoint(new SKPoint(4f, 7f), SKColors.Blue);
        using var picture = recorder.EndRecording();

        Assert.Collection(
            picture.Picture.Commands,
            command => Assert.Equal(RenderCommandType.PushBlendMode, command.Type),
            command => Assert.Equal(RenderCommandType.DrawPath, command.Type),
            command => Assert.Equal(RenderCommandType.PopBlendMode, command.Type));
    }

    private static void AssertPathBounds(PathGeometry path, SKRect expected)
    {
        Assert.True(path.TryGetBounds(out var min, out var max));
        Assert.Equal(expected, new SKRect(min.X, min.Y, max.X, max.Y));
    }
}
