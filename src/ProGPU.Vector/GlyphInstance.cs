using System.Numerics;
using System.Runtime.InteropServices;

namespace ProGPU.Vector;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GlyphInstance
{
    public Vector2 SnappedLogicalPos;       // 8 bytes (Location 0) - screen space or logical pos
    public Vector2 BasisX;                  // 8 bytes (Location 1) - X basis of activeTransform
    public Vector2 BasisY;                  // 8 bytes (Location 2) - Y basis of activeTransform
    public Vector4 BearSize;                // 16 bytes (Location 3) - BearX, BearY, Width, Height
    public Vector4 TexCoords;               // 16 bytes (Location 4) - TexCoordMin.X, TexCoordMin.Y, TexCoordMax.X, TexCoordMax.Y
    public Vector4 Color;                   // 16 bytes (Location 5) - RGBA color
    public Vector4 ScaleBoldItalicUseMvp;   // 16 bytes (Location 6) - ScaleRatio, BoldOffset, ItalicSkew, UseMvp (1.0 or 0.0)
    public float BrushIndex;                // 4 bytes (Location 7) - Brush index
    public float Padding;                   // 4 bytes (Location 8) - exact integer: transformIndex * 256 + atlasPage

    public readonly uint AtlasPage => DecodeAtlasPage(Padding);

    public readonly uint TransformIndex => DecodeTransformIndex(Padding);

    public static float EncodeAtlasHandle(uint atlasPage, uint transformIndex)
    {
        if (atlasPage > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(atlasPage));
        if (transformIndex > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(transformIndex));
        return checked(transformIndex * 256u + atlasPage);
    }

    public static uint DecodeAtlasPage(float encoded) =>
        checked((uint)MathF.Round(encoded)) & 0xffu;

    public static uint DecodeTransformIndex(float encoded) =>
        checked((uint)MathF.Round(encoded)) >> 8;
}
