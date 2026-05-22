using System.Numerics;
using System.Runtime.InteropServices;

namespace ProGPU.Vector;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VectorVertex
{
    public Vector2 Position;
    public Vector4 Color;
    public Vector2 TexCoord;

    public VectorVertex(Vector2 position, Vector4 color, Vector2 texCoord)
    {
        Position = position;
        Color = color;
        TexCoord = texCoord;
    }
}
