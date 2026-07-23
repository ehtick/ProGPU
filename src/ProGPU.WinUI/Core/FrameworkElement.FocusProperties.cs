namespace Microsoft.UI.Xaml;

public partial class FrameworkElement
{
    public static readonly DependencyProperty AllowFocusOnInteractionProperty = DependencyProperty.Register(
        nameof(AllowFocusOnInteraction), typeof(bool), typeof(FrameworkElement), new PropertyMetadata(true));

    public static readonly DependencyProperty FocusVisualMarginProperty = DependencyProperty.Register(
        nameof(FocusVisualMargin), typeof(Thickness), typeof(FrameworkElement),
        new PropertyMetadata(default(Thickness)) { AffectsRender = true });

    public static readonly DependencyProperty AllowFocusWhenDisabledProperty = DependencyProperty.Register(
        nameof(AllowFocusWhenDisabled), typeof(bool), typeof(FrameworkElement), new PropertyMetadata(false));

    public bool AllowFocusOnInteraction
    {
        get => (bool)(GetValue(AllowFocusOnInteractionProperty) ?? true);
        set => SetValue(AllowFocusOnInteractionProperty, value);
    }

    public Thickness FocusVisualMargin
    {
        get => (Thickness)(GetValue(FocusVisualMarginProperty) ?? default(Thickness));
        set => SetValue(FocusVisualMarginProperty, value);
    }

    public bool AllowFocusWhenDisabled
    {
        get => (bool)(GetValue(AllowFocusWhenDisabledProperty) ?? false);
        set => SetValue(AllowFocusWhenDisabledProperty, value);
    }
}
