using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkDrawableCompatibilityTests
{
    [Fact]
    public void MetadataDelegatesToVirtualHooks()
    {
        using var drawable = new TestDrawable();

        Assert.Equal(new SKRect(2f, 3f, 22f, 13f), drawable.Bounds);
        Assert.Equal(137, drawable.ApproximateBytesUsed);
    }

    [Fact]
    public void NotifyDrawingChangedAdvancesGeneration()
    {
        using var drawable = new TestDrawable();
        var initial = drawable.GenerationId;

        drawable.NotifyDrawingChanged();

        Assert.Equal(unchecked(initial + 1), drawable.GenerationId);
    }

    [Fact]
    public void DrawComposesMatrixAndRestoresCanvasState()
    {
        using var drawable = new TestDrawable();
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 100f, 100f));
        canvas.Translate(5f, 7f);
        var before = canvas.TotalMatrix;
        var local = SKMatrix.CreateScaleTranslation(2f, 3f, 11f, 13f);

        drawable.Draw(canvas, in local);

        Assert.Equal(SKMatrix.Concat(before, local), drawable.MatrixAtDraw);
        Assert.Equal(before, canvas.TotalMatrix);
    }

    [Fact]
    public void CanvasOverloadsApplyPointAndMatrixTransforms()
    {
        using var drawable = new TestDrawable();
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, 100f, 100f));

        canvas.DrawDrawable(drawable, new SKPoint(7f, 9f));
        Assert.Equal(SKMatrix.CreateTranslation(7f, 9f), drawable.MatrixAtDraw);

        var matrix = SKMatrix.CreateRotationDegrees(30f, 2f, 3f);
        canvas.DrawDrawable(drawable, in matrix);
        Assert.Equal(matrix, drawable.MatrixAtDraw);

        canvas.DrawDrawable(drawable, 4f, 6f);
        Assert.Equal(SKMatrix.CreateTranslation(4f, 6f), drawable.MatrixAtDraw);
    }

    [Fact]
    public void DefaultSnapshotRecordsDrawableCommandsAndBounds()
    {
        using var drawable = new TestDrawable();
        using var picture = drawable.Snapshot();

        Assert.Equal(drawable.Bounds, picture.CullRect);
        var command = Assert.Single(picture.Picture.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(drawable.Bounds, new SKRect(
            command.Rect.X,
            command.Rect.Y,
            command.Rect.Right,
            command.Rect.Bottom));
    }

    [Fact]
    public void OwnedDrawableUsesInheritedLifetime()
    {
        var drawable = new TestDrawable();

        Assert.NotEqual(IntPtr.Zero, drawable.Handle);
        drawable.Dispose();
        Assert.Equal(IntPtr.Zero, drawable.Handle);
    }

    private sealed class TestDrawable : SKDrawable
    {
        public SKMatrix MatrixAtDraw { get; private set; }

        protected internal override void OnDraw(SKCanvas canvas)
        {
            MatrixAtDraw = canvas.TotalMatrix;
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(OnGetBounds(), paint);
        }

        protected internal override int OnGetApproximateBytesUsed() => 137;

        protected internal override SKRect OnGetBounds() => new(2f, 3f, 22f, 13f);
    }
}
