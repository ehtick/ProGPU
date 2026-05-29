using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Text;
using ProGPU.Scene;

namespace ProGPU.WinUI.Charts.Renderers
{
    public static class AnnotationRenderer
    {
        public static void Draw(DrawingContext context, ChartGPUOptions options, LinearScale xScale, LinearScale yScale,
                                TtfFont defaultFont, Rect plotArea, string targetLayer)
        {
            if (options?.Annotations == null) return;

            foreach (var ann in options.Annotations)
            {
                if (!ann.Layer.Equals(targetLayer, StringComparison.OrdinalIgnoreCase)) continue;

                var colorStr = ann.Style?.Color ?? "#D83B01"; // Standard Orange Accent annotation color
                var colorVal = ChartUtils.ParseCssColor(colorStr);
                var strokeBrush = new SolidColorBrush(new Vector4(colorVal.X, colorVal.Y, colorVal.Z, (float)(ann.Style?.Opacity ?? 1.0)));
                var pen = new Pen(strokeBrush, (float)(ann.Style?.LineWidth ?? 1.0));

                if (ann is AnnotationLineX lx)
                {
                    float px = (float)xScale.Scale(lx.X);
                    if (px >= plotArea.X && px <= plotArea.X + plotArea.Width)
                    {
                        float minY = plotArea.Y;
                        float maxY = plotArea.Y + plotArea.Height;
                        if (lx.YRange != null && lx.YRange.Length == 2)
                        {
                            minY = (float)yScale.Scale(lx.YRange[1]);
                            maxY = (float)yScale.Scale(lx.YRange[0]);
                        }
                        context.DrawLine(pen, new Vector2(px, minY), new Vector2(px, maxY));

                        if (ann.Label != null)
                        {
                            string text = ann.Label.Text ?? lx.X.ToString($"F{ann.Label.Decimals}");
                            context.DrawText(text, defaultFont, 10f, strokeBrush, new Vector2(px + 4f, minY + 4f));
                        }
                    }
                }
                else if (ann is AnnotationLineY ly)
                {
                    float py = (float)yScale.Scale(ly.Y);
                    if (py >= plotArea.Y && py <= plotArea.Y + plotArea.Height)
                    {
                        float minX = plotArea.X;
                        float maxX = plotArea.X + plotArea.Width;
                        if (ly.XRange != null && ly.XRange.Length == 2)
                        {
                            minX = (float)xScale.Scale(ly.XRange[0]);
                            maxX = (float)xScale.Scale(ly.XRange[1]);
                        }
                        context.DrawLine(pen, new Vector2(minX, py), new Vector2(maxX, py));

                        if (ann.Label != null)
                        {
                            string text = ann.Label.Text ?? ly.Y.ToString($"F{ann.Label.Decimals}");
                            context.DrawText(text, defaultFont, 10f, strokeBrush, new Vector2(minX + 4f, py - 12f));
                        }
                    }
                }
                else if (ann is AnnotationPoint ap)
                {
                    float px = (float)xScale.Scale(ap.X);
                    float py = (float)yScale.Scale(ap.Y);

                    if (plotArea.Contains(new Vector2(px, py)))
                    {
                        float size = (float)(ap.Marker?.Size ?? 6.0);
                        context.FillCircle(strokeBrush, new Vector2(px, py), size / 2f);

                        if (ann.Label != null)
                        {
                            string text = ann.Label.Text ?? $"({ap.X:F1}, {ap.Y:F1})";
                            context.DrawText(text, defaultFont, 10f, strokeBrush, new Vector2(px + 8f, py - 6f));
                        }
                    }
                }
                else if (ann is AnnotationText at)
                {
                    float px = at.Position.Space.Equals("plot", StringComparison.OrdinalIgnoreCase) ?
                        plotArea.X + (float)at.Position.X : (float)xScale.Scale(at.Position.X);

                    float py = at.Position.Space.Equals("plot", StringComparison.OrdinalIgnoreCase) ?
                        plotArea.Y + (float)at.Position.Y : (float)yScale.Scale(at.Position.Y);

                    if (plotArea.Contains(new Vector2(px, py)))
                    {
                        context.DrawText(at.Text, defaultFont, 11f, strokeBrush, new Vector2(px, py));
                    }
                }
            }
        }
    }
}
