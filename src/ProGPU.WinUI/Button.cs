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
public class Button : ContentControl
{

    public event EventHandler? Click;

    public Button()
    {
        CornerRadius = 6f;
        Padding = new Thickness(12, 6, 12, 6);
        
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled && IsPointerPressed && IsPointerOver)
        {
            Click?.Invoke(this, EventArgs.Empty);
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

        Vector2 inset = new Vector2(borderH + paddingH, borderV + paddingV);
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

        float minW = 64f;
        float minH = 28f;
        return new Vector2(
            Math.Max(minW - paddingH, contentDesired.X + borderH),
            Math.Max(minH - paddingV, contentDesired.Y + borderV)
        );
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            return;
        }

        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            float leftInset = BorderThickness.Left;
            float topInset = BorderThickness.Top;
            float rightInset = BorderThickness.Right;
            float bottomInset = BorderThickness.Bottom;

            float childW = Math.Min(arrangeRect.Width - (leftInset + rightInset), contentVisual.DesiredSize.X);
            float childH = Math.Min(arrangeRect.Height - (topInset + bottomInset), contentVisual.DesiredSize.Y);

            float childX = arrangeRect.X + leftInset + (arrangeRect.Width - (leftInset + rightInset) - childW) / 2f;
            float childY = arrangeRect.Y + topInset + (arrangeRect.Height - (topInset + bottomInset) - childH) / 2f;

            contentVisual.Arrange(new Rect(childX, childY, childW, childH));
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (HasTemplate)
        {
            base.OnRender(context);
            return;
        }

        Brush? bg = GetCurrentBackground();
        Brush? borderBrush = GetCurrentBorderBrush();
        Pen pen = new Pen(borderBrush ?? ThemeManager.GetBrush("ControlBorder"), BorderThickness.Left > 0 ? BorderThickness.Left : 1f);

        // Draw soft 3D elevation shadows (ambient & penumbra layers)
        if (IsEnabled)
        {
            // Ambient shadow (offset Y=2, very soft, low opacity)
            context.FillRoundedRectangle(ThemeManager.GetBrush("ButtonAmbientShadow"), new Rect(0, 2, Size.X, Size.Y), CornerRadius);

            // Penumbra shadow (offset Y=1, tighter, slightly higher opacity)
            context.FillRoundedRectangle(ThemeManager.GetBrush("ButtonPenumbraShadow"), new Rect(0, 1, Size.X, Size.Y), CornerRadius);
        }

        // Draw main button background and border
        context.DrawRoundedRectangle(bg, pen, new Rect(Vector2.Zero, Size), CornerRadius);

        // Draw active focus ring indicator
        if (IsEnabled && IsFocused)
        {
            var focusPen = new Pen(ThemeManager.GetBrush("SystemAccentColor"), 2f); // Sharp Segoe Blue active focus ring
            // Slightly inset focus ring for clean aesthetics
            float inset = 1.5f;
            var focusRect = new Rect(inset, inset, Size.X - 2 * inset, Size.Y - 2 * inset);
            float focusR = Math.Max(0f, CornerRadius - inset);
            context.DrawRoundedRectangle(null, focusPen, focusRect, focusR);
        }

        base.OnRender(context);
    }

    public override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsEnabled && (e.Key == Silk.NET.Input.Key.Space || e.Key == Silk.NET.Input.Key.Enter))
        {
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
