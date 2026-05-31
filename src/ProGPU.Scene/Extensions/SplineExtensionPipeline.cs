using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Vector;

namespace ProGPU.Scene.Extensions
{
    public class SplineExtensionPipeline : ICompositorExtension
    {
        public void Compile(
            Compositor compositor,
            IRenderDataProvider? provider,
            Matrix4x4 transform,
            ref RenderCommand cmd)
        {
            if (cmd.Pen == null) return;

            ReadOnlySpan<Vector2> controlPoints = provider != null ? 
                provider.GetPoints(cmd.PointBufferOffset, cmd.PointBufferCount) : 
                cmd.PolylinePoints;

            ReadOnlySpan<double> knots = provider != null ? 
                provider.GetDoubles(cmd.DoubleBufferOffset, cmd.DoubleBufferCount) : 
                cmd.SplineKnots;

            ReadOnlySpan<double> weights = provider != null && cmd.WeightBufferCount > 0 ? 
                provider.GetDoubles(cmd.WeightBufferOffset, cmd.WeightBufferCount) : 
                cmd.SplineWeights;

            if (controlPoints.Length < 2 || knots.IsEmpty) return;

            int degree = cmd.SplineDegree;

            if (knots.Length < controlPoints.Length + degree + 1)
            {
                // Fallback: draw control points as a polyline using the compositor's internal helper
                var fallbackCmd = new RenderCommand
                {
                    Pen = cmd.Pen,
                    PointBufferOffset = cmd.PointBufferOffset,
                    PointBufferCount = cmd.PointBufferCount,
                    PolylinePoints = cmd.PolylinePoints,
                    IsClosed = false
                };
                compositor.CompilePolyline(provider, fallbackCmd, transform);
                return;
            }

            double startKnot = knots[degree];
            double endKnot = knots[knots.Length - degree - 1];

            // Calculate screen-space bounding box of control points to determine dynamic LOD
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var cp in controlPoints)
            {
                var sp = Vector2.Transform(cp, transform);
                minX = Math.Min(minX, sp.X);
                minY = Math.Min(minY, sp.Y);
                maxX = Math.Max(maxX, sp.X);
                maxY = Math.Max(maxY, sp.Y);
            }

            var minPt = new Vector2(minX, minY);
            var maxPt = new Vector2(maxX, maxY);

            float sizeOnScreen = Vector2.Distance(minPt, maxPt);
            if (sizeOnScreen < 2f) return; // Too small to see

            // Determine dynamic segment count (LOD) based on screen size
            int numPoints = 100;
            if (sizeOnScreen < 20f) numPoints = 10;
            else if (sizeOnScreen < 80f) numPoints = 25;
            else if (sizeOnScreen < 250f) numPoints = 50;
            else numPoints = 100;

            // Pre-evaluate B-spline points directly to screen space
            Span<Vector2> transformed = numPoints + 1 <= 512 ? stackalloc Vector2[numPoints + 1] : new Vector2[numPoints + 1];
            double delta = (endKnot - startKnot) / numPoints;
            for (int i = 0; i <= numPoints; i++)
            {
                double u = startKnot + i * delta;
                transformed[i] = EvaluateBSpline(degree, controlPoints, knots, weights, u, transform);
            }

            int startIndex = compositor.VectorVertices.Count;
            float penBrushIdx = compositor.RegisterBrush(cmd.Pen.Brush);
            var penSolidColor = (cmd.Pen.Brush is SolidColorBrush solid) ? solid.Color : new Vector4(1f, 1f, 1f, 1f);
            float thickness = cmd.Pen.Thickness;

            // Compile segments into the vertex/index buffer in exactly one batch operation
            int totalVerticesToAdd = numPoints * 4;
            int totalIndicesToAdd = numPoints * 6;

            int originalVertexCount = compositor.VectorVertices.Count;
            CollectionsMarshal.SetCount(compositor.VectorVertices, originalVertexCount + totalVerticesToAdd);
            var vertexSpan = CollectionsMarshal.AsSpan(compositor.VectorVertices).Slice(originalVertexCount, totalVerticesToAdd);

