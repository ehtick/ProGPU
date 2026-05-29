using System;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Scene;
using Microsoft.UI.Xaml;

namespace ProGPU.WinUI.Charts.Components
{
    public static class DataZoomComponent
    {
        public static void Draw(DrawingContext context, Rect sliderArea, Rect leftHandle, Rect rightHandle,
                                double zoomStart, double zoomEnd)
        {
            var cardBack = ThemeManager.GetBrush("CardBackground");
            var borderBrush = ThemeManager.GetBrush("ControlBorder");
            var selectionBrush = ThemeManager.GetBrush("SelectionHighlight");
            var borderPen = new Pen(borderBrush, 1f);

            // 1. Slider track background
            context.DrawRectangle(cardBack, borderPen, sliderArea);

            // 2. Translucent selection highlight area
            float leftX = sliderArea.X + (float)(zoomStart / 100.0 * sliderArea.Width);
            float rightX = sliderArea.X + (float)(zoomEnd / 100.0 * sliderArea.Width);
            var selectedRange = new Rect(leftX, sliderArea.Y, Math.Max(1f, rightX - leftX), sliderArea.Height);
            context.DrawRectangle(selectionBrush, null, selectedRange);

            // 3. Handles drawing
            var handleBrush = ThemeManager.GetBrush("TextPrimary");
            context.DrawRectangle(handleBrush, borderPen, leftHandle);
            context.DrawRectangle(handleBrush, borderPen, rightHandle);
        }
    }
}
