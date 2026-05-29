using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Text;
using ProGPU.Scene;
using Microsoft.UI.Xaml;

namespace ProGPU.WinUI.Charts.Components
{
    public static class TooltipComponent
    {
        public static void Draw(DrawingContext context, ChartGPUOptions options, LinearScale xScale, LinearScale yScale,
                                TtfFont defaultFont, Rect plotArea, Vector2 lastHoverPos, bool hasInteractionActive)
        {
            if (options == null || !hasInteractionActive) return;

            var crosshairColor = ThemeManager.GetBrush("TextSecondary");
            var crosshairPen = new Pen(crosshairColor, 1f);
            var palette = options.Palette ?? new string[] { "#0078D4", "#107C41", "#D83B01", "#A8003F", "#5C2D91" };

            // 1. Find nearest points across active series (Cartesian, line, area, bar, scatter)
            var match = ChartInteraction.FindNearestPoint((IReadOnlyList<SeriesConfig>?)options.Series ?? Array.Empty<SeriesConfig>(), lastHoverPos.X, lastHoverPos.Y, xScale, yScale, 20.0);

            // 2. Find nearest points in Candlestick series
            var candlestickSeries = new List<CandlestickSeriesConfig>();
            if (options.Series != null)
            {
                for (int s = 0; s < options.Series.Count; s++)
                {
                    if (options.Series[s] is CandlestickSeriesConfig c && c.Visible)
                    {
                        candlestickSeries.Add(c);
                    }
                }
            }

            CandlestickMatch? candleMatch = null;
            if (candlestickSeries.Count > 0)
            {
                candleMatch = ChartInteraction.FindCandlestick(candlestickSeries, lastHoverPos.X, lastHoverPos.Y, xScale, yScale, 20.0);
            }

            // Determine closest X value
            double closestXVal = double.NaN;
            if (match != null)
            {
                closestXVal = match.Point.X;
            }
            else if (candleMatch != null)
            {
                closestXVal = candleMatch.Point.Timestamp;
            }

            if (!double.IsFinite(closestXVal)) return;

            // Draw vertical crosshair line at exact snapped nearest X
            float crosshairX = (float)xScale.Scale(closestXVal);
            if (crosshairX >= plotArea.X && crosshairX <= plotArea.X + plotArea.Width)
            {
                context.DrawLine(crosshairPen, new Vector2(crosshairX, plotArea.Y), new Vector2(crosshairX, plotArea.Y + plotArea.Height));
            }

            var paramList = new List<TooltipParams>();

            // Find all points at this X value
            var pointsAtX = ChartInteraction.FindPointsAtX((IReadOnlyList<SeriesConfig>?)options.Series ?? Array.Empty<SeriesConfig>(), crosshairX, xScale, 20.0);
            foreach (var patX in pointsAtX)
            {
                var seriesConfig = options.Series![patX.SeriesIndex];
                var seriesColor = palette[patX.SeriesIndex % palette.Count];

                paramList.Add(new TooltipParams
                {
                    SeriesName = seriesConfig.Name ?? $"Series {patX.SeriesIndex}",
                    SeriesIndex = patX.SeriesIndex,
                    DataIndex = patX.DataIndex,
                    Value = new double[] { patX.Point.X, patX.Point.Y },
                    Color = seriesColor
                });

                // Highlight point dot (only for line/area series)
                if (seriesConfig is LineSeriesConfig || seriesConfig is AreaSeriesConfig)
                {
                    float py = (float)yScale.Scale(patX.Point.Y);
                    context.FillCircle(new SolidColorBrush(ChartUtils.ParseCssColor(seriesColor)), new Vector2(crosshairX, py), 4f);
                }
            }

            // Add candlestick if matched
            if (candleMatch != null && candleMatch.Point.Timestamp == closestXVal)
            {
                int originalIdx = -1;
                if (options.Series != null)
                {
                    for (int s = 0; s < options.Series.Count; s++)
                    {
                        if (options.Series[s] is CandlestickSeriesConfig csc && csc.Visible)
                        {
                            originalIdx = s;
                            break;
                        }
                    }
                }

                if (originalIdx == -1) originalIdx = candleMatch.SeriesIndex; // Fallback

                var seriesColor = palette[originalIdx % palette.Count];
                paramList.Add(new TooltipParams
                {
                    SeriesName = options.Series![originalIdx].Name ?? $"Candlestick {originalIdx}",
                    SeriesIndex = originalIdx,
                    DataIndex = candleMatch.DataIndex,
                    Value = new double[] { candleMatch.Point.Timestamp, candleMatch.Point.Open, candleMatch.Point.Close, candleMatch.Point.Low, candleMatch.Point.High },
                    Color = seriesColor
                });
            }

            if (paramList.Count == 0 || !options.Tooltip.Show) return;

            // Draw sleek, Glassmorphic Tooltip Card overlay
            float cardWidth = 180f;
            float cardHeight = 35f + paramList.Count * 18f;

            // Adaptive coordinate positioning
            float tx = lastHoverPos.X + 15f;
            float ty = lastHoverPos.Y + 15f;

            if (tx + cardWidth > plotArea.X + plotArea.Width)
            {
                tx = lastHoverPos.X - cardWidth - 15f;
            }
            if (ty + cardHeight > plotArea.Y + plotArea.Height)
            {
                ty = plotArea.Y + plotArea.Height - cardHeight;
            }

            var cardBack = new SolidColorBrush(new Vector4(0.08f, 0.08f, 0.12f, 0.82f)); // Sleek acrylic glass
            var cardBorder = ThemeManager.GetBrush("ControlBorder");
            var textPrimary = ThemeManager.GetBrush("TextPrimary");
            var cardBorderPen = new Pen(cardBorder, 1f);

            context.DrawRoundedRectangle(cardBack, cardBorderPen, new Rect(tx, ty, cardWidth, cardHeight), 6f);

            // Title label
            string headerText = string.Empty;
            if (options.XAxis?.Type == AxisType.Time)
            {
                headerText = DateTimeOffset.FromUnixTimeMilliseconds((long)closestXVal).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                headerText = $"X: {closestXVal:F1}";
            }

            context.DrawText(headerText, defaultFont, 10f, ThemeManager.GetBrush("TextSecondary"), new Vector2(tx + 10f, ty + 8f));

            // Populate hover details
            for (int p = 0; p < paramList.Count; p++)
            {
                var valItem = paramList[p];
                float lineY = ty + 26f + p * 18f;

                // Color indicator dot
                var dotBrush = new SolidColorBrush(ChartUtils.ParseCssColor(valItem.Color));
                context.FillCircle(dotBrush, new Vector2(tx + 14f, lineY + 6f), 3f);

                // Print series name and value
                string detail = string.Empty;
                if (valItem.Value.Length == 5)
                {
                    detail = $"{valItem.SeriesName}: [O:{valItem.Value[1]:F1}, C:{valItem.Value[2]:F1}]";
                }
                else
                {
                    detail = $"{valItem.SeriesName}: {valItem.Value[1]:F1}";
                }

                context.DrawText(detail, defaultFont, 10f, textPrimary, new Vector2(tx + 24f, lineY));
            }
        }
    }
}
