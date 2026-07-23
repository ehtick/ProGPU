using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;

namespace Microsoft.UI.Xaml.Controls;

public enum LightDismissOverlayMode
{
    Auto = 0,
    On = 1,
    Off = 2
}

[ContentProperty(Name = nameof(Content))]
public class Flyout : FlyoutBase
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(UIElement), typeof(Flyout), new PropertyMetadata(null));

    public static readonly DependencyProperty FlyoutPresenterStyleProperty = DependencyProperty.Register(
        nameof(FlyoutPresenterStyle), typeof(Style), typeof(Flyout), new PropertyMetadata(null));

    public UIElement? Content { get => GetValue(ContentProperty) as UIElement; set => SetValue(ContentProperty, value); }
    public Style? FlyoutPresenterStyle { get => GetValue(FlyoutPresenterStyleProperty) as Style; set => SetValue(FlyoutPresenterStyleProperty, value); }

    protected override Control CreatePresenter() => new FlyoutPresenter
    {
        Content = Content,
        Style = FlyoutPresenterStyle
    };
}

public class FlyoutPresenter : ContentControl
{
    public static readonly DependencyProperty IsDefaultShadowEnabledProperty = DependencyProperty.Register(
        nameof(IsDefaultShadowEnabled), typeof(bool), typeof(FlyoutPresenter), new PropertyMetadata(true));

    public bool IsDefaultShadowEnabled
    {
        get => (bool)(GetValue(IsDefaultShadowEnabledProperty) ?? true);
        set => SetValue(IsDefaultShadowEnabledProperty, value);
    }
}
