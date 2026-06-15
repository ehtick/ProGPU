using System;
using System.Numerics;

namespace ProGPU.Vector;

public static class ArcSegmentGeometry
{
    public const float DefaultFlattenAngleRadians = MathF.PI / 8.0f;
    public const int MaxFlattenSegmentCount = 64;

    private const float Epsilon = 0.00001f;

    public static bool TryGetArcCenter(
        Vector2 start,
        Vector2 end,
        Vector2 radii,
        float rotationAngleDegrees,
        bool isLargeArc,
        SweepDirection sweepDirection,
        out Vector2 center,
        out float theta1,
        out float deltaTheta,
        out float radiusX,
        out float radiusY)
    {
        center = default;
        theta1 = 0.0f;
        deltaTheta = 0.0f;
        radiusX = MathF.Abs(radii.X);
        radiusY = MathF.Abs(radii.Y);

        if (!IsFinite(start) ||
            !IsFinite(end) ||
            !IsFinite(radii) ||
            !float.IsFinite(rotationAngleDegrees) ||
            Vector2.DistanceSquared(start, end) <= Epsilon * Epsilon ||
            radiusX <= Epsilon ||
            radiusY <= Epsilon)
        {
            return false;
        }

        float phi = rotationAngleDegrees * MathF.PI / 180.0f;
        float cosPhi = MathF.Cos(phi);
        float sinPhi = MathF.Sin(phi);

        float dx = (start.X - end.X) * 0.5f;
        float dy = (start.Y - end.Y) * 0.5f;
        float x1p = cosPhi * dx + sinPhi * dy;
        float y1p = -sinPhi * dx + cosPhi * dy;

        float prx = radiusX * radiusX;
        float pry = radiusY * radiusY;
        float px1p = x1p * x1p;
        float py1p = y1p * y1p;

        float radiiCheck = px1p / prx + py1p / pry;
        if (!float.IsFinite(radiiCheck))
        {
            return false;
        }

        if (radiiCheck > 1.0f)
        {
            float scale = MathF.Sqrt(radiiCheck);
            radiusX *= scale;
            radiusY *= scale;
            prx = radiusX * radiusX;
            pry = radiusY * radiusY;
        }

        float denominator = prx * py1p + pry * px1p;
        if (denominator <= Epsilon || !float.IsFinite(denominator))
        {
            return false;
        }

        float sign = isLargeArc == (sweepDirection == SweepDirection.Clockwise) ? -1.0f : 1.0f;
        float sqTerm = (prx * pry - prx * py1p - pry * px1p) / denominator;
        if (!float.IsFinite(sqTerm))
        {
            return false;
        }

        if (sqTerm < 0.0f)
        {
            sqTerm = 0.0f;
        }

        float coef = sign * MathF.Sqrt(sqTerm);
        float cxp = coef * ((radiusX * y1p) / radiusY);
        float cyp = coef * -((radiusY * x1p) / radiusX);

        center = new Vector2(
            cosPhi * cxp - sinPhi * cyp + (start.X + end.X) * 0.5f,
            sinPhi * cxp + cosPhi * cyp + (start.Y + end.Y) * 0.5f);

        float ux = (x1p - cxp) / radiusX;
        float uy = (y1p - cyp) / radiusY;
        float vx = (-x1p - cxp) / radiusX;
        float vy = (-y1p - cyp) / radiusY;

        theta1 = MathF.Atan2(uy, ux);
        float theta2 = MathF.Atan2(vy, vx);

        deltaTheta = theta2 - theta1;
        if (sweepDirection == SweepDirection.Clockwise)
        {
            if (deltaTheta < 0.0f)
            {
                deltaTheta += 2.0f * MathF.PI;
            }
        }
        else if (deltaTheta > 0.0f)
        {
            deltaTheta -= 2.0f * MathF.PI;
        }

        return IsFinite(center) &&
               float.IsFinite(theta1) &&
               float.IsFinite(deltaTheta) &&
               float.IsFinite(radiusX) &&
               float.IsFinite(radiusY);
    }

