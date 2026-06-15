using System;
using System.Numerics;
using ProGPU.Vector;

namespace ProGPU.Scene.Extensions;

internal static class QuadClipper
{
    public static bool TryClipAxisAlignedQuad(
        Rect clip,
        ref Vector2 v0,
        ref Vector2 v1,
        ref Vector2 v2,
        ref Vector2 v3,
        ref Vector2 uv0,
        ref Vector2 uv1,
        ref Vector2 uv2,
        ref Vector2 uv3)
    {
        const float epsilon = 0.0001f;
        if (MathF.Abs(v0.Y - v1.Y) > epsilon ||
            MathF.Abs(v2.Y - v3.Y) > epsilon ||
            MathF.Abs(v0.X - v3.X) > epsilon ||
            MathF.Abs(v1.X - v2.X) > epsilon)
        {
            return true;
        }

        var left = MathF.Min(MathF.Min(v0.X, v1.X), MathF.Min(v2.X, v3.X));
        var right = MathF.Max(MathF.Max(v0.X, v1.X), MathF.Max(v2.X, v3.X));
        var top = MathF.Min(MathF.Min(v0.Y, v1.Y), MathF.Min(v2.Y, v3.Y));
        var bottom = MathF.Max(MathF.Max(v0.Y, v1.Y), MathF.Max(v2.Y, v3.Y));

        var clipLeft = MathF.Max(left, clip.X);
        var clipTop = MathF.Max(top, clip.Y);
        var clipRight = MathF.Min(right, clip.X + clip.Width);
        var clipBottom = MathF.Min(bottom, clip.Y + clip.Height);
        if (clipRight <= clipLeft || clipBottom <= clipTop)
        {
            return false;
        }

        var x0 = v0.X;
        var x1 = v1.X;
        var y0 = v0.Y;
        var y1 = v3.Y;
        if (MathF.Abs(x1 - x0) <= epsilon || MathF.Abs(y1 - y0) <= epsilon)
        {
            return false;
        }

        var nx0 = Math.Clamp(x0, clipLeft, clipRight);
        var nx1 = Math.Clamp(x1, clipLeft, clipRight);
        var ny0 = Math.Clamp(y0, clipTop, clipBottom);
        var ny1 = Math.Clamp(y1, clipTop, clipBottom);

        var u0 = (nx0 - x0) / (x1 - x0);
        var u1 = (nx1 - x0) / (x1 - x0);
        var tv0 = (ny0 - y0) / (y1 - y0);
        var tv1 = (ny1 - y0) / (y1 - y0);

        v0 = new Vector2(nx0, ny0);
        v1 = new Vector2(nx1, ny0);
        v2 = new Vector2(nx1, ny1);
        v3 = new Vector2(nx0, ny1);
        uv0 = new Vector2(u0, tv0);
        uv1 = new Vector2(u1, tv0);
        uv2 = new Vector2(u1, tv1);
        uv3 = new Vector2(u0, tv1);
        return true;
    }
}
