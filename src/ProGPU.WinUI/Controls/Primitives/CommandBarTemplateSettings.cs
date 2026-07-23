namespace Microsoft.UI.Xaml.Controls.Primitives;

public sealed class CommandBarTemplateSettings : DependencyObject
{
    public Visibility EffectiveOverflowButtonVisibility { get; internal set; }
    public double ContentHeight { get; internal set; }
    public Windows.Foundation.Rect OverflowContentClipRect { get; internal set; }
    public double OverflowContentMaxHeight { get; internal set; }
    public double OverflowContentMinWidth { get; internal set; }
    public double OverflowContentMaxWidth { get; internal set; }
    public double OverflowContentHorizontalOffset { get; internal set; }
    public double OverflowContentHeight { get; internal set; }
    public double NegativeOverflowContentHeight { get; internal set; }
    public double OverflowContentCompactYTranslation { get; internal set; }
    public double OverflowContentMinimalYTranslation { get; internal set; }
    public double OverflowContentHiddenYTranslation { get; internal set; }
}
