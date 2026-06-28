namespace ProGPU.Wpf.Interop;

public interface IPortableVisualStateSource
{
    bool TryGetPortableVisualState(out PortableVisualState state);
}

public sealed class PortableVisualState
{
    public bool HasOffset { get; set; }

    public PortablePoint Offset { get; set; }

    public bool HasTransform { get; set; }

    public object? Transform { get; set; }

    public bool HasClip { get; set; }

    public object? Clip { get; set; }

    public bool HasScrollableAreaClip { get; set; }

    public PortableRect ScrollableAreaClip { get; set; }

    public bool HasOpacity { get; set; }

    public double Opacity { get; set; } = 1.0;

    public bool HasOpacityMask { get; set; }

    public object? OpacityMask { get; set; }
}
