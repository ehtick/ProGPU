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

public class ComboBoxItem : ContentControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            "IsSelected",
            typeof(bool),
            typeof(ComboBoxItem),
            new PropertyMetadata(false) { AffectsRender = true });

    private string _text = string.Empty;

    public bool IsSelected
    {
        get => (bool)(GetValue(IsSelectedProperty) ?? false);
        set => SetValue(IsSelectedProperty, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value ?? string.Empty;
                if (Content == null || Content is TextVisual)
                {
                    var tv = new TextVisual
                    {
                        Text = _text,
                        Brush = Foreground ?? new ThemeResourceBrush("TextPrimary"),
                        FontSize = 14f
                    };
                    Content = tv;
                }
            }
        }
    }



    public event EventHandler? Selected;

    public ComboBoxItem()
    {
        CornerRadius = 4f;
        Padding = new Thickness(8, 6, 8, 6);
        HeightConstraint = 32f;

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    public ComboBoxItem(string text) : this()
    {
        Text = text;
    }

    protected override void OnPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(dp, oldValue, newValue);
        if (dp == ForegroundProperty)
        {
            if (Content is TextVisual tv)
            {
                tv.Brush = Foreground ?? new ThemeResourceBrush("TextPrimary");
            }
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled && IsPointerPressed && IsPointerOver)
        {
            Selected?.Invoke(this, EventArgs.Empty);
        }
        base.OnPointerReleased(e);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float borderH = BorderThickness.Horizontal;
        float borderV = BorderThickness.Vertical;
        float paddingH = Padding.Horizontal;
        float paddingV = Padding.Vertical;

        Vector2 inset = new Vector2(borderH + paddingH, borderV + paddingV);
        Vector2 contentAvail = new Vector2(
            Math.Max(0f, availableSize.X - inset.X),
            Math.Max(0f, availableSize.Y - inset.Y)
        );

        Vector2 contentDesired = Vector2.Zero;
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            if (contentVisual is TextVisual tv && tv.Font == null)
            {
                tv.Font = GetActiveFont();
            }
            contentVisual.Measure(contentAvail);
            contentDesired = contentVisual.DesiredSize;
        }

        return new Vector2(
            Math.Max(64f - paddingH, contentDesired.X + borderH),
            HeightConstraint ?? Math.Max(32f - paddingV, contentDesired.Y + borderV)
        );
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            float leftInset = BorderThickness.Left;
            float topInset = BorderThickness.Top;
            float rightInset = BorderThickness.Right;
            float bottomInset = BorderThickness.Bottom;

            float childW = Math.Min(arrangeRect.Width - (leftInset + rightInset), contentVisual.DesiredSize.X);
            float childH = Math.Min(arrangeRect.Height - (topInset + bottomInset), contentVisual.DesiredSize.Y);

            float childX = arrangeRect.X + leftInset;
            float childY = arrangeRect.Y + topInset + (arrangeRect.Height - (topInset + bottomInset) - childH) / 2f;

            contentVisual.Arrange(new Rect(childX, childY, childW, childH));
        }
    }

    public override Brush? GetCurrentBackground()
    {
        if (IsSelected) return ThemeManager.GetBrush("ComboBoxItemBackgroundSelected") ?? ThemeManager.GetBrush("SelectionHighlight");
        if (IsPointerOver) return ThemeManager.GetBrush("ComboBoxItemBackgroundPointerOver") ?? ThemeManager.GetBrush("ControlBackgroundHover");
        return null;
    }

    public override Brush? GetCurrentBorderBrush()
    {
        if (IsSelected) return ThemeManager.GetBrush("SystemAccentColor");
        if (IsPointerOver) return base.GetCurrentBorderBrush();
        return null;
    }

    public override void OnRender(DrawingContext context)
    {
        Brush? bg = GetCurrentBackground();
        Brush? borderBrush = GetCurrentBorderBrush();
        Pen? pen = borderBrush != null ? new Pen(borderBrush, BorderThickness.Left > 0 ? BorderThickness.Left : 1f) : null;

        if (bg != null)
        {
            context.DrawRoundedRectangle(bg, pen, new Rect(Vector2.Zero, Size), CornerRadius);
        }

        base.OnRender(context);
    }
}
