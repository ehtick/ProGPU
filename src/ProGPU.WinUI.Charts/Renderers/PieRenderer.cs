using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Scene;

namespace ProGPU.WinUI.Charts.Renderers
{
    public static class PieRenderer
    {
        public static void Draw(DrawingContext context, PieSeriesConfig psc, Rect plotArea,
                                IReadOnlyList<string> defaultPalette)
        {
            if (psc.Data.Count == 0) return;

            // Pie Center and radius resolving
            float cx = plotArea.X + plotArea.Width / 2f;
            float cy = plotArea.Y + plotArea.Height / 2f;

            if (psc.Center.HasValue)
            {
                cx = plotArea.X + (float)ChartUtils.ParsePercent(psc.Center.Value.X, plotArea.Width);
                cy = plotArea.Y + (float)ChartUtils.ParsePercent(psc.Center.Value.Y, plotArea.Height);
            }

            float maxRadius = Math.Min(plotArea.Width, plotArea.Height) / 2f;
            float innerRadius = 0.0f;
            float outerRadius = maxRadius * 0.7f;

            if (psc.Radius.HasValue)
            {
                if (psc.Radius.Value.IsTuple)
                {
                    innerRadius = (float)ChartUtils.ParsePercent(psc.Radius.Value.Inner, maxRadius);
                    outerRadius = (float)ChartUtils.ParsePercent(psc.Radius.Value.Outer, maxRadius);
                }
                else
                {
                    outerRadius = (float)ChartUtils.ParsePercent(psc.Radius.Value.Outer, maxRadius);
                }
            }

            // Slice computation
            double total = 0.0;
            foreach (var item in psc.Data)
            {
                if (item.Visible) total += item.Value;
            }

            if (total <= 0.0) return;

            double startAngle = psc.StartAngle; // In degrees
            var palette = defaultPalette ?? new string[] { "#0078D4", "#107C41", "#D83B01", "#A8003F", "#5C2D91" };

            for (int i = 0; i < psc.Data.Count; i++)
            {
                var item = psc.Data[i];
                if (!item.Visible) continue;

                double sweep = (item.Value / total) * 360.0;
                double endAngle = startAngle + sweep;

                var sliceColorStr = item.Color ?? palette[(i % palette.Count)];
                var sliceColor = ChartUtils.ParseCssColor(sliceColorStr);
                var sliceBrush = new SolidColorBrush(sliceColor);

                // High fidelity vector approximate of slice circle sector using polygons (32 points)
                int segmentsCount = 32;
                var polyPoints = new List<Vector2>(segmentsCount * 2 + 2);

                double radStart = startAngle * Math.PI / 180.0;
                double radEnd = endAngle * Math.PI / 180.0;

                // Outer Arc
                for (int s = 0; s <= segmentsCount; s++)
                {
                    double theta = radStart + (radEnd - radStart) * s / segmentsCount;
                    float px = cx + (float)Math.Cos(theta) * outerRadius;
                    float py = cy + (float)Math.Sin(theta) * outerRadius;
                    polyPoints.Add(new Vector2(px, py));
                }

                // Inner Arc (reversed order for clean winding)
                for (int s = segmentsCount; s >= 0; s--)
                {
                    double theta = radStart + (radEnd - radStart) * s / segmentsCount;
                    float px = cx + (float)Math.Cos(theta) * innerRadius;
                    float py = cy + (float)Math.Sin(theta) * innerRadius;
                    polyPoints.Add(new Vector2(px, py));
                }

                // Closed segment drawing
                if (innerRadius > 0.0f)
                {
                    // Draw Donut slice as polyquads
                    for (int s = 0; s < segmentsCount; s++)
                    {
                        var p1 = polyPoints[s];
                        var p2 = polyPoints[s + 1];
                        var p3 = polyPoints[polyPoints.Count - 1 - (s + 1)];
                        var p4 = polyPoints[polyPoints.Count - 1 - s];
                        context.FillQuad(sliceBrush, p1, p2, p3, p4);
                    }
                }
                else
                {
                    // Simple filled pie segment triangles
                    for (int s = 0; s < segmentsCount; s++)
                    {
                        var p1 = polyPoints[s];
                        var p2 = polyPoints[s + 1];
                        var centerPt = new Vector2(cx, cy);
                        context.FillTriangle(sliceBrush, centerPt, p1, p2);
                    }
                }

                startAngle = endAngle;
            }
        }
    }
}
