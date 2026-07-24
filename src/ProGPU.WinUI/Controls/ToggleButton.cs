using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls.Primitives;

public class ToggleButton : ButtonBase
{
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            "IsChecked",
            typeof(bool),
            typeof(ToggleButton),
            new PropertyMetadata(false, (d, e) => ((ToggleButton)d).OnIsCheckedChanged(
                (bool)(e.OldValue ?? false),
                (bool)(e.NewValue ?? false))));

    public bool IsChecked
    {
        get => (bool)(GetValue(IsCheckedProperty) ?? false);
        set => SetValue(IsCheckedProperty, value);
    }

    public event EventHandler? Checked;
    public event EventHandler? Unchecked;
    public event EventHandler? CheckedChanged;

    public ToggleButton()
    {
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            SetDefaultStyle(defaultStyle);
        }
    }

    protected virtual void OnIsCheckedChanged(bool oldValue, bool newValue)
    {
        Invalidate();
        OnVisualStateChanged();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        if (IsChecked)
        {
            Checked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Unchecked?.Invoke(this, EventArgs.Empty);
        }
    }

    public override void OnVisualStateChanged()
    {
        base.OnVisualStateChanged();

        string checkState = IsChecked ? "Checked" : "Unchecked";
        string interactionState = !IsEnabled
            ? "Disabled"
            : IsPointerPressed && IsPointerOver
                ? "Pressed"
                : IsPointerOver
                    ? "PointerOver"
                    : "Normal";

        if (!VisualStateManager.GoToState(
                this,
                checkState + interactionState,
                useTransitions: true))
        {
            VisualStateManager.GoToState(this, checkState, useTransitions: true);
        }
    }

    protected override void OnClick()
    {
        IsChecked = !IsChecked;
        base.OnClick();
    }
}
