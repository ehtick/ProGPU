namespace SkiaSharp;

public class SKAutoCanvasRestore : IDisposable
{
    private SKCanvas? _canvas;
    private readonly int _saveCount;

    public SKAutoCanvasRestore(SKCanvas canvas)
        : this(canvas, doSave: true)
    {
    }

    public SKAutoCanvasRestore(SKCanvas canvas, bool doSave)
    {
        _canvas = canvas;
        if (canvas is null)
        {
            return;
        }

        _saveCount = canvas.SaveCount;
        if (doSave)
        {
            canvas.Save();
        }
    }

    public void Restore()
    {
        var canvas = _canvas;
        if (canvas is null)
        {
            return;
        }

        _canvas = null;
        canvas.RestoreToCount(_saveCount);
    }

    public void Dispose() => Restore();
}
