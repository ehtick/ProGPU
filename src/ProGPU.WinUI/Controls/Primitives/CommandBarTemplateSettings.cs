namespace Microsoft.UI.Xaml.Controls.Primitives;

public sealed class CommandBarTemplateSettings : DependencyObject
{
    public CommandBarOverflowButtonVisibility EffectiveOverflowButtonVisibility { get; internal set; }
    public double ContentHeight { get; internal set; }
    public double OverflowContentMaxHeight { get; internal set; }
    public double OverflowContentMinWidth { get; internal set; }
    public double OverflowContentCompactYTranslation { get; internal set; }
    public double OverflowContentMinimalYTranslation { get; internal set; }
    public double OverflowContentHiddenYTranslation { get; internal set; }
}
