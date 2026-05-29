using System;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Scene;

namespace ProGPU.WinUI.Charts.Renderers
{
    public static class BarRenderer
    {
        public static void Draw(DrawingContext context, BarSeriesConfig bs, int seriesIdx, int totalBars,
                                LinearScale xScale, LinearScale yScale, Brush brush, Rect plotArea)
        {
            int count = bs.Data!.PointCount;
            if (count == 0) return;

            float baselineY = (float)yScale.Scale(0.0);
            if (!double.IsFinite(baselineY)) baselineY = plotArea.Y + plotArea.Height;

            // Bar sizing computations
            float catWidth = plotArea.Width / count;
            float barWidth = catWidth * 0.6f; // Standard defaults: 60% categorical width

            if (bs.BarWidth != null)
            {
                if (bs.BarWidth.Value.IsPercent)
                {
                    double val = ChartUtils.ParsePercent(bs.BarWidth.Value, catWidth);
                    barWidth = (float)val;
                }
                else
                {
                    barWidth = (float)bs.BarWidth.Value.RealValue!.Value;
                }
            }

            // Offset alignment for clustered multi-series bars
            float barOffset = 0f;
            if (totalBars > 1 && string.IsNullOrEmpty(bs.Stack))
            {
                float totalWidth = barWidth * totalBars;
                float gap = totalWidth * 0.05f; // small spacing
                barOffset = (seriesIdx - (totalBars - 1) / 2.0f) * (barWidth + gap);
            }

            for (int i = 0; i < count; i++)
            {
                double x = bs.Data.GetX(i);
                double y = bs.Data.GetY(i);

                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

                float px = (float)xScale.Scale(x) + barOffset;
                float py = (float)yScale.Scale(y);

                float left = px - barWidth / 2.0f;
                float right = px + barWidth / 2.0f;
                float top = Math.Min(py, baselineY);
                float bottom = Math.Max(py, baselineY);

                var r = new Rect(left, top, barWidth, Math.Max(1f, bottom - top));

                // Corners configurations
                float rad = (float)(bs.ItemStyle?.BorderRadius ?? 0.0);
                if (rad > 0f)
                {
                    context.FillRoundedRectangle(brush, r, rad);
                }
                else
                {
                    context.DrawRectangle(brush, null, r);
                }
            }
        }
    }
}
