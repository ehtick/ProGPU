using ProGPU.Scene;
using Silk.NET.WebGPU;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasSaveLayerRecCompatibilityTests
{
    [Fact]
    public void SaveLayerRecordTypesMatchNativeDefaultsAndValues()
    {
        var record = default(SKCanvasSaveLayerRec);

        Assert.Null(record.Bounds);
        Assert.Null(record.Paint);
        Assert.Null(record.Backdrop);
        Assert.Equal(SKCanvasSaveLayerRecFlags.None, record.Flags);
        Assert.Equal(0, (int)SKCanvasSaveLayerRecFlags.None);
        Assert.Equal(2, (int)SKCanvasSaveLayerRecFlags.PreserveLcdText);
        Assert.Equal(4, (int)SKCanvasSaveLayerRecFlags.InitializeWithPrevious);
        Assert.Equal(0x10, (int)SKCanvasSaveLayerRecFlags.F16ColorType);
    }

    [Fact]
    public void DefaultRecordUsesFullBoundsWithoutPreviousSnapshot()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        var record = default(SKCanvasSaveLayerRec);

        var restoreCount = canvas.SaveLayer(in record);

        Assert.Equal(1, restoreCount);
        Assert.Equal(2, canvas.SaveCount);
        Assert.Equal(new SKRect(0f, 0f, 100f, 80f), canvas.CurrentLayerBounds);
        Assert.Null(canvas.CurrentLayerPreviousContext);
        Assert.Null(canvas.CurrentLayerPaint);
        Assert.Null(canvas.CurrentLayerBackdrop);
        Assert.Equal(SKCanvasSaveLayerRecFlags.None, canvas.CurrentLayerFlags);
        Assert.Equal(TextureFormat.Rgba8Unorm, SKCanvas.GetSaveLayerTextureFormat(record.Flags));
    }

    [Fact]
    public void AdvancedRecordSnapshotsPreviousCommandsAndPaintState()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 100f, 80f);
        using var sourcePaint = new SKPaint { Color = SKColors.Red };
        using var layerPaint = new SKPaint { Color = SKColors.Blue };
        using var backdrop = SKImageFilter.CreateBlur(2f, 3f);
        canvas.DrawRect(new SKRect(1f, 2f, 30f, 40f), sourcePaint);
        var record = new SKCanvasSaveLayerRec
        {
            Bounds = new SKRect(5f, 6f, 70f, 60f),
            Paint = layerPaint,
            Backdrop = backdrop,
            Flags = SKCanvasSaveLayerRecFlags.PreserveLcdText |
                SKCanvasSaveLayerRecFlags.InitializeWithPrevious |
                SKCanvasSaveLayerRecFlags.F16ColorType,
        };

        var restoreCount = canvas.SaveLayer(in record);
        layerPaint.Color = SKColors.Green;

        Assert.Equal(1, restoreCount);
        Assert.NotSame(context, canvas.DrawingContext);
        Assert.Equal(new SKRect(5f, 6f, 70f, 60f), canvas.CurrentLayerBounds);
        Assert.Same(backdrop, canvas.CurrentLayerBackdrop);
        Assert.Equal(record.Flags, canvas.CurrentLayerFlags);
        Assert.NotNull(canvas.CurrentLayerPreviousContext);
        Assert.Single(canvas.CurrentLayerPreviousContext!.Commands);
        Assert.NotSame(layerPaint, canvas.CurrentLayerPaint);
        Assert.Equal(SKColors.Blue, canvas.CurrentLayerPaint!.Color);
        Assert.Equal(TextureFormat.Rgba16float, SKCanvas.GetSaveLayerTextureFormat(record.Flags));
    }
}
