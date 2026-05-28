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
[TemplatePart(Name = "SwitchThumb", Type = typeof(Thumb))]
[TemplatePart(Name = "SwitchKnob", Type = typeof(Grid))]
[TemplatePart(Name = "SwitchKnobBounds", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "OuterBorder", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "SwitchKnobOn", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "SwitchKnobOff", Type = typeof(FrameworkElement))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class ToggleSwitch : Control
{
    private bool _isOn;
    private FrameworkElement? _content;

    private Thumb? _switchThumb;
    private Grid? _switchKnob;
    private FrameworkElement? _switchKnobBounds;
    private FrameworkElement? _outerBorder;
    private FrameworkElement? _switchKnobOn;
    private FrameworkElement? _switchKnobOff;
    private FrameworkElement? _onContentPresenter;
    private FrameworkElement? _offContentPresenter;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn != value)
            {
                _isOn = value;
                Invalidate();
                UpdateTemplateStates();
                Toggled?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged();
            }
        }
    }

    public FrameworkElement? Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                if (_content != null) RemoveChild(_content);
                _content = value;
                if (_content != null) AddChild(_content);
                Invalidate();
                OnPropertyChanged();
            }
        }
    }

    public event EventHandler? Toggled;

    public ToggleSwitch()
    {
        CornerRadius = 10f; // Track height is ~20, so corner radius 10 makes it a capsule.
        Padding = new Thickness(6, 4, 6, 4);
        
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _switchThumb = GetTemplateChild("SwitchThumb") as Thumb;
        if (_switchThumb != null)
        {
            _switchThumb.DragCompleted += (s, e) => {
                if (IsEnabled) IsOn = !IsOn;
            };
            // Also listen to tap/click on thumb
            _switchThumb.PointerReleased += (s, e) => {
                if (IsEnabled) IsOn = !IsOn;
            };
        }

        _switchKnob = GetTemplateChild("SwitchKnob") as Grid;
        _switchKnobBounds = GetTemplateChild("SwitchKnobBounds") as FrameworkElement;
        _outerBorder = GetTemplateChild("OuterBorder") as FrameworkElement;
        _switchKnobOn = GetTemplateChild("SwitchKnobOn") as FrameworkElement;
        _switchKnobOff = GetTemplateChild("SwitchKnobOff") as FrameworkElement;
        _onContentPresenter = GetTemplateChild("OnContentPresenter") as FrameworkElement;
        _offContentPresenter = GetTemplateChild("OffContentPresenter") as FrameworkElement;

        UpdateTemplateStates();
    }

    private void UpdateTemplateStates()
    {
        if (!HasTemplate) return;

        if (_switchKnob != null)
        {
            _switchKnob.HorizontalAlignment = IsOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _switchKnob.InvalidateMeasure();
            _switchKnob.InvalidateArrange();
        }

        if (_switchKnobBounds != null) _switchKnobBounds.Opacity = IsOn ? 1.0f : 0.0f;
        if (_outerBorder != null) _outerBorder.Opacity = IsOn ? 0.0f : 1.0f;
        if (_switchKnobOn != null) _switchKnobOn.Opacity = IsOn ? 1.0f : 0.0f;
        if (_switchKnobOff != null) _switchKnobOff.Opacity = IsOn ? 0.0f : 1.0f;
        if (_onContentPresenter != null) _onContentPresenter.Opacity = IsOn ? 1.0f : 0.0f;
        if (_offContentPresenter != null) _offContentPresenter.Opacity = IsOn ? 0.0f : 1.0f;
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (HasTemplate)
        {
            base.OnPointerReleased(e);
            return;
        }

        if (IsEnabled && IsPointerPressed && IsPointerOver)
        {
            IsOn = !IsOn;
        }
        base.OnPointerReleased(e);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (HasTemplate)
        {
            return base.MeasureOverride(availableSize);
        }
        float borderH = BorderThickness.Horizontal;
        float borderV = BorderThickness.Vertical;
        float paddingH = Padding.Horizontal;
        float paddingV = Padding.Vertical;

        float trackW = 40f;
        float trackH = 20f;
        float spacing = 8f;

        Vector2 inset = new Vector2(borderH + paddingH + trackW, borderV + paddingV);
        if (Content != null)
        {
            inset.X += spacing;
        }

        Vector2 contentAvail = new Vector2(
            Math.Max(0f, availableSize.X - inset.X),
            Math.Max(0f, availableSize.Y - inset.Y)
        );

        Vector2 contentDesired = Vector2.Zero;
        if (Content != null)
        {
            Content.Measure(contentAvail);
            contentDesired = Content.DesiredSize;
        }

        return new Vector2(
            contentDesired.X + borderH + trackW + (Content != null ? spacing : 0f),
            Math.Max(trackH, contentDesired.Y) + borderV
        );
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            return;
        }
        float leftInset = BorderThickness.Left;
        float topInset = BorderThickness.Top;
        float trackW = 40f;
        float spacing = 8f;

        if (Content != null)
        {
            float contentX = arrangeRect.X + leftInset + trackW + spacing;
            float contentW = arrangeRect.Width - (leftInset + BorderThickness.Right + trackW + spacing);
            float contentH = Content.DesiredSize.Y;
            float contentY = arrangeRect.Y + topInset + (arrangeRect.Height - (topInset + BorderThickness.Bottom) - contentH) / 2f;

            Content.Arrange(new Rect(contentX, contentY, contentW, contentH));
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (HasTemplate)
        {
            base.OnRender(context);
            return;
        }
        float leftInset = BorderThickness.Left + Padding.Left;
        float trackW = 40f;
        float trackH = 20f;
        float trackY = (Size.Y - trackH) / 2f;

        Rect trackRect = new Rect(leftInset, trackY, trackW, trackH);

        Brush? trackBg;
        Pen? trackBorder = null;

        if (!IsEnabled)
        {
            trackBg = ThemeManager.GetBrush("ToggleSwitchContainerBackground");
            trackBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush("ControlBorder"), 1f);
        }
        else if (IsOn)
        {
            trackBg = IsPointerPressed
                ? ThemeManager.GetBrush("ToggleSwitchFillOnPressed")
                : (IsPointerOver ? ThemeManager.GetBrush("ToggleSwitchFillOnPointerOver") : ThemeManager.GetBrush("ToggleSwitchFillOn"));
        }
        else
        {
            trackBg = Background ?? ThemeManager.GetBrush(IsPointerPressed ? "ControlBackgroundPressed" : IsPointerOver ? "ToggleSwitchContainerBackgroundPointerOver" : "ToggleSwitchContainerBackground");
            trackBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush(IsPointerOver ? "ControlBorderHover" : "ControlBorder"), 1f);
        }

        // Draw capsule track
        context.DrawRoundedRectangle(trackBg, trackBorder, trackRect, CornerRadius);

        // Draw thumb
        float thumbRadius = IsPointerPressed ? 5f : 6f; // breathing thumb
        float thumbMargin = 4f;
        float thumbDiameter = thumbRadius * 2f;

        float thumbMinX = trackRect.X + thumbMargin + thumbRadius;
        float thumbMaxX = trackRect.X + trackRect.Width - thumbMargin - thumbRadius;

        float thumbX = IsOn ? thumbMaxX : thumbMinX;
        float thumbY = trackRect.Y + trackRect.Height / 2f;

        Rect thumbRect = new Rect(thumbX - thumbRadius, thumbY - thumbRadius, thumbDiameter, thumbDiameter);

        Brush thumbBg;
        Pen? thumbBorder = null;

        if (!IsEnabled)
        {
            thumbBg = ThemeManager.GetBrush("ControlBackground");
            thumbBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush("ControlBorder"), 1f);
        }
        else if (IsOn)
        {
            thumbBg = ThemeManager.GetBrush("ToggleSwitchKnobFillOn");
        }
        else
        {
            thumbBg = IsPointerOver 
                ? ThemeManager.GetBrush("ToggleSwitchKnobFillOn")
                : ThemeManager.GetBrush("ToggleSwitchKnobFillOff");
            thumbBorder = new Pen(BorderBrush ?? ThemeManager.GetBrush("ControlBorder"), 1f);
        }

        context.DrawRoundedRectangle(thumbBg, thumbBorder, thumbRect, thumbRadius);

        // Draw focus ring around track
        if (IsEnabled && IsFocused)
        {
            var focusPen = new Pen(ThemeManager.GetBrush("SystemAccentColor"), 2f);
            Rect focusRect = new Rect(trackRect.X - 2f, trackRect.Y - 2f, trackRect.Width + 4f, trackRect.Height + 4f);
            context.DrawRoundedRectangle(null, focusPen, focusRect, CornerRadius + 2f);
        }

        base.OnRender(context);
    }
}
