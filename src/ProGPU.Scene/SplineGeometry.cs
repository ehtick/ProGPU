using System;
using System.Numerics;
using ProGPU.Vector;

namespace ProGPU.Scene;

internal static class SplineGeometry
{
    public const int MaxSegmentCount = 100;

    public static PathGeometry CreatePath(
        ReadOnlySpan<Vector2> controlPoints,
        ReadOnlySpan<double> knots,
        ReadOnlySpan<double> weights,
        int degree,
        bool isClosed)
    {
        var path = new PathGeometry();
        if (!TryGetDomain(controlPoints, knots, degree, out double startKnot, out double endKnot))
        {
            return controlPoints.Length >= 2
                ? RenderCommandGeometryCache.CreatePolylinePath(controlPoints, isClosed)
                : path;
        }

        var figure = new PathFigure(
            EvaluatePoint(degree, controlPoints, knots, weights, startKnot, Matrix4x4.Identity),
            isClosed);

        double delta = (endKnot - startKnot) / MaxSegmentCount;
        for (int i = 1; i <= MaxSegmentCount; i++)
        {
            double u = startKnot + i * delta;
            figure.Segments.Add(new LineSegment(
                EvaluatePoint(degree, controlPoints, knots, weights, u, Matrix4x4.Identity)));
        }

        path.Figures.Add(figure);
        return path;
    }

    public static int GetScreenSegmentCount(ReadOnlySpan<Vector2> controlPoints, Matrix4x4 transform)
    {
        if (controlPoints.Length == 0)
        {
            return 0;
        }

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        foreach (var controlPoint in controlPoints)
        {
            var screenPoint = Vector2.Transform(controlPoint, transform);
            if (!float.IsFinite(screenPoint.X) || !float.IsFinite(screenPoint.Y))
            {
                return 0;
            }

            minX = MathF.Min(minX, screenPoint.X);
            minY = MathF.Min(minY, screenPoint.Y);
            maxX = MathF.Max(maxX, screenPoint.X);
            maxY = MathF.Max(maxY, screenPoint.Y);
        }

        float sizeOnScreen = Vector2.Distance(new Vector2(minX, minY), new Vector2(maxX, maxY));
        if (!float.IsFinite(sizeOnScreen) || sizeOnScreen < 2f)
        {
            return 0;
        }

        if (sizeOnScreen < 20f)
        {
            return 10;
        }

        if (sizeOnScreen < 80f)
        {
            return 25;
        }

        if (sizeOnScreen < 250f)
        {
            return 50;
        }

        return MaxSegmentCount;
    }

    public static bool TryEvaluatePoints(
        int degree,
        ReadOnlySpan<Vector2> controlPoints,
        ReadOnlySpan<double> knots,
        ReadOnlySpan<double> weights,
        Matrix4x4 transform,
        Span<Vector2> points)
    {
        if (points.Length < 2 ||
            !TryGetDomain(controlPoints, knots, degree, out double startKnot, out double endKnot))
        {
            return false;
        }

        int segmentCount = points.Length - 1;
        double delta = (endKnot - startKnot) / segmentCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            double u = startKnot + i * delta;
            points[i] = EvaluatePoint(degree, controlPoints, knots, weights, u, transform);
        }

        return true;
    }

    public static bool TryGetDomain(
        ReadOnlySpan<Vector2> controlPoints,
        ReadOnlySpan<double> knots,
        int degree,
        out double startKnot,
        out double endKnot)
    {
        startKnot = 0d;
        endKnot = 0d;

        if (degree < 0 ||
            controlPoints.Length < 2 ||
            knots.Length < controlPoints.Length + degree + 1)
        {
            return false;
        }

        int endKnotIndex = knots.Length - degree - 1;
        if (degree >= knots.Length ||
            endKnotIndex <= degree ||
            endKnotIndex >= knots.Length)
        {
            return false;
        }

        startKnot = knots[degree];
        endKnot = knots[endKnotIndex];
        return double.IsFinite(startKnot) &&
               double.IsFinite(endKnot) &&
               endKnot > startKnot;
    }

    public static Vector2 EvaluatePoint(
        int degree,
        ReadOnlySpan<Vector2> controlPoints,
        ReadOnlySpan<double> knots,
        ReadOnlySpan<double> weights,
        double u,
        Matrix4x4 transform)
    {
        int k = -1;
        if (u < knots[degree])
        {
            u = knots[degree];
        }

        int endKnotIndex = knots.Length - degree - 1;
        if (u > knots[endKnotIndex])
        {
            u = knots[endKnotIndex];
        }

        for (int i = degree; i < knots.Length - 1; i++)
        {
            if (u >= knots[i] && u <= knots[i + 1])
            {
                k = i;
                break;
            }
        }

        if (k == -1)
        {
            k = knots.Length - degree - 2;
        }

        int pointCount = degree + 1;
        Span<Vector3> d = pointCount <= 64
            ? stackalloc Vector3[pointCount]
            : new Vector3[pointCount];

        for (int j = 0; j <= degree; j++)
        {
            int idx = k - degree + j;
            if (idx >= 0 && idx < controlPoints.Length)
            {
                float w = 1f;
                if (!weights.IsEmpty && idx < weights.Length)
                {
                    w = (float)weights[idx];
                }

                d[j] = new Vector3(controlPoints[idx].X * w, controlPoints[idx].Y * w, w);
            }
            else
            {
                d[j] = Vector3.Zero;
            }
        }

        for (int r = 1; r <= degree; r++)
        {
            for (int j = degree; j >= r; j--)
            {
                int i = k - degree + j;
                double denom = knots[i + degree + 1 - r] - knots[i];
                float alpha = denom > 1e-9
                    ? (float)((u - knots[i]) / denom)
                    : 0f;
                d[j] = (1f - alpha) * d[j - 1] + alpha * d[j];
            }
        }

        Vector3 finalH = d[degree];
        var cartesianPoint = MathF.Abs(finalH.Z) > 1e-9f
            ? new Vector2(finalH.X / finalH.Z, finalH.Y / finalH.Z)
            : new Vector2(finalH.X, finalH.Y);

        return Vector2.Transform(cartesianPoint, transform);
    }
}
