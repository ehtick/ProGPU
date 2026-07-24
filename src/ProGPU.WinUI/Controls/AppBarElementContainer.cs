namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// Adapts arbitrary content so it can participate in a command bar.
/// </summary>
public class AppBarElementContainer : ContentControl, ICommandBarElement
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(AppBarElementContainer),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty IsInOverflowProperty = DependencyProperty.Register(
        nameof(IsInOverflow),
        typeof(bool),
        typeof(AppBarElementContainer),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty DynamicOverflowOrderProperty = DependencyProperty.Register(
        nameof(DynamicOverflowOrder),
        typeof(int),
        typeof(AppBarElementContainer),
        new PropertyMetadata(0));

    public bool IsCompact
    {
        get => (bool)(GetValue(IsCompactProperty) ?? false);
        set => SetValue(IsCompactProperty, value);
    }

    public bool IsInOverflow => (bool)(GetValue(IsInOverflowProperty) ?? false);

    public int DynamicOverflowOrder
    {
        get => (int)(GetValue(DynamicOverflowOrderProperty) ?? 0);
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    internal void SetIsInOverflow(bool value) => SetValue(IsInOverflowProperty, value);
}
