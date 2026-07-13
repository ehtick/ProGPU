using System.Numerics;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasPictureOverloadCompatibilityTests
{
    [Fact]
    public void OptionalPaintOverloadRecordsOneNestedPicture()
    {
        using var source = CreatePicture();
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 40f, 40f));

        canvas.DrawPicture(source);
        using var picture = recorder.EndRecording();

        var command = Assert.Single(picture.Picture.Commands);
        Assert.Equal(RenderCommandType.DrawPicture, command.Type);
        Assert.NotNull(command.Picture);
        Assert.Equal(Matrix4x4.Identity, command.Transform);
    }

    [Fact]
    public void ScalarPositionOverloadComposesWithCurrentMatrix()
    {
        using var source = CreatePicture();
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 40f, 40f));
        canvas.Scale(2f);

        canvas.DrawPicture(source, 3f, 5f);
        using var picture = recorder.EndRecording();

        var command = Assert.Single(picture.Picture.Commands);
        var expected = SKMatrix.Concat(SKMatrix.CreateScale(2f, 2f), SKMatrix.CreateTranslation(3f, 5f));
        Assert.Equal(expected.ToMatrix4x4(), command.Transform);
    }

    [Fact]
    public void PointPositionOverloadMatchesScalarPosition()
    {
        using var source = CreatePicture();
        using var pointRecorder = new SKPictureRecorder();
        var pointCanvas = pointRecorder.BeginRecording(new SKRect(0f, 0f, 40f, 40f));
        pointCanvas.DrawPicture(source, new SKPoint(7f, 11f));
        using var pointPicture = pointRecorder.EndRecording();

        using var scalarRecorder = new SKPictureRecorder();
        var scalarCanvas = scalarRecorder.BeginRecording(new SKRect(0f, 0f, 40f, 40f));
        scalarCanvas.DrawPicture(source, 7f, 11f);
        using var scalarPicture = scalarRecorder.EndRecording();

        Assert.Equal(
            Assert.Single(scalarPicture.Picture.Commands).Transform,
            Assert.Single(pointPicture.Picture.Commands).Transform);
    }

    [Fact]
    public void MatrixOverloadRestoresCanvasStateAndPreservesPaintBlend()
    {
        using var source = CreatePicture();
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 40f, 40f));
        canvas.Translate(2f, 4f);
        var before = canvas.TotalMatrix;
        var local = SKMatrix.CreateRotationDegrees(30f);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };

        canvas.DrawPicture(source, in local, paint);
        Assert.Equal(before, canvas.TotalMatrix);
        using var picture = recorder.EndRecording();

        Assert.Collection(
            picture.Picture.Commands,
            command => Assert.Equal(RenderCommandType.PushBlendMode, command.Type),
            command =>
            {
                Assert.Equal(RenderCommandType.DrawPicture, command.Type);
                Assert.Equal(SKMatrix.Concat(before, local).ToMatrix4x4(), command.Transform);
            },
            command => Assert.Equal(RenderCommandType.PopBlendMode, command.Type));
    }

    private static SKPicture CreatePicture()
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 10f, 10f));
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(1f, 2f, 5f, 7f), paint);
        return recorder.EndRecording();
    }
}