            int originalIndexCount = compositor.VectorIndices.Count;
            CollectionsMarshal.SetCount(compositor.VectorIndices, originalIndexCount + totalIndicesToAdd);
            var indexSpan = CollectionsMarshal.AsSpan(compositor.VectorIndices).Slice(originalIndexCount, totalIndicesToAdd);

            for (int i = 0; i < numPoints; i++)
            {
                var p0_pos = transformed[i];
                var p1_pos = transformed[i + 1];

                uint idxStart = (uint)(originalVertexCount + i * 4);

                int vIdx = i * 4;
                vertexSpan[vIdx] = new VectorVertex(p0_pos, penSolidColor, p0_pos, penBrushIdx, p1_pos, 1f, thickness, 3f);
                vertexSpan[vIdx + 1] = new VectorVertex(p0_pos, penSolidColor, p0_pos, penBrushIdx, p1_pos, -1f, thickness, 3f);
                vertexSpan[vIdx + 2] = new VectorVertex(p1_pos, penSolidColor, p0_pos, penBrushIdx, p1_pos, 2f, thickness, 3f);
                vertexSpan[vIdx + 3] = new VectorVertex(p1_pos, penSolidColor, p0_pos, penBrushIdx, p1_pos, -2f, thickness, 3f);

                int iIdx = i * 6;
                indexSpan[iIdx] = idxStart;
                indexSpan[iIdx + 1] = idxStart + 1;
                indexSpan[iIdx + 2] = idxStart + 2;
                indexSpan[iIdx + 3] = idxStart + 1;
                indexSpan[iIdx + 4] = idxStart + 3;
                indexSpan[iIdx + 5] = idxStart + 2;
            }

            if (compositor.ActiveClipRect.HasValue)
            {
                var vertices = CollectionsMarshal.AsSpan(compositor.VectorVertices);
                for (int i = startIndex; i < vertices.Length; i++)
                {
                    var v = vertices[i];
                    v.Position = compositor.ClampToClip(v.Position);
                    vertices[i] = v;
                }
            }
        }

        private Vector2 EvaluateBSpline(
            int degree,
            ReadOnlySpan<Vector2> controlPoints,
            ReadOnlySpan<double> knots,
            ReadOnlySpan<double> weights,
            double u,
            Matrix4x4 transform)
        {
            int k = -1;
            if (u < knots[degree]) u = knots[degree];
            if (u > knots[knots.Length - degree - 1]) u = knots[knots.Length - degree - 1];

            for (int i = degree; i < knots.Length - 1; i++)
            {
                if (u >= knots[i] && u <= knots[i + 1])
                {
                    k = i;
                    break;
                }
            }

            if (k == -1)
            {
                k = knots.Length - degree - 2;
            }

            Span<Vector3> d = stackalloc Vector3[degree + 1];
            for (int j = 0; j <= degree; j++)
            {
                int idx = k - degree + j;
                if (idx >= 0 && idx < controlPoints.Length)
                {
                    float w = 1f;
                    if (!weights.IsEmpty && idx < weights.Length)
                    {
                        w = (float)weights[idx];
                    }
                    d[j] = new Vector3(controlPoints[idx].X * w, controlPoints[idx].Y * w, w);
                }
                else
                {
                    d[j] = Vector3.Zero;
                }
            }

            for (int r = 1; r <= degree; r++)
            {
                for (int j = degree; j >= r; j--)
                {
                    int i = k - degree + j;
                    double denom = knots[i + degree + 1 - r] - knots[i];
                    float alpha = (denom > 1e-9) ? (float)((u - knots[i]) / denom) : 0f;
                    d[j] = (1f - alpha) * d[j - 1] + alpha * d[j];
                }
            }

            Vector3 finalH = d[degree];
            Vector2 cartesianPt = (Math.Abs(finalH.Z) > 1e-9f) 
                ? new Vector2(finalH.X / finalH.Z, finalH.Y / finalH.Z) 
                : new Vector2(finalH.X, finalH.Y);

            return Vector2.Transform(cartesianPt, transform);
        }

        public unsafe void Render(
            Compositor compositor,
            void* renderPassEncoder,
            bool isOffscreen,
            in Compositor.CompositorDrawCall dc)
        {
            // Pure compile-time vector primitive.
        }
    }
}
