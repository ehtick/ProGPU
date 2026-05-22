using System;
using System.Collections.Generic;
using System.Numerics;

namespace ProGPU.Vector;

public static class StrokeTessellator
{
    private static Vector2 NormalizeSafe(Vector2 v)
    {
        float len = v.Length();
        return len > 1e-6f ? v / len : Vector2.Zero;
    }

    public static void TessellateStroke(
        List<Vector2> path,
        float thickness,
        Vector4 color,
        bool isClosed,
        List<VectorVertex> outVertices,
        List<ushort> outIndices)
    {
        if (path.Count < 2) return;

        int count = path.Count;
        int vertexStart = outVertices.Count;

        // Generate left and right border vertices for each point along the path
        for (int i = 0; i < count; i++)
        {
            Vector2 curr = path[i];
            Vector2 prev, next;
            Vector2 dir1, dir2;

            if (isClosed)
            {
                prev = path[(i - 1 + count) % count];
                next = path[(i + 1) % count];
                dir1 = NormalizeSafe(curr - prev);
                dir2 = NormalizeSafe(next - curr);
            }
            else
            {
                if (i == 0)
                {
                    next = path[1];
                    Vector2 dir = NormalizeSafe(next - curr);
                    Vector2 normal = new Vector2(-dir.Y, dir.X);
                    
                    outVertices.Add(new VectorVertex(curr + normal * (thickness / 2f), color, Vector2.Zero));
                    outVertices.Add(new VectorVertex(curr - normal * (thickness / 2f), color, Vector2.Zero));
                    continue;
                }
                else if (i == count - 1)
                {
                    prev = path[count - 2];
                    Vector2 dir = NormalizeSafe(curr - prev);
                    Vector2 normal = new Vector2(-dir.Y, dir.X);
                    
                    outVertices.Add(new VectorVertex(curr + normal * (thickness / 2f), color, Vector2.Zero));
                    outVertices.Add(new VectorVertex(curr - normal * (thickness / 2f), color, Vector2.Zero));
                    continue;
                }
                else
                {
                    prev = path[i - 1];
                    next = path[i + 1];
                    dir1 = NormalizeSafe(curr - prev);
                    dir2 = NormalizeSafe(next - curr);
                }
            }

            Vector2 n1 = new Vector2(-dir1.Y, dir1.X);
            Vector2 n2 = new Vector2(-dir2.Y, dir2.X);
            Vector2 miterN = NormalizeSafe(n1 + n2);
            
            float dot = Vector2.Dot(miterN, n1);
            float miterScale = dot > 1e-4f ? 1.0f / dot : 1.0f;

            // Clamp miter to prevent spikes
            if (miterScale > 4.0f)
            {
                miterScale = 4.0f;
            }

            Vector2 offset = miterN * (thickness / 2f) * miterScale;

            outVertices.Add(new VectorVertex(curr + offset, color, Vector2.Zero));
            outVertices.Add(new VectorVertex(curr - offset, color, Vector2.Zero));
        }

        // Connect the left/right vertices into quads (pairs of triangles)
        int segmentsCount = isClosed ? count : count - 1;
        for (int i = 0; i < segmentsCount; i++)
        {
            int iNext = (i + 1) % count;

            ushort v0_left = (ushort)(vertexStart + 2 * i);
            ushort v0_right = (ushort)(vertexStart + 2 * i + 1);
            ushort v1_left = (ushort)(vertexStart + 2 * iNext);
            ushort v1_right = (ushort)(vertexStart + 2 * iNext + 1);

            // Triangle 1
            outIndices.Add(v0_left);
            outIndices.Add(v0_right);
            outIndices.Add(v1_left);

            // Triangle 2
            outIndices.Add(v0_right);
            outIndices.Add(v1_right);
            outIndices.Add(v1_left);
        }
    }
}
