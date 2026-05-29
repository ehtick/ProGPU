using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Text;
using ProGPU.Scene;
using Microsoft.UI.Xaml;

namespace ProGPU.WinUI.Charts.Renderers
{
    public static class AxisGridRenderer
    {
        private static int ComputeMaxFractionDigitsFromStep(double tickStep, int cap = 8)
        {
            double stepAbs = Math.Abs(tickStep);
            if (!double.IsFinite(stepAbs) || stepAbs == 0) return 0;

            for (int d = 0; d <= cap; d++)
            {
                double scaled = stepAbs * Math.Pow(10, d);
                double rounded = Math.Round(scaled);
                double err = Math.Abs(scaled - rounded);
                double tol = 1e-9 * Math.Max(1.0, Math.Abs(scaled));
                if (err <= tol) return d;
            }

            return Math.Max(0, Math.Min(cap, 1 - (int)Math.Floor(Math.Log10(stepAbs)) + 1));
        }

        private static string FormatTickValue(double v, string formatSpecifier)
        {
            if (!double.IsFinite(v)) return string.Empty;
            double normalized = Math.Abs(v) < 1e-12 ? 0.0 : v;
            return normalized.ToString(formatSpecifier, CultureInfo.InvariantCulture);
        }

        public static void DrawGridlines(DrawingContext context, ChartGPUOptions options, LinearScale xScale, LinearScale yScale, Rect plotArea)
        {
            if (options == null || !options.GridLines.Show) return;

            var gridColor = ThemeManager.GetBrush("ControlBorder");
            float opacity = (float)options.GridLines.Opacity;
            var hatchPen = new Pen(gridColor, 0.5f);

            // Horizontal Grid Lines (aligned with primary Y scale ticks)
            if (options.GridLines.Horizontal.Show)
            {
                int count = options.GridLines.Horizontal.Count ?? 5;
                for (int i = 0; i < count; i++)
                {
                    double t = count == 1 ? 0.5 : (double)i / (count - 1);
                    double yVal = yScale.DomainMin + t * (yScale.DomainMax - yScale.DomainMin);
                    float y = (float)yScale.Scale(yVal);

                    if (y >= plotArea.Y && y <= plotArea.Y + plotArea.Height)
                    {
                        context.PushOpacity(opacity);
                        context.DrawLine(hatchPen, new Vector2(plotArea.X, y), new Vector2(plotArea.X + plotArea.Width, y));
                        context.PopOpacity();
                    }
                }
            }

            // Vertical Grid Lines (aligned with X scale ticks)
            if (options.GridLines.Vertical.Show)
            {
                int count = options.GridLines.Vertical.Count ?? 5;
                for (int i = 0; i < count; i++)
                {
                    double t = count == 1 ? 0.5 : (double)i / (count - 1);
                    double xVal = xScale.DomainMin + t * (xScale.DomainMax - xScale.DomainMin);
                    float x = (float)xScale.Scale(xVal);

                    if (x >= plotArea.X && x <= plotArea.X + plotArea.Width)
                    {
                        context.PushOpacity(opacity);
                        context.DrawLine(hatchPen, new Vector2(x, plotArea.Y), new Vector2(x, plotArea.Y + plotArea.Height));
                        context.PopOpacity();
                    }
                }
            }
        }

        public static void DrawAxes(DrawingContext context, ChartGPUOptions options, LinearScale xScale,
                                    LinearScale yScaleLeft, LinearScale? yScaleRight, TtfFont defaultFont, Rect plotArea)
        {
            var textBrush = ThemeManager.GetBrush("TextSecondary");
            var tickColor = ThemeManager.GetBrush("ControlBorder");
            var tickPen = new Pen(tickColor, 1f);

            // 1. Bottom X-Axis Labels & Ticks
            int xTicks = options?.GridLines?.Vertical?.Count ?? 5;
            double xStep = (xScale.DomainMax - xScale.DomainMin) / Math.Max(1, xTicks - 1);
            int xDecimals = ComputeMaxFractionDigitsFromStep(xStep);
            string xFormat = "F" + xDecimals;

            for (int i = 0; i < xTicks; i++)
            {
                double t = xTicks == 1 ? 0.5 : (double)i / (xTicks - 1);
                double xVal = xScale.DomainMin + t * (xScale.DomainMax - xScale.DomainMin);
                float x = (float)xScale.Scale(xVal);

                if (x < plotArea.X - 2.0f || x > plotArea.X + plotArea.Width + 2.0f) continue;

                // Draw bottom tick line
                context.DrawLine(tickPen, new Vector2(x, plotArea.Y + plotArea.Height), new Vector2(x, plotArea.Y + plotArea.Height + 5f));

                string label = string.Empty;
                if (options?.XAxis?.TickFormatter != null)
                {
                    label = options.XAxis.TickFormatter(xVal) ?? string.Empty;
                }
                else if (options?.XAxis?.Type == AxisType.Time)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)xVal).LocalDateTime;
                    label = dt.ToString("HH:mm:ss");
                }
                else
                {
                    label = FormatTickValue(xVal, xFormat);
                }

