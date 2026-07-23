using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = "Content")]
public class Button : ButtonBase
{
    public static readonly DependencyProperty FlyoutProperty = DependencyProperty.Register(
        nameof(Flyout), typeof(FlyoutBase), typeof(Button), new PropertyMetadata(null));

    public FlyoutBase? Flyout
    {
        get => GetValue(FlyoutProperty) as FlyoutBase;
        set => SetValue(FlyoutProperty, value);
    }

    public Button()
    {
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            SetDefaultStyle(defaultStyle);
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return base.MeasureOverride(availableSize);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        base.ArrangeOverride(arrangeRect);
    }

    public override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
    }

    protected override void OnClick()
    {
        base.OnClick();
        Flyout?.ShowAt(this);
    }
}
