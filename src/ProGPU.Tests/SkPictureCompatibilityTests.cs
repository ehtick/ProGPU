using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkPictureCompatibilityTests
{
    [Fact]
    public void RecorderOverloadTracksCanvasAndEmptyCullState()
    {
        using var recorder = new SKPictureRecorder();
        Assert.IsAssignableFrom<SKObject>(recorder);
        Assert.Null(recorder.RecordingCanvas);

        var canvas = recorder.BeginRecording(new SKRect(1f, 2f, 31f, 42f), useRTree: true);
        Assert.Same(canvas, recorder.RecordingCanvas);
        using var empty = recorder.EndRecording();

        Assert.Null(recorder.RecordingCanvas);
        Assert.Equal(SKRect.Empty, empty.CullRect);
        Assert.Equal(0, empty.ApproximateOperationCount);
        Assert.Equal(0, empty.GetApproximateOperationCount(includeNested: true));
        Assert.True(empty.UniqueId > 0);
        Assert.True(empty.ApproximateBytesUsed >= 24);
    }

    [Fact]
    public void PictureMetadataCountsNestedCommandsAndAssignsUniqueIds()
    {
        using var child = Record(new SKRect(0f, 0f, 100f, 80f), canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(5f, 6f, 20f, 30f), paint);
            canvas.DrawCircle(30f, 20f, 5f, paint);
        });
        using var parent = Record(new SKRect(0f, 0f, 200f, 160f), canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawPicture(child);
            canvas.DrawLine(0f, 0f, 10f, 10f, paint);
        });

        Assert.NotEqual(0u, child.UniqueId);
        Assert.NotEqual(0u, parent.UniqueId);
        Assert.NotEqual(child.UniqueId, parent.UniqueId);
        Assert.Equal(child.Picture.Commands.Length, child.ApproximateOperationCount);
        Assert.Equal(parent.Picture.Commands.Length, parent.ApproximateOperationCount);
        Assert.Equal(
            child.GetApproximateOperationCount(includeNested: true) + 1,
            parent.GetApproximateOperationCount(includeNested: true));
        Assert.True(parent.ApproximateBytesUsed > child.ApproximateBytesUsed);
    }

    [Fact]
    public void PictureShaderOverloadsRetainTileFilterAndMatrixState()
    {
        var cull = new SKRect(3f, 4f, 43f, 54f);
        using var picture = Record(cull, canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Green };
            canvas.DrawRect(new SKRect(4f, 5f, 10f, 12f), paint);
        });
        using var defaults = picture.ToShader();

        Assert.NotNull(defaults.Picture);
        Assert.Equal(SKShaderTileMode.Clamp, defaults.Picture.TileModeX);
        Assert.Equal(SKShaderTileMode.Clamp, defaults.Picture.TileModeY);
        Assert.Equal(SKFilterMode.Nearest, defaults.Picture.FilterMode);
        Assert.Equal(SKMatrix.Identity, defaults.Picture.LocalMatrix);
        Assert.Equal(cull, defaults.Picture.TileRect);

        var matrix = SKMatrix.CreateTranslation(7f, 9f);
        var tile = new SKRect(1f, 2f, 11f, 22f);
        using var configured = picture.ToShader(
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Mirror,
            SKFilterMode.Linear,
            matrix,
            tile);

        Assert.NotNull(configured.Picture);
        Assert.Equal(SKShaderTileMode.Repeat, configured.Picture.TileModeX);
        Assert.Equal(SKShaderTileMode.Mirror, configured.Picture.TileModeY);
        Assert.Equal(SKFilterMode.Linear, configured.Picture.FilterMode);
        Assert.Equal(matrix, configured.Picture.LocalMatrix);
        Assert.Equal(tile, configured.Picture.TileRect);
    }

    [Fact]
    public void DrawableRecordingRetainsBoundsAndProducesIndependentSnapshots()
    {
        var cull = new SKRect(7f, 8f, 57f, 68f);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(cull, useRTree: false);
        using (var paint = new SKPaint { Color = SKColors.Magenta })
        {
            canvas.DrawRect(new SKRect(8f, 9f, 12f, 13f), paint);
        }

        using var drawable = recorder.EndRecordingAsDrawable();
        using var snapshot = drawable.Snapshot();
        using var playback = Record(cull, target => drawable.Draw(target, 0f, 0f));

        Assert.Equal(cull, drawable.Bounds);
        Assert.True(drawable.ApproximateBytesUsed > 0);
        Assert.Equal(cull, snapshot.CullRect);
        Assert.Equal(1, snapshot.ApproximateOperationCount);
        Assert.Equal(RenderCommandType.DrawPicture, Assert.Single(playback.Picture.Commands).Type);
        Assert.NotEqual(snapshot.UniqueId, playback.UniqueId);
    }

    private static SKPicture Record(SKRect cullRect, Action<SKCanvas> draw)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(cullRect);
        draw(canvas);
        return recorder.EndRecording();
    }
}
