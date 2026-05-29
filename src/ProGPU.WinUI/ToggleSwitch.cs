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

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = "Content")]
public class ToggleSwitch : ContentControl
{
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            "IsOn",
            typeof(bool),
            typeof(ToggleSwitch),
            new PropertyMetadata(false, (d, e) => ((ToggleSwitch)d).OnIsOnChanged((bool)(e.NewValue ?? false))));

    public bool IsOn
    {
        get => (bool)(GetValue(IsOnProperty) ?? false);
        set => SetValue(IsOnProperty, value);
    }

    public event EventHandler? Toggled;

    public ToggleSwitch()
    {
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            SetDefaultStyle(defaultStyle);
        }
    }

    private void OnIsOnChanged(bool isOn)
    {
        Invalidate();
        Toggled?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsOn));
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled && IsPointerPressed && IsPointerOver)
        {
            IsOn = !IsOn;
        }
        base.OnPointerReleased(e);
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
}
