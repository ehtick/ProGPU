using System.Numerics;
using ProGPU.Scene;

namespace SkiaSharp;

internal static class SKAtlasLayout
{
    internal static TexturePatch[] CreatePatches(
        SKRect[] sprites,
        SKRotationScaleMatrix[] transforms,
        SKColor[]? colors,
        SKBlendMode colorBlendMode,
        SKColorFilter? colorFilter,
        out SKRect bounds)
    {
        var patchCount = 0;
        for (var index = 0; index < sprites.Length; index++)
        {
            if (sprites[index].Width > 0f && sprites[index].Height > 0f)
            {
                patchCount++;
            }
        }

        if (patchCount == 0)
        {
            bounds = SKRect.Empty;
            return [];
        }

        var patches = new TexturePatch[patchCount];
        var patchIndex = 0;
        var hasBounds = false;
        var left = 0f;
        var top = 0f;
        var right = 0f;
        var bottom = 0f;
        for (var index = 0; index < sprites.Length; index++)
        {
            var sprite = sprites[index];
            if (sprite.Width <= 0f || sprite.Height <= 0f)
            {
                continue;
            }

            var rsxform = transforms[index];
            var destinationTransform = new Matrix3x2(
                rsxform.SCos,
                rsxform.SSin,
                -rsxform.SSin,
                rsxform.SCos,
                rsxform.TX,
                rsxform.TY);
            Vector4? color = null;
            if (colors != null)
            {
                var filteredColor = colorFilter?.Apply(colors[index]) ?? colors[index];
                color = ToVector4(filteredColor);
            }

            var destination = new Rect(0f, 0f, sprite.Width, sprite.Height);
            patches[patchIndex++] = new TexturePatch(
                new Rect(sprite.Left, sprite.Top, sprite.Width, sprite.Height),
                destination,
                destinationTransform,
                color,
                (VertexColorBlendMode)colorBlendMode);

            AddBoundsPoint(Vector2.Transform(Vector2.Zero, destinationTransform));
            AddBoundsPoint(Vector2.Transform(new Vector2(sprite.Width, 0f), destinationTransform));
            AddBoundsPoint(Vector2.Transform(new Vector2(sprite.Width, sprite.Height), destinationTransform));
            AddBoundsPoint(Vector2.Transform(new Vector2(0f, sprite.Height), destinationTransform));
        }

        bounds = hasBounds ? new SKRect(left, top, right, bottom) : SKRect.Empty;
        return patches;

        void AddBoundsPoint(Vector2 point)
        {
            if (!hasBounds)
            {
                left = right = point.X;
                top = bottom = point.Y;
                hasBounds = true;
                return;
            }

            left = MathF.Min(left, point.X);
            top = MathF.Min(top, point.Y);
            right = MathF.Max(right, point.X);
            bottom = MathF.Max(bottom, point.Y);
        }
    }

    private static Vector4 ToVector4(SKColor color) => new(
        color.Red / 255f,
        color.Green / 255f,
        color.Blue / 255f,
        color.Alpha / 255f);
}