    public static Vector2 EvaluatePoint(
        Vector2 center,
        float radiusX,
        float radiusY,
        float rotationAngleDegrees,
        float theta)
    {
        float phi = rotationAngleDegrees * MathF.PI / 180.0f;
        float cosPhi = MathF.Cos(phi);
        float sinPhi = MathF.Sin(phi);
        float cosTheta = MathF.Cos(theta);
        float sinTheta = MathF.Sin(theta);

        return new Vector2(
            radiusX * cosTheta * cosPhi - radiusY * sinTheta * sinPhi + center.X,
            radiusX * cosTheta * sinPhi + radiusY * sinTheta * cosPhi + center.Y);
    }

    public static bool TryTransformArcSegment(
        Vector2 start,
        ArcSegment arc,
        Matrix4x4 transform,
        out Vector2 transformedStart,
        out ArcSegment transformedArc)
    {
        transformedStart = default;
        transformedArc = null!;

        if (arc == null || !Is2DAffineTransform(transform))
        {
            return false;
        }

        transformedStart = Vector2.Transform(start, transform);
        Vector2 transformedEnd = Vector2.Transform(arc.Point, transform);
        if (!IsFinite(transformedStart) || !IsFinite(transformedEnd))
        {
            return false;
        }

        if (TryGetPositiveSimilarityTransform(transform, out float uniformScale, out float rotationDegrees))
        {
            transformedArc = new ArcSegment(
                transformedEnd,
                new Vector2(MathF.Abs(arc.Size.X) * uniformScale, MathF.Abs(arc.Size.Y) * uniformScale),
                NormalizeRotationAngle(arc.RotationAngle + rotationDegrees),
                arc.IsLargeArc,
                arc.SweepDirection,
                arc.IsSmoothJoin);
            return true;
        }

        if (TryGetPositiveAxisAlignedScale(transform, arc.RotationAngle, out Vector2 axisScale))
        {
            transformedArc = new ArcSegment(
                transformedEnd,
                new Vector2(MathF.Abs(arc.Size.X) * axisScale.X, MathF.Abs(arc.Size.Y) * axisScale.Y),
                NormalizeRotationAngle(arc.RotationAngle),
                arc.IsLargeArc,
                arc.SweepDirection,
                arc.IsSmoothJoin);
            return true;
        }

        if (!TryGetArcCenter(
                start,
                arc.Point,
                arc.Size,
                arc.RotationAngle,
                arc.IsLargeArc,
                arc.SweepDirection,
                out _,
                out _,
                out _,
                out float radiusX,
                out float radiusY))
        {
            return false;
        }

        float determinant = Get2DDeterminant(transform);
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= Epsilon)
        {
            return false;
        }

        float phi = arc.RotationAngle * MathF.PI / 180.0f;
        float cosPhi = MathF.Cos(phi);
        float sinPhi = MathF.Sin(phi);

        Vector2 axisX = TransformDirection(new Vector2(radiusX * cosPhi, radiusX * sinPhi), transform);
        Vector2 axisY = TransformDirection(new Vector2(-radiusY * sinPhi, radiusY * cosPhi), transform);
        if (!TryGetPrincipalEllipseAxes(axisX, axisY, out Vector2 radii, out float transformedRotationAngle))
        {
            return false;
        }

        var sweepDirection = determinant < 0.0f
            ? ReverseSweepDirection(arc.SweepDirection)
            : arc.SweepDirection;

