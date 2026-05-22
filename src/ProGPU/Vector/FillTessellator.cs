using System.Collections.Generic;
using System.Numerics;

namespace ProGPU.Vector;

public static class FillTessellator
{
    public static void TessellateFill(
        List<Vector2> path,
        Vector4 color,
        List<VectorVertex> outVertices,
        List<ushort> outIndices)
    {
        if (path.Count < 3) return;

        int vertexStart = outVertices.Count;
        int count = path.Count;

        // Strip out duplicate final vertex if closed path contains it
        int pointsToUse = count;
        if (count > 2 && path[0] == path[^1])
        {
            pointsToUse = count - 1;
        }

        if (pointsToUse < 3) return;

        // Add all points as vertices
        for (int i = 0; i < pointsToUse; i++)
        {
            outVertices.Add(new VectorVertex(path[i], color, Vector2.Zero));
        }

        // Generate triangle fan indices (0, i, i + 1)
        for (int i = 1; i < pointsToUse - 1; i++)
        {
            outIndices.Add((ushort)vertexStart);
            outIndices.Add((ushort)(vertexStart + i));
            outIndices.Add((ushort)(vertexStart + i + 1));
        }
    }
}
