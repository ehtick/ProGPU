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
                var fallbackCmd = new RenderCommand
                {
                    Pen = cmd.Pen,
                    PointBufferOffset = cmd.PointBufferOffset,
                    PointBufferCount = cmd.PointBufferCount,
                    PolylinePoints = cmd.PolylinePoints,
                    IsClosed = cmd.IsClosed
                };
                compositor.CompilePolyline(provider, fallbackCmd, transform);
                return;
            }

            int numPoints = SplineGeometry.GetScreenSegmentCount(controlPoints, transform);
            if (numPoints == 0) return;

            Span<Vector2> transformed = numPoints + 1 <= 512 ? stackalloc Vector2[numPoints + 1] : new Vector2[numPoints + 1];
            if (!SplineGeometry.TryEvaluatePoints(degree, controlPoints, knots, weights, transform, transformed))
            {
                return;
            }

            int startIndex = compositor.VectorVertices.Count;
            float penBrushIdx = compositor.RegisterBrush(cmd.Pen.Brush);
            var penSolidColor = (cmd.Pen.Brush is SolidColorBrush solid) ? solid.Color : new Vector4(1f, 1f, 1f, 1f);
            float thickness = cmd.Pen.Thickness;
            bool closeSpline = cmd.IsClosed &&
                Vector2.DistanceSquared(transformed[0], transformed[transformed.Length - 1]) > 1e-8f;
            int segmentCount = numPoints + (closeSpline ? 1 : 0);

            int totalVerticesToAdd = segmentCount * 4;
            int totalIndicesToAdd = segmentCount * 6;

            int originalVertexCount = compositor.VectorVertices.Count;
            CollectionsMarshal.SetCount(compositor.VectorVertices, originalVertexCount + totalVerticesToAdd);
            var vertexSpan = CollectionsMarshal.AsSpan(compositor.VectorVertices).Slice(originalVertexCount, totalVerticesToAdd);

            int originalIndexCount = compositor.VectorIndices.Count;
            CollectionsMarshal.SetCount(compositor.VectorIndices, originalIndexCount + totalIndicesToAdd);
            var indexSpan = CollectionsMarshal.AsSpan(compositor.VectorIndices).Slice(originalIndexCount, totalIndicesToAdd);

            for (int i = 0; i < segmentCount; i++)
            {
                var p0_pos = transformed[i];
                var p1_pos = i == numPoints ? transformed[0] : transformed[i + 1];

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
