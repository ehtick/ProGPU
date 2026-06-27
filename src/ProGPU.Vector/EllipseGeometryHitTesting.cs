using System;
using System.Numerics;

#nullable enable
#pragma warning disable IDE0057, IDE0059, IDE0078, IDE0300, IDE0301, IDE0305

namespace ProGPU.Vector;

#if PROGPU_VECTOR_INTERNAL
internal
#else
public
#endif
static class EllipseGeometryHitTesting
{
    public static bool ContainsFill(
        Vector2 point,
        Vector2 center,
        Vector2 radius)
    {
        if (!IsFinite(point) || !IsFinite(center) || !IsFinite(radius))
        {
            return false;
        }

        float radiusX = MathF.Abs(radius.X);
        float radiusY = MathF.Abs(radius.Y);
        if (radiusX <= 0.0f || radiusY <= 0.0f)
        {
            return false;
        }

        float normalizedX = (point.X - center.X) / radiusX;
        float normalizedY = (point.Y - center.Y) / radiusY;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.0f;
    }

    public static bool ContainsStroke(
        Vector2 point,
        Vector2 center,
        Vector2 radius,
        float strokeThickness,
        float tolerance,
        bool relativeTolerance)
    {
        if (!IsFinite(point) || !IsFinite(center) || !IsFinite(radius))
        {
            return false;
        }

        if (!float.IsFinite(strokeThickness) || strokeThickness <= 0.0f || !float.IsFinite(tolerance))
        {
            return false;
        }

        float radiusX = MathF.Abs(radius.X);
        float radiusY = MathF.Abs(radius.Y);
        if (radiusX <= 0.0f || radiusY <= 0.0f)
        {
            return false;
        }

        float tolerancePadding = MathF.Max(0.0f, tolerance);
        if (relativeTolerance)
        {
            tolerancePadding *= MathF.Max(radiusX * 2.0f, radiusY * 2.0f);
        }

        if (!float.IsFinite(tolerancePadding))
        {
            return false;
        }

        float halfStroke = (MathF.Abs(strokeThickness) * 0.5f) + tolerancePadding;
        if (halfStroke <= 0.0f || !float.IsFinite(halfStroke))
        {
            return false;
        }

        Vector2 outerRadius = new(radiusX + halfStroke, radiusY + halfStroke);
        if (!ContainsFill(point, center, outerRadius))
        {
            return false;
        }

        float innerRadiusX = radiusX - halfStroke;
        float innerRadiusY = radiusY - halfStroke;
        if (innerRadiusX <= 0.0f || innerRadiusY <= 0.0f)
        {
            return true;
        }

        return !ContainsFill(point, center, new Vector2(innerRadiusX, innerRadiusY));
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
