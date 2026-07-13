namespace SkiaSharp;

public enum SKCanvasSaveLayerRecFlags
{
    None = 0,
    PreserveLcdText = 2,
    InitializeWithPrevious = 4,
    F16ColorType = 0x10,
}

public struct SKCanvasSaveLayerRec
{
    public SKRect? Bounds { get; set; }

    public SKPaint? Paint { get; set; }

    public SKImageFilter? Backdrop { get; set; }

    public SKCanvasSaveLayerRecFlags Flags { get; set; }
}
