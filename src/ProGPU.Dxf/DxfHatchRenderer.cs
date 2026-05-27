using System;
using System.Collections.Generic;
using System.Numerics;
using netDxf.Entities;
using ProGPU.Vector;

namespace ProGPU.Dxf;

public class DxfHatchRenderer : IDxfEntityRenderer
{
    public void Render(EntityObject entity, DxfRenderContext context, Matrix4x4 transform)
    {
        if (entity is not Hatch hatch) return;
        if (hatch.BoundaryPaths == null) return;

        var combined = DxfDocumentRenderer.GetOcsMatrix(hatch.Normal) * transform;

        // 1. Retrieve or initialize the cache entry for this static Hatch
        if (!context.HatchCache.TryGetValue(hatch, out var entry))
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            bool hasPoints = false;
            foreach (var bp in hatch.BoundaryPaths)
            {
                var localPts = GetLocalBoundaryPoints(bp);
                foreach (var pt in localPts)
                {
                    var v3 = new Vector3(pt.X, pt.Y, 0f);
                    var v3Transformed = Vector3.Transform(v3, combined);
                    minX = Math.Min(minX, v3Transformed.X);
                    minY = Math.Min(minY, v3Transformed.Y);
                    maxX = Math.Max(maxX, v3Transformed.X);
                    maxY = Math.Max(maxY, v3Transformed.Y);
                    hasPoints = true;
                }
            }

            if (!hasPoints)
            {
                minX = minY = maxX = maxY = 0f;
            }

            entry = new HatchCacheEntry
            {
                MinModelBounds = new Vector2(minX, minY),
                MaxModelBounds = new Vector2(maxX, maxY)
            };
            context.HatchCache[hatch] = entry;
        }

        // 2. Perform Frustum Culling
        var minScreen = context.TransformToScreen(entry.MinModelBounds);
        var maxScreen = context.TransformToScreen(entry.MaxModelBounds);
        float sMinX = Math.Min(minScreen.X, maxScreen.X);
        float sMaxX = Math.Max(minScreen.X, maxScreen.X);
        float sMinY = Math.Min(minScreen.Y, maxScreen.Y);
        float sMaxY = Math.Max(minScreen.Y, maxScreen.Y);

        if (context.IsOffScreen(new Vector2(sMinX, sMinY), new Vector2(sMaxX, sMaxY)))
        {
            return; // Exit early, completely culled!
        }

        bool isSolid = hatch.Pattern == null || 
                       string.Equals(hatch.Pattern.Name, "SOLID", StringComparison.OrdinalIgnoreCase);

        var brush = context.GetCachedBrush(hatch);
        var brushColor = (brush is SolidColorBrush solidBrush) ? solidBrush.Color : new Vector4(1f, 1f, 1f, 1f);
        var pen = context.GetCachedPen(hatch, 1f);

        bool useGpuShader = false;
        if (!isSolid && hatch.Pattern != null && hatch.Pattern.LineDefinitions.Count == 1)
        {
            var lineFam = hatch.Pattern.LineDefinitions[0];
            if (lineFam.DashPattern == null || lineFam.DashPattern.Count == 0)
            {
                useGpuShader = true;
            }
        }

        // 3. Render Solid / Shader-based GPU Fill (with stable screen-space PathGeometry cache)
        if (isSolid || useGpuShader)
        {
            bool isZoomPanUnchanged = context.EnableGpuTransforms ||
                                      (Math.Abs(entry.CachedZoom - context.Zoom) < 1e-5f &&
                                       Vector2.Distance(entry.CachedPan, context.Pan) < 1e-4f);

            PathGeometry pathGeometry;
            if (isZoomPanUnchanged && entry.CachedPathGeometry != null)
            {
                pathGeometry = entry.CachedPathGeometry;
            }
            else
            {
                // Regenerate screen-space path geometry
                pathGeometry = new PathGeometry();
                foreach (var bp in hatch.BoundaryPaths)
                {
                    var pts = GetBoundaryPoints(bp, context, combined);
                    if (pts.Count >= 3)
                    {
                        var fig = new PathFigure(pts[0], isClosed: true);
                        for (int i = 1; i < pts.Count; i++)
                        {
                            fig.Segments.Add(new LineSegment(pts[i]));
                        }
                        pathGeometry.Figures.Add(fig);
                    }
                }

                entry.CachedPathGeometry = pathGeometry;
                entry.CachedZoom = context.Zoom;
                entry.CachedPan = context.Pan;
            }

            if (pathGeometry.Figures.Count > 0)
            {
                // Count segments across all figures
                int segmentCount = 0;
                foreach (var fig in pathGeometry.Figures)
                {
                    segmentCount += fig.Segments.Count;
                    if (fig.IsClosed)
                    {
                        segmentCount++;
                    }
                }

                Brush activeBrush;
                if (isSolid)
                {
                    activeBrush = brush;
                }
                else
                {
                    var lineFam = hatch.Pattern!.LineDefinitions[0];
                    float angleRad = (float)(lineFam.Angle * Math.PI / 180.0);
                    float spacing = (float)Math.Sqrt(lineFam.Delta.X * lineFam.Delta.X + lineFam.Delta.Y * lineFam.Delta.Y);
                    if (spacing < 1e-4f) spacing = 5.0f;
                    float lineThickness = 1.0f;
                    activeBrush = new HatchPatternBrush(angleRad, spacing, lineThickness, brushColor)
                    {
                        Opacity = brush.Opacity
                    };
                }

                if (segmentCount <= 120)
                {
                    context.DrawingContext.DrawHatch(activeBrush, pathGeometry);
                }
                else
                {
                    context.DrawingContext.DrawPath(activeBrush, null, pathGeometry);
                }
            }

            RenderBoundaryOutlines(hatch, context, transform);
            return;
        }

