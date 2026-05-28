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

[TemplatePart(Name = "HorizontalThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "HorizontalTemplate", Type = typeof(Grid))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class Slider : Control
{
    private float _minimum = 0f;
    private float _maximum = 100f;
    private float _value = 0f;
    private bool _isDragging;

    private Thumb? _horizontalThumb;
    private Grid? _horizontalTemplate;

    public float Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum != value)
            {
                _minimum = value;
                Value = Math.Clamp(Value, _minimum, _maximum);
                Invalidate();
                OnPropertyChanged();
            }
        }
    }

    public float Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum != value)
            {
                _maximum = value;
                Value = Math.Clamp(Value, _minimum, _maximum);
                Invalidate();
                OnPropertyChanged();
            }
        }
    }

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
                UpdateTemplateLayout();
                ValueChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged();
            }
        }
    }

    public event EventHandler? ValueChanged;

    public Slider()
    {
        HeightConstraint = 32f;
        WidthConstraint = 200f;
        
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _horizontalThumb = GetTemplateChild("HorizontalThumb") as Thumb;
        if (_horizontalThumb != null)
        {
            _horizontalThumb.DragDelta += OnThumbDragDelta;
        }

        _horizontalTemplate = GetTemplateChild("HorizontalTemplate") as Grid;
        UpdateTemplateLayout();
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        float totalWidth = Size.X;
        float thumbWidth = _horizontalThumb != null ? _horizontalThumb.Size.X : 8f;
        if (thumbWidth <= 0f) thumbWidth = 8f;
        float trackWidth = totalWidth - thumbWidth;
        if (trackWidth <= 0f) return;

        float deltaPct = e.HorizontalChange / trackWidth;
        Value = Math.Clamp(Value + deltaPct * (Maximum - Minimum), Minimum, Maximum);
    }

    private void UpdateTemplateLayout()
    {
        if (!HasTemplate || _horizontalTemplate == null) return;

        float totalWidth = Size.X;
        float thumbWidth = _horizontalThumb != null ? _horizontalThumb.Size.X : 8f;
        if (thumbWidth <= 0f) thumbWidth = 8f;
        float trackWidth = totalWidth - thumbWidth;
        float pct = 0f;
        if (Maximum > Minimum)
        {
            pct = (Value - Minimum) / (Maximum - Minimum);
        }

        if (_horizontalTemplate.ColumnDefinitions.Count >= 3)
        {
            _horizontalTemplate.ColumnDefinitions[0] = new GridLength(pct * trackWidth, GridUnitType.Absolute);
            _horizontalTemplate.InvalidateMeasure();
            _horizontalTemplate.InvalidateArrange();
            _horizontalTemplate.Invalidate();
        }
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (HasTemplate)
        {
            base.OnPointerPressed(e);
            return;
        }

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
        if (HasTemplate)
        {
            base.OnPointerReleased(e);
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;
            InputSystem.ReleasePointerCapture();
        }
        base.OnPointerReleased(e);
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (HasTemplate)
        {
            base.OnPointerMoved(e);
            return;
        }

        if (_isDragging && IsEnabled)
        {
            UpdateValueFromPos(e.Position.X);
        }
        base.OnPointerMoved(e);
    }

    private void UpdateValueFromPos(float localX)
    {
        float thumbRadius = 8f;
        float width = Size.X;
        float trackWidth = width - 2 * thumbRadius;
        if (trackWidth <= 0f) return;

        float pct = (localX - thumbRadius) / trackWidth;
        pct = Math.Clamp(pct, 0f, 1f);
        Value = Minimum + pct * (Maximum - Minimum);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (HasTemplate)
        {
            return base.MeasureOverride(availableSize);
        }
        float w = WidthConstraint ?? Math.Max(120f, availableSize.X);
        float h = HeightConstraint ?? 32f;
        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            UpdateTemplateLayout();
            return;
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (HasTemplate)
        {
            base.OnRender(context);
            return;
        }
        float baseThumbRadius = 8f;
        float trackHeight = 4f;
        float yCenter = Size.Y / 2f;

        float width = Size.X;
        float trackWidth = width - 2 * baseThumbRadius;

        float pct = 0f;
        if (Maximum > Minimum)
        {
            pct = (Value - Minimum) / (Maximum - Minimum);
        }

        float thumbX = baseThumbRadius + pct * trackWidth;

        // Micro-animated breathing thumb: 7f normal, 9f on hover/drag
        float drawThumbRadius = (IsPointerOver || _isDragging) && IsEnabled ? 9f : 7f;

        // 1. Draw Inactive Track (Right side)
        Rect inactiveRect = new Rect(thumbX, yCenter - trackHeight / 2f, Math.Max(0f, width - baseThumbRadius - thumbX), trackHeight);
        Brush inactiveBg = Background ?? ThemeManager.GetBrush(IsEnabled ? "SliderTrackFill" : "SliderTrackFillDisabled");
        context.DrawRectangle(inactiveBg, null, inactiveRect);

        // 2. Draw Active Track (Left side)
        if (thumbX > baseThumbRadius)
        {
            Rect activeRect = new Rect(baseThumbRadius, yCenter - trackHeight / 2f, thumbX - baseThumbRadius, trackHeight);
            Brush activeBg = ThemeManager.GetBrush(IsEnabled 
                ? (_isDragging ? "SliderTrackValueFillPressed" : IsPointerOver ? "SliderTrackValueFillPointerOver" : "SliderTrackValueFill") 
                : "SliderTrackValueFillDisabled");
            context.DrawRectangle(activeBg, null, activeRect);
        }

        // 3. Draw Thumb (Circle)
        Rect thumbRect = new Rect(thumbX - drawThumbRadius, yCenter - drawThumbRadius, drawThumbRadius * 2f, drawThumbRadius * 2f);
        Brush thumbBg;
        Pen? thumbBorder;

        if (!IsEnabled)
        {
            thumbBg = ThemeManager.GetBrush("ControlBackground");
            thumbBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush("SliderThumbBorderBrush"), 1f);
        }
        else if (_isDragging)
        {
            thumbBg = ThemeManager.GetBrush("SliderTrackValueFillPressed"); // Pressed Accent Segoe Blue
            thumbBorder = new Pen(ThemeManager.CurrentTheme == ElementTheme.Light ? ThemeManager.GetBrush("CardBackground") : ThemeManager.GetBrush("TextPrimary"), 1.5f);
        }
        else if (IsPointerOver)
        {
            thumbBg = ThemeManager.CurrentTheme == ElementTheme.Light ? ThemeManager.GetBrush("CardBackground") : ThemeManager.GetBrush("TextPrimary");
            thumbBorder = new Pen(ThemeManager.GetBrush("SliderTrackValueFillPointerOver"), 1f);
        }
        else
        {
            thumbBg = ThemeManager.CurrentTheme == ElementTheme.Light ? ThemeManager.GetBrush("CardBackground") : ThemeManager.GetBrush("TextPrimary");
            thumbBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush("SliderThumbBorderBrush"), 1f);
        }

        // Standard Circle rendering using rounded rect path (radius = drawThumbRadius)
        context.DrawRoundedRectangle(thumbBg, thumbBorder, thumbRect, drawThumbRadius);

        // Draw active focus ring indicator around thumb
        if (IsEnabled && IsFocused)
        {
            var focusPen = new Pen(ThemeManager.GetBrush("SystemAccentColor"), 1.5f);
            Rect focusRect = new Rect(thumbRect.X - 2.5f, thumbRect.Y - 2.5f, thumbRect.Width + 5f, thumbRect.Height + 5f);
            context.DrawRoundedRectangle(null, focusPen, focusRect, drawThumbRadius + 2.5f);
        }

        base.OnRender(context);
    }
}
