using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Scene;

namespace ProGPU.WinUI.Charts.Renderers
{
    public static class CandlestickRenderer
    {
        public static void Draw(DrawingContext context, CandlestickSeriesConfig csc, LinearScale xScale, LinearScale yScale,
                                Rect plotArea)
        {
            if (csc.Data.Count == 0) return;

            // Wick and Body colors
            var upColor = ChartUtils.ParseCssColor(csc.ItemStyle?.UpColor ?? "#E54B4B"); // Vibrant Up Red/Green standard
            var downColor = ChartUtils.ParseCssColor(csc.ItemStyle?.DownColor ?? "#2EC4B6"); // Slick Down Green/Blue standard
            var upBrush = new SolidColorBrush(upColor);
            var downBrush = new SolidColorBrush(downColor);
            var upPen = new Pen(upBrush, (float)(csc.ItemStyle?.BorderWidth ?? 1.0));
            var downPen = new Pen(downBrush, (float)(csc.ItemStyle?.BorderWidth ?? 1.0));

            // Visual bar width calculations
            float candleWidth = 8.0f;
            if (csc.BarWidth != null)
            {
                if (csc.BarWidth.Value.IsPercent)
                {
                    double val = ChartUtils.ParsePercent(csc.BarWidth.Value, plotArea.Width / csc.Data.Count);
                    candleWidth = (float)val;
                }
                else
                {
                    candleWidth = (float)csc.BarWidth.Value.RealValue!.Value;
                }
            }

            candleWidth = Math.Clamp(candleWidth, (float)csc.BarMinWidth, (float)(csc.BarMaxWidth ?? 40.0));

            foreach (var item in csc.Data)
            {
                float px = (float)xScale.Scale(item.Timestamp);
                float oY = (float)yScale.Scale(item.Open);
                float cY = (float)yScale.Scale(item.Close);
                float hY = (float)yScale.Scale(item.High);
                float lY = (float)yScale.Scale(item.Low);

                bool isUp = item.Close >= item.Open;
                var currentPen = isUp ? upPen : downPen;
                var currentBrush = isUp ? upBrush : downBrush;

                // 1. Draw thin wicks
                context.DrawLine(currentPen, new Vector2(px, hY), new Vector2(px, Math.Min(oY, cY)));
                context.DrawLine(currentPen, new Vector2(px, lY), new Vector2(px, Math.Max(oY, cY)));

                // 2. Draw thick candle body
                float top = Math.Min(oY, cY);
                float bottom = Math.Max(oY, cY);
                float h = Math.Max(1.0f, bottom - top);

                var rect = new Rect(px - candleWidth / 2.0f, top, candleWidth, h);

                if (csc.Style.Equals("hollow", StringComparison.OrdinalIgnoreCase) && isUp)
                {
                    // Hollow up candles (drawn only as empty border outline)
                    context.DrawRectangle(null, currentPen, rect);
                }
                else
                {
                    context.DrawRectangle(currentBrush, null, rect);
                }
            }
        }
    }
}