        // 4. Render Pattern Fallback with OCS-space segment pre-calculation
        if (entry.ModelCpuLines == null)
        {
            var cpuLines = new List<(Vector2 Start, Vector2 End)>();

            foreach (var bp in hatch.BoundaryPaths)
            {
                var localPts = GetLocalBoundaryPoints(bp);
                if (localPts.Count < 3)
                {
                    var pts = GetBoundaryPoints(bp, context, combined);
                    Matrix4x4.Invert(combined, out var inv);
                    foreach (var p in pts)
                    {
                        localPts.Add(Vector2.Transform(p, inv));
                    }
                }

                // Remove duplicate vertices
                for (int i = localPts.Count - 1; i > 0; i--)
                {
                    if (Vector2.Distance(localPts[i], localPts[i - 1]) < 1e-4f)
                    {
                        localPts.RemoveAt(i);
                    }
                }
                if (localPts.Count >= 3 && Vector2.Distance(localPts[localPts.Count - 1], localPts[0]) < 1e-4f)
                {
                    localPts.RemoveAt(localPts.Count - 1);
                }

                if (localPts.Count < 3) continue;

                foreach (var lineFam in hatch.Pattern!.LineDefinitions)
                {
                    double angleRad = lineFam.Angle * Math.PI / 180.0;
                    var d = new Vector2((float)Math.Cos(angleRad), (float)Math.Sin(angleRad));
                    var n = new Vector2(-d.Y, d.X); // Perpendicular

                    var basePt = new Vector2((float)lineFam.Origin.X, (float)lineFam.Origin.Y);
                    var offset = new Vector2((float)lineFam.Delta.X, (float)lineFam.Delta.Y);

                    float denomProj = Vector2.Dot(offset, n);
                    if (Math.Abs(denomProj) < 1e-5f) denomProj = 5.0f;

                    float minK = float.MaxValue;
                    float maxK = float.MinValue;

                    foreach (var pt in localPts)
                    {
                        float kVal = Vector2.Dot(pt - basePt, n) / denomProj;
                        minK = Math.Min(minK, kVal);
                        maxK = Math.Max(maxK, kVal);
                    }

                    int startK = (int)Math.Floor(minK) - 1;
                    int endK = (int)Math.Ceiling(maxK) + 1;

                    for (int k = startK; k <= endK; k++)
                    {
                        var pBase = basePt + k * offset;
                        var intersections = new List<float>();

                        for (int i = 0; i < localPts.Count; i++)
                        {
                            var A = localPts[i];
                            var B = localPts[(i + 1) % localPts.Count];
                            var v = B - A;

                            float det = d.X * (-v.Y) - d.Y * (-v.X);
                            if (Math.Abs(det) < 1e-6f) continue;

                            float t = ((A.X - pBase.X) * (-v.Y) - (A.Y - pBase.Y) * (-v.X)) / det;
                            float u = (d.X * (A.Y - pBase.Y) - d.Y * (A.X - pBase.X)) / det;

                            if (u >= 0f && u <= 1f)
                            {
                                intersections.Add(t);
                            }
                        }

                        if (intersections.Count < 2) continue;

                        intersections.Sort();

                        for (int i = 0; i < intersections.Count - 1; i += 2)
                        {
                            float t0 = intersections[i];
                            float t1 = intersections[i + 1];

                            if (lineFam.DashPattern == null || lineFam.DashPattern.Count == 0)
                            {
                                cpuLines.Add((pBase + t0 * d, pBase + t1 * d));
                            }
                            else
                            {
                                CollectDashedSegments(pBase, d, t0, t1, lineFam.DashPattern, cpuLines);
                            }
                        }
                    }
                }
            }

            entry.ModelCpuLines = cpuLines;
        }

        // Draw cached CPU lines transformed to screen space
        foreach (var lineSeg in entry.ModelCpuLines)
        {
            var startPt = context.Transform(lineSeg.Start, combined);
            var endPt = context.Transform(lineSeg.End, combined);
            context.DrawingContext.DrawLine(pen, startPt, endPt);
        }