        transformedArc = new ArcSegment(
            transformedEnd,
            radii,
            transformedRotationAngle,
            arc.IsLargeArc,
            sweepDirection,
            arc.IsSmoothJoin);
        return true;
    }

    public static int CountFlattenedSegments(
        Vector2 start,
        ArcSegment arc,
        float maxAngleRadians = DefaultFlattenAngleRadians)
    {
        if (!TryGetArcCenter(
                start,
                arc.Point,
                arc.Size,
                arc.RotationAngle,
                arc.IsLargeArc,
                arc.SweepDirection,
                out _,
                out _,
                out float deltaTheta,
                out _,
                out _))
        {
            return Vector2.DistanceSquared(start, arc.Point) > Epsilon * Epsilon ? 1 : 0;
        }

        return CountArcSegments(deltaTheta, maxAngleRadians);
    }

    public static Vector2[] FlattenArc(
        Vector2 start,
        ArcSegment arc,
        float maxAngleRadians = DefaultFlattenAngleRadians)
    {
        if (!TryGetArcCenter(
                start,
                arc.Point,
                arc.Size,
                arc.RotationAngle,
                arc.IsLargeArc,
                arc.SweepDirection,
                out var center,
                out float theta1,
                out float deltaTheta,
                out float radiusX,
                out float radiusY))
        {
            return Vector2.DistanceSquared(start, arc.Point) > Epsilon * Epsilon
                ? new[] { start, arc.Point }
                : new[] { start };
        }

        int segmentCount = CountArcSegments(deltaTheta, maxAngleRadians);
        var points = new Vector2[segmentCount + 1];
        points[0] = start;
        for (int i = 1; i < segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            points[i] = EvaluatePoint(center, radiusX, radiusY, arc.RotationAngle, theta1 + t * deltaTheta);
        }

        points[segmentCount] = arc.Point;
        return points;
    }

    private static int CountArcSegments(float deltaTheta, float maxAngleRadians)
    {
        if (!float.IsFinite(maxAngleRadians) || maxAngleRadians <= Epsilon)
        {
            maxAngleRadians = DefaultFlattenAngleRadians;
        }

        int segmentCount = (int)MathF.Ceiling(MathF.Abs(deltaTheta) / maxAngleRadians);
        return Math.Clamp(segmentCount, 1, MaxFlattenSegmentCount);
    }

    private static bool TryGetPositiveSimilarityTransform(Matrix4x4 transform, out float scale, out float rotationDegrees)
    {
        Vector2 basisX = new(transform.M11, transform.M12);
        Vector2 basisY = new(transform.M21, transform.M22);
        float scaleX = basisX.Length();
        float scaleY = basisY.Length();

        if (!float.IsFinite(scaleX)
            || !float.IsFinite(scaleY)
            || scaleX <= Epsilon
            || scaleY <= Epsilon
            || MathF.Abs(scaleX - scaleY) > Epsilon
            || MathF.Abs(Vector2.Dot(basisX, basisY)) > Epsilon * scaleX * scaleY
            || Get2DDeterminant(transform) <= Epsilon)
        {
            scale = 0.0f;
            rotationDegrees = 0.0f;
            return false;
        }

        scale = (scaleX + scaleY) * 0.5f;
        rotationDegrees = MathF.Atan2(transform.M12, transform.M11) * 180.0f / MathF.PI;
        return true;
    }

    private static bool TryGetPositiveAxisAlignedScale(Matrix4x4 transform, float rotationAngle, out Vector2 scale)
    {
        bool isAxisAlignedArc = MathF.Abs(NormalizeAxisAngle(rotationAngle)) <= Epsilon;
        if (!isAxisAlignedArc
            || MathF.Abs(transform.M12) > Epsilon
            || MathF.Abs(transform.M21) > Epsilon
            || transform.M11 <= Epsilon
            || transform.M22 <= Epsilon)
        {
            scale = default;
            return false;
        }

        scale = new Vector2(transform.M11, transform.M22);
        return true;
    }

    private static bool TryGetPrincipalEllipseAxes(Vector2 axisX, Vector2 axisY, out Vector2 radii, out float rotationAngle)
    {
        radii = default;
        rotationAngle = 0.0f;

        if (!IsFinite(axisX) || !IsFinite(axisY))
        {
            return false;
        }

        float sxx = axisX.X * axisX.X + axisY.X * axisY.X;
        float sxy = axisX.X * axisX.Y + axisY.X * axisY.Y;
        float syy = axisX.Y * axisX.Y + axisY.Y * axisY.Y;
        float trace = sxx + syy;
        float diff = sxx - syy;
        float root = MathF.Sqrt(MathF.Max(0.0f, diff * diff + 4.0f * sxy * sxy));
        float lambda1 = (trace + root) * 0.5f;
        float lambda2 = (trace - root) * 0.5f;

        if (!float.IsFinite(lambda1)
            || !float.IsFinite(lambda2)
            || lambda1 <= Epsilon * Epsilon
            || lambda2 <= Epsilon * Epsilon)
        {
            return false;
        }

        Vector2 axis;
        if (MathF.Abs(sxy) > Epsilon || MathF.Abs(lambda1 - sxx) > Epsilon)
        {
            axis = new Vector2(sxy, lambda1 - sxx);
            float axisLength = axis.Length();
            if (!float.IsFinite(axisLength) || axisLength <= Epsilon)
            {
                axis = Vector2.UnitX;
            }
            else
            {
                axis /= axisLength;
            }
        }
        else
        {
            axis = Vector2.UnitX;
        }

        radii = new Vector2(MathF.Sqrt(lambda1), MathF.Sqrt(lambda2));
        rotationAngle = NormalizeRotationAngle(MathF.Atan2(axis.Y, axis.X) * 180.0f / MathF.PI);
        return IsFinite(radii) && float.IsFinite(rotationAngle);
    }

    private static Vector2 TransformDirection(Vector2 direction, Matrix4x4 transform)
    {
        return new Vector2(
            direction.X * transform.M11 + direction.Y * transform.M21,
            direction.X * transform.M12 + direction.Y * transform.M22);
    }

    private static bool Is2DAffineTransform(Matrix4x4 transform)
    {
        return MathF.Abs(transform.M13) <= Epsilon
            && MathF.Abs(transform.M14) <= Epsilon
            && MathF.Abs(transform.M23) <= Epsilon
            && MathF.Abs(transform.M24) <= Epsilon
            && MathF.Abs(transform.M31) <= Epsilon
            && MathF.Abs(transform.M32) <= Epsilon
            && MathF.Abs(transform.M33 - 1.0f) <= Epsilon
            && MathF.Abs(transform.M34) <= Epsilon
            && MathF.Abs(transform.M43) <= Epsilon
            && MathF.Abs(transform.M44 - 1.0f) <= Epsilon
            && float.IsFinite(transform.M11)
            && float.IsFinite(transform.M12)
            && float.IsFinite(transform.M21)
            && float.IsFinite(transform.M22)
            && float.IsFinite(transform.M41)
            && float.IsFinite(transform.M42);
    }

    private static float Get2DDeterminant(Matrix4x4 transform)
    {
        return transform.M11 * transform.M22 - transform.M12 * transform.M21;
    }

    private static SweepDirection ReverseSweepDirection(SweepDirection sweepDirection)
    {
        return sweepDirection == SweepDirection.Clockwise
            ? SweepDirection.Counterclockwise
            : SweepDirection.Clockwise;
    }

    private static float NormalizeRotationAngle(float angle)
    {
        if (!float.IsFinite(angle))
        {
            return 0.0f;
        }

        float normalized = angle % 360.0f;
        if (normalized < 0.0f)
        {
            normalized += 360.0f;
        }

        return normalized;
    }

    private static float NormalizeAxisAngle(float angle)
    {
        float normalized = NormalizeRotationAngle(angle);
        if (normalized > 180.0f)
        {
            normalized -= 360.0f;
        }

        if (normalized > 90.0f)
        {
            normalized -= 180.0f;
        }
        else if (normalized < -90.0f)
        {
            normalized += 180.0f;
        }

        return normalized;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
