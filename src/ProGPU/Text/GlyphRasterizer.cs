using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;

namespace ProGPU.Text;

public struct RasterGlyph
{
    public int Width;
    public int Height;
    public int BearX;
    public int BearY;
    public byte[] AlphaMap;
}

public static class GlyphRasterizer
{
    public static RasterGlyph Rasterize(PathGeometry outline, TtfFont font, float emSize)
    {
        // 1. Calculate scaling factors
        float scale = emSize / font.UnitsPerEm;

        // Extract and flatten contours, scaling coordinates
        var flattenedContours = outline.Flatten(0.2f); // Flatten curves with high tolerance
        var scaledContours = new List<List<Vector2>>();
        
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        // Convert TTF coordinates (Y-up) to screen coordinates (Y-down) and find bounds
        foreach (var contour in flattenedContours)
        {
            var scaledContour = new List<Vector2>(contour.Count);
            foreach (var pt in contour)
            {
                var spt = new Vector2(pt.X * scale, -pt.Y * scale);
                scaledContour.Add(spt);

                minX = Math.Min(minX, spt.X);
                maxX = Math.Max(maxX, spt.X);
                minY = Math.Min(minY, spt.Y);
                maxY = Math.Max(maxY, spt.Y);
            }
            scaledContours.Add(scaledContour);
        }

        // If glyph is empty
        if (scaledContours.Count == 0 || minX > maxX || minY > maxY)
        {
            return new RasterGlyph { Width = 0, Height = 0, BearX = 0, BearY = 0, AlphaMap = Array.Empty<byte>() };
        }

        // 2. Add padding to avoid clipping antialiased edges
        int padding = 2;
        int xStart = (int)Math.Floor(minX) - padding;
        int xEnd = (int)Math.Ceiling(maxX) + padding;
        int yStart = (int)Math.Floor(minY) - padding;
        int yEnd = (int)Math.Ceiling(maxY) + padding;

        int width = xEnd - xStart;
        int height = yEnd - yStart;

        byte[] alphaMap = new byte[width * height];

        // 3. Ray-casting polygon intersection checker with Even-Odd rule
        bool IsPointInContours(Vector2 p)
        {
            bool inside = false;
            foreach (var contour in scaledContours)
            {
                int count = contour.Count;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    Vector2 v1 = contour[i];
                    Vector2 v2 = contour[j];

                    if (((v1.Y > p.Y) != (v2.Y > p.Y)) &&
                        (p.X < (v2.X - v1.X) * (p.Y - v1.Y) / (v2.Y - v1.Y) + v1.X))
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        // 4. Supersampling: 4x4 grid (16 samples per pixel)
        float[] subpixelOffsets = { 0.125f, 0.375f, 0.625f, 0.875f };

        for (int y = 0; y < height; y++)
        {
            float pixelY = yStart + y;
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                float pixelX = xStart + x;
                int hits = 0;

                // Test 16 subpixel sample points
                for (int sy = 0; sy < 4; sy++)
                {
                    float testY = pixelY + subpixelOffsets[sy];
                    for (int sx = 0; sx < 4; sx++)
                    {
                        float testX = pixelX + subpixelOffsets[sx];
                        if (IsPointInContours(new Vector2(testX, testY)))
                        {
                            hits++;
                        }
                    }
                }

                // Compute final alpha (0 to 255)
                alphaMap[rowOffset + x] = (byte)((hits * 255) / 16);
            }
        }

        return new RasterGlyph
        {
            Width = width,
            Height = height,
            BearX = xStart,
            BearY = yStart,
            AlphaMap = alphaMap
        };
    }
}