        RenderBoundaryOutlines(hatch, context, transform);
    }

    private void CollectDashedSegments(Vector2 pBase, Vector2 d, float t0, float t1, IList<double> dashes, List<(Vector2 Start, Vector2 End)> cpuLines)
    {
        float totalLength = 0f;
        foreach (var dash in dashes)
        {
            totalLength += (float)Math.Abs(dash);
        }
        if (totalLength < 1e-4f) return;

        float segmentStart = t0;
        float segmentEnd = t1;
        float currentT = segmentStart;
        
        while (currentT < segmentEnd)
        {
            float relativeT = currentT >= 0f ? (currentT % totalLength) : (totalLength + (currentT % totalLength));
            if (relativeT >= totalLength) relativeT -= totalLength;

            float accum = 0f;
            int dashIdx = 0;
            float currentDashLen = 0f;
            bool isPenDown = true;

            for (int i = 0; i < dashes.Count; i++)
            {
                float len = (float)Math.Abs(dashes[i]);
                if (relativeT >= accum && relativeT < accum + len)
                {
                    dashIdx = i;
                    currentDashLen = len;
                    isPenDown = dashes[i] >= 0.0;
                    break;
                }
                accum += len;
            }

            float remainingInDash = (accum + currentDashLen) - relativeT;
            float step = Math.Min(remainingInDash, segmentEnd - currentT);

            if (isPenDown)
            {
                cpuLines.Add((pBase + currentT * d, pBase + (currentT + step) * d));
            }

            currentT += step;
        }
    }

    private void RenderBoundaryOutlines(Hatch hatch, DxfRenderContext context, Matrix4x4 transform)
    {
        foreach (var bp in hatch.BoundaryPaths)
        {
            if (bp.Entities == null) continue;
            foreach (var childEntity in bp.Entities)
            {
                if (childEntity.Layer == null)
                {
                    childEntity.Layer = hatch.Layer;
                }
                DxfDocumentRenderer.RenderEntity(childEntity, context, transform);
            }
        }
    }

    private List<Vector2> GetLocalBoundaryPoints(HatchBoundaryPath bp)
    {
        var points = new List<Vector2>();
        if (bp.Entities == null) return points;

        foreach (var ent in bp.Entities)
        {
            if (ent is Line line)
            {
                points.Add(new Vector2((float)line.StartPoint.X, (float)line.StartPoint.Y));
            }
            else if (ent is Arc arc)
            {
                var center = new Vector2((float)arc.Center.X, (float)arc.Center.Y);
                float radius = (float)arc.Radius;
                float startAng = (float)(arc.StartAngle * Math.PI / 180.0);
                float endAng = (float)(arc.EndAngle * Math.PI / 180.0);
                if (endAng < startAng) endAng += 2f * MathF.PI;

                int steps = 16;
                for (int i = 0; i <= steps; i++)
                {
                    float angle = startAng + (endAng - startAng) * (i / (float)steps);
                    points.Add(center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius));
                }
            }
            else if (ent is LwPolyline lw)
            {
                foreach (var vertex in lw.Vertexes)
                {
                    points.Add(new Vector2((float)vertex.Position.X, (float)vertex.Position.Y));
                }
            }
            else if (ent is Polyline poly)
            {
                foreach (var vertex in poly.Vertexes)
                {
                    points.Add(new Vector2((float)vertex.Position.X, (float)vertex.Position.Y));
                }
            }
        }

        for (int i = points.Count - 1; i > 0; i--)
        {
            if (Vector2.Distance(points[i], points[i - 1]) < 1e-3f)
            {
                points.RemoveAt(i);
            }
        }
        return points;
    }

    private List<Vector2> GetBoundaryPoints(HatchBoundaryPath bp, DxfRenderContext context, Matrix4x4 combined)
    {
        var points = new List<Vector2>();
        if (bp.Entities == null) return points;

        foreach (var ent in bp.Entities)
        {
            if (ent is Line line)
            {
                var p1 = context.Transform(new Vector2((float)line.StartPoint.X, (float)line.StartPoint.Y), combined);
                points.Add(p1);
            }
            else if (ent is Arc arc)
            {
                var center = new Vector2((float)arc.Center.X, (float)arc.Center.Y);
                float radius = (float)arc.Radius;
                float startAng = (float)(arc.StartAngle * Math.PI / 180.0);
                float endAng = (float)(arc.EndAngle * Math.PI / 180.0);
                if (endAng < startAng) endAng += 2f * MathF.PI;

                int steps = 16;
                for (int i = 0; i <= steps; i++)
                {
                    float angle = startAng + (endAng - startAng) * (i / (float)steps);
                    var p = context.Transform(center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius), combined);
                    points.Add(p);
                }
            }
            else if (ent is LwPolyline lw)
            {
                foreach (var vertex in lw.Vertexes)
                {
                    var p = context.Transform(new Vector2((float)vertex.Position.X, (float)vertex.Position.Y), combined);
                    points.Add(p);
                }
            }
            else if (ent is Polyline poly)
            {
                foreach (var vertex in poly.Vertexes)
                {
                    var p = context.Transform(new Vector2((float)vertex.Position.X, (float)vertex.Position.Y), combined);
                    points.Add(p);
                }
            }
        }

        for (int i = points.Count - 1; i > 0; i--)
        {
            if (Vector2.Distance(points[i], points[i - 1]) < 1e-3f)
            {
                points.RemoveAt(i);
            }
        }
        return points;
    }
}
