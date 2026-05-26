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
using static System.FormattableString;

namespace Microsoft.UI.Xaml.Controls;

public class ThemeToggleIconControl : Control
{
    public ThemeToggleIconControl()
    {
        WidthConstraint = 20f;
        HeightConstraint = 20f;
        Margin = new Thickness(0, 0, 0, 0);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float w = WidthConstraint ?? 20f;
        float h = HeightConstraint ?? 20f;
        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
    }

    public override void OnRender(DrawingContext context)
    {
        var textPrimary = ThemeManager.GetBrush("TextPrimary");
        var primaryPen = new Pen(textPrimary, 1.5f);

        float cx = Size.X / 2f;
        float cy = Size.Y / 2f;

        if (ThemeManager.CurrentTheme == ElementTheme.Light)
        {
            // Draw a beautiful vector Sun
            float r = 4.5f;
            context.DrawCircle(null, primaryPen, new Vector2(cx, cy), r);

            // 8 radiating sun rays
            int numRays = 8;
            for (int i = 0; i < numRays; i++)
            {
                float angle = (float)(i * 2 * Math.PI / numRays);
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                Vector2 start = new Vector2(cx + (r + 2f) * cos, cy + (r + 2f) * sin);
                Vector2 end = new Vector2(cx + (r + 5f) * cos, cy + (r + 5f) * sin);
                context.DrawLine(primaryPen, start, end);
            }
        }
        else
        {
            // Draw a beautiful crescent Moon using cubic beziers
            // High fidelity curved path designed to look extremely sleek
            var moonPath = PathGeometry.Parse(Invariant($"M {cx + 2.5f} {cy - 6.5f} C {cx - 4.5f} {cy - 6.5f} {cx - 4.5f} {cy + 6.5f} {cx + 2.5f} {cy + 6.5f} C {cx - 0.5f} {cy + 3.5f} {cx - 0.5f} {cy - 3.5f} {cx + 2.5f} {cy - 6.5f} Z"));
            context.DrawPath(textPrimary, primaryPen, moonPath);
        }

        base.OnRender(context);
    }
}
