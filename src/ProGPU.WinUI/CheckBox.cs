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

public class CheckBox : ContentControl
{
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            "IsChecked",
            typeof(bool),
            typeof(CheckBox),
            new PropertyMetadata(false, (d, e) => ((CheckBox)d).OnCheckedChanged()));

    public bool IsChecked
    {
        get => (bool)(GetValue(IsCheckedProperty) ?? false);
        set => SetValue(IsCheckedProperty, value);
    }

    public event EventHandler? Checked;
    public event EventHandler? Unchecked;
    public event EventHandler? CheckedChanged;

    public CheckBox()
    {
        CornerRadius = 4f;
        Padding = new Thickness(8, 4, 8, 4);

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    public override Brush? GetCurrentBackground()
    {
        if (!IsEnabled) return ThemeManager.GetBrush("CheckBoxBackgroundDisabled") ?? Background;
        if (IsChecked)
        {
            if (IsPointerPressed) return ThemeManager.GetBrush("CheckBoxCheckBackgroundFillCheckedPressed");
            if (IsPointerOver) return ThemeManager.GetBrush("CheckBoxCheckBackgroundFillCheckedPointerOver");
            return ThemeManager.GetBrush("CheckBoxCheckBackgroundFillChecked");
        }
        return base.GetCurrentBackground();
    }

    public override Brush? GetCurrentBorderBrush()
    {
        if (!IsEnabled) return ThemeManager.GetBrush("CheckBoxBorderBrushDisabled") ?? BorderBrush;
        if (IsChecked) return null;
        return base.GetCurrentBorderBrush();
    }

    private void OnCheckedChanged()
    {
        Invalidate();
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

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled && IsPointerPressed && IsPointerOver)
        {
            IsChecked = !IsChecked;
        }
        base.OnPointerReleased(e);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float borderH = BorderThickness.Horizontal;
        float borderV = BorderThickness.Vertical;
        float paddingH = Padding.Horizontal;
        float paddingV = Padding.Vertical;

        float boxSize = 18f;
        float spacing = 8f;

        Vector2 inset = new Vector2(borderH + paddingH + boxSize + spacing, borderV + paddingV);
        Vector2 contentAvail = new Vector2(
            Math.Max(0f, availableSize.X - inset.X),
            Math.Max(0f, availableSize.Y - inset.Y)
        );

        Vector2 contentDesired = Vector2.Zero;
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            contentVisual.Measure(contentAvail);
            contentDesired = contentVisual.DesiredSize;
        }

        return new Vector2(
            contentDesired.X + borderH + boxSize + spacing,
            Math.Max(boxSize, contentDesired.Y) + borderV
        );
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        float leftInset = BorderThickness.Left;
        float topInset = BorderThickness.Top;
        float boxSize = 18f;
        float spacing = 8f;

        // Vertically center the box in the arrange area
        float boxY = arrangeRect.Y + topInset + (arrangeRect.Height - (topInset + BorderThickness.Bottom) - boxSize) / 2f;

        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            float contentX = arrangeRect.X + leftInset + boxSize + spacing;
            float contentW = arrangeRect.Width - (leftInset + BorderThickness.Right + boxSize + spacing);
            float contentH = contentVisual.DesiredSize.Y;
            float contentY = arrangeRect.Y + topInset + (arrangeRect.Height - (topInset + BorderThickness.Bottom) - contentH) / 2f;

            contentVisual.Arrange(new Rect(contentX, contentY, contentW, contentH));
        }
    }

    public override void OnRender(DrawingContext context)
    {
        float leftInset = BorderThickness.Left + Padding.Left;
        float boxSize = 18f;
        float boxY = (Size.Y - boxSize) / 2f;

        Rect boxRect = new Rect(leftInset, boxY, boxSize, boxSize);

        // Styling brushes
        Brush? boxBg = GetCurrentBackground();
        Brush? borderBrush = GetCurrentBorderBrush();
        Pen? boxBorder = borderBrush != null ? new Pen(borderBrush, BorderThickness.Left > 0 ? BorderThickness.Left : 1f) : null;

        // Draw check box frame
        context.DrawRoundedRectangle(boxBg, boxBorder, boxRect, CornerRadius);

        // Draw checkmark vector if checked
        if (IsChecked)
        {
            var checkGeometry = new PathGeometry();
            var checkFigure = new PathFigure(new Vector2(boxRect.X + 4.5f, boxRect.Y + 9f), isClosed: false);
            checkFigure.Segments.Add(new LineSegment(new Vector2(boxRect.X + 8f, boxRect.Y + 12.5f)));
            checkFigure.Segments.Add(new LineSegment(new Vector2(boxRect.X + 13.5f, boxRect.Y + 5f)));
            checkGeometry.Figures.Add(checkFigure);

            // Draw white/muted checkmark stroke
            var checkBrush = IsEnabled 
                ? (ThemeManager.GetBrush("CheckBoxCheckGlyphForegroundChecked") ?? (ThemeManager.CurrentTheme == ElementTheme.Light ? ThemeManager.GetBrush("CardBackground") : ThemeManager.GetBrush("TextPrimary")))
                : ThemeManager.GetBrush("TextSecondary");
            var checkPen = new Pen(checkBrush, 2f);
            context.DrawPath(null, checkPen, checkGeometry);
        }

        // Draw active focus ring indicator around the checkbox box frame
        if (IsEnabled && IsFocused)
        {
            var focusPen = new Pen(ThemeManager.GetBrush("SystemAccentColor"), 2f); // Segoe Blue active focus ring
            Rect focusRect = new Rect(boxRect.X - 2f, boxRect.Y - 2f, boxRect.Width + 4f, boxRect.Height + 4f);
            context.DrawRoundedRectangle(null, focusPen, focusRect, CornerRadius + 2f);
        }

        base.OnRender(context);
    }
}
