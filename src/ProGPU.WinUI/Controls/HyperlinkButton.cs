using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls;

public class HyperlinkButton : Button
{
    public new FrameworkElement? Content
    {
        get => base.Content as FrameworkElement;
        set
        {
            base.Content = value;
            UpdateContentForeground();
        }
    }

    public new Brush? Foreground
    {
        get => base.Foreground;
        set
        {
            base.Foreground = value;
            UpdateContentForeground();
            Invalidate();
        }
    }

    public HyperlinkButton()
    {
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0, 2, 0, 2);
        Foreground = new ThemeResourceBrush("HyperlinkButtonForeground");
    }

    private void UpdateContentForeground()
    {
        if (ContentVisual is TextVisual tv)
        {
            tv.Brush = Foreground;
        }
    }

    public override void OnRender(DrawingContext context)
    {
        // Transparent/no background unless background is explicitly set
        if (Background != null)
        {
            context.DrawRectangle(Background, null, new Rect(Vector2.Zero, Size));
        }

        // Active focus indicator
        if (IsEnabled && IsKeyboardFocusVisualVisible)
        {
            var focusPen = ThemeManager.GetPen("SystemAccentColor", 1f);
            context.DrawRectangle(null, focusPen, new Rect(0f, 0f, Size.X, Size.Y));
        }

        // Underline on hover
        if (IsEnabled && IsPointerOver && ContentVisual != null)
        {
            float leftInset = BorderThickness.Left + Padding.Left;
            float topInset = BorderThickness.Top + Padding.Top;
            float rightInset = BorderThickness.Right + Padding.Right;
            float bottomInset = BorderThickness.Bottom + Padding.Bottom;

            float childW = Math.Min(Size.X - (leftInset + rightInset), ContentVisual.DesiredSize.X);
            float childH = Math.Min(Size.Y - (topInset + bottomInset), ContentVisual.DesiredSize.Y);

            float childX = leftInset + (Size.X - (leftInset + rightInset) - childW) / 2f;
            float childY = topInset + (Size.Y - (topInset + bottomInset) - childH) / 2f;

            var accentBrush = Foreground ?? ThemeManager.GetBrush("SystemAccentColor");
            // Draw a 1px solid rectangle line as underline
            context.DrawRectangle(accentBrush, null, new Rect(childX, childY + childH + 1f, childW, 1f));
        }

        // Bypassing base.OnRender to avoid drawing standard button borders/shadows/overlays
    }
}