                float snappedX = (float)DpiSnapper.Snap(x - 20f, 1.0);
                float snappedY = (float)DpiSnapper.Snap(plotArea.Y + plotArea.Height + 8f, 1.0);

                context.DrawText(label, defaultFont, 10f, textBrush, new Vector2(snappedX, snappedY));
            }

            // 2. Left Y-Axis Labels & Ticks
            int leftTicks = options?.GridLines?.Horizontal?.Count ?? 5;
            double yStepLeft = (yScaleLeft.DomainMax - yScaleLeft.DomainMin) / Math.Max(1, leftTicks - 1);
            int yDecimalsLeft = ComputeMaxFractionDigitsFromStep(yStepLeft);
            string yFormatLeft = "F" + yDecimalsLeft;

            var leftAxisConfig = (options?.YAxes != null && options.YAxes.Count > 0) ? options.YAxes[0] : options?.YAxis;
            for (int i = 0; i < leftTicks; i++)
            {
                double t = leftTicks == 1 ? 0.5 : (double)i / (leftTicks - 1);
                double yVal = yScaleLeft.DomainMin + t * (yScaleLeft.DomainMax - yScaleLeft.DomainMin);
                float y = (float)yScaleLeft.Scale(yVal);

                if (y < plotArea.Y - 2.0f || y > plotArea.Y + plotArea.Height + 2.0f) continue;

                // Draw left tick line
                context.DrawLine(tickPen, new Vector2(plotArea.X - 5f, y), new Vector2(plotArea.X, y));

                string label = string.Empty;
                if (leftAxisConfig?.TickFormatter != null)
                {
                    label = leftAxisConfig.TickFormatter(yVal) ?? string.Empty;
                }
                else
                {
                    label = FormatTickValue(yVal, yFormatLeft);
                }

                float snappedX = (float)DpiSnapper.Snap(plotArea.X - 45f, 1.0);
                float snappedY = (float)DpiSnapper.Snap(y - 5f, 1.0);
                context.DrawText(label, defaultFont, 10f, textBrush, new Vector2(snappedX, snappedY));
            }

            // 3. Right Y-Axis Labels & Ticks (For dual Y-axes layouts)
            if (yScaleRight != null && options?.YAxes != null && options.YAxes.Count > 1)
            {
                int rightTicks = options?.GridLines?.Horizontal?.Count ?? 5;
                double yStepRight = (yScaleRight.DomainMax - yScaleRight.DomainMin) / Math.Max(1, rightTicks - 1);
                int yDecimalsRight = ComputeMaxFractionDigitsFromStep(yStepRight);
                string yFormatRight = "F" + yDecimalsRight;

                var rightAxisConfig = options?.YAxes?[1];
                for (int i = 0; i < rightTicks; i++)
                {
                    double t = rightTicks == 1 ? 0.5 : (double)i / (rightTicks - 1);
                    double yVal = yScaleRight.DomainMin + t * (yScaleRight.DomainMax - yScaleRight.DomainMin);
                    float y = (float)yScaleRight.Scale(yVal);

                    if (y < plotArea.Y - 2.0f || y > plotArea.Y + plotArea.Height + 2.0f) continue;

                    // Draw right tick line
                    context.DrawLine(tickPen, new Vector2(plotArea.X + plotArea.Width, y), new Vector2(plotArea.X + plotArea.Width + 5f, y));

                    string label = string.Empty;
                    if (rightAxisConfig?.TickFormatter != null)
                    {
                        label = rightAxisConfig.TickFormatter(yVal) ?? string.Empty;
                    }
                    else
                    {
                        label = FormatTickValue(yVal, yFormatRight);
                    }

                    float snappedX = (float)DpiSnapper.Snap(plotArea.X + plotArea.Width + 8f, 1.0);
                    float snappedY = (float)DpiSnapper.Snap(y - 5f, 1.0);
                    context.DrawText(label, defaultFont, 10f, textBrush, new Vector2(snappedX, snappedY));
                }
            }
        }
    }
}
