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

public class Slider : Control
{
    private bool _isDragging;

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            "Minimum",
            typeof(float),
            typeof(Slider),
            new PropertyMetadata(0f, (d, e) => ((Slider)d).OnMinimumChanged((float)(e.NewValue ?? 0f))));

    public float Minimum
    {
        get => (float)(GetValue(MinimumProperty) ?? 0f);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            "Maximum",
            typeof(float),
            typeof(Slider),
            new PropertyMetadata(100f, (d, e) => ((Slider)d).OnMaximumChanged((float)(e.NewValue ?? 100f))));

    public float Maximum
    {
        get => (float)(GetValue(MaximumProperty) ?? 100f);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            "Value",
            typeof(float),
            typeof(Slider),
            new PropertyMetadata(0f, (d, e) => ((Slider)d).OnValueChanged((float)(e.NewValue ?? 0f))));

    public float Value
    {
        get => (float)(GetValue(ValueProperty) ?? 0f);
        set => SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    public event EventHandler? ValueChanged;

    public Slider()
    {
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            SetDefaultStyle(defaultStyle);
        }
    }

    private void OnMinimumChanged(float newMin)
    {
        Value = Math.Clamp(Value, newMin, Maximum);
        OnPropertyChanged(nameof(Minimum));
    }

    private void OnMaximumChanged(float newMax)
    {
        Value = Math.Clamp(Value, Minimum, newMax);
        OnPropertyChanged(nameof(Maximum));
    }

    private void OnValueChanged(float newValue)
    {
        ValueChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Value));
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            _isDragging = true;
            InputSystem.CapturePointer(this);
            UpdateValueFromPos(e.Position.X);
            base.OnPointerPressed(e);
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            InputSystem.ReleasePointerCapture();
        }
        base.OnPointerReleased(e);
    }

    public override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        _isDragging = false;
        base.OnPointerCanceled(e);
    }

    public override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        _isDragging = false;
        base.OnPointerCaptureLost(e);
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (_isDragging && IsEnabled)
        {
            UpdateValueFromPos(e.Position.X);
        }
        base.OnPointerMoved(e);
    }

    private void UpdateValueFromPos(float localX)
    {
        float thumbRadius = ActualThemeFamily == VisualThemeFamily.macOS ? 7f : 8f;
        float width = Size.X;
        float trackWidth = width - 2 * thumbRadius;
        if (trackWidth <= 0f) return;

        float pct = (localX - thumbRadius) / trackWidth;
        pct = Math.Clamp(pct, 0f, 1f);
        Value = Minimum + pct * (Maximum - Minimum);
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
