using System.Numerics;
using ProGPU.Backend;
using ProGPU.Vector;

namespace SkiaSharp;

public partial class SKPath
{
    public SKPath Simplify()
    {
        var result = new SKPath();
        Simplify(result);
        return result;
    }

    public bool Simplify(SKPath result)
    {
        if (result == null)
        {
            return false;
        }

        if (TrySimplifyLinearGeometry(windingOutput: false, out var simplified))
        {
            ApplySolvedGeometry(result, simplified);
            return true;
        }
        if (TrySolveWithActiveGpu(windingOutput: false, out simplified))
        {
            ApplySolvedGeometry(result, simplified);
            return true;
        }

        using var copy = new SKPath(this)
        {
            FillType = FillType is SKPathFillType.InverseEvenOdd or SKPathFillType.InverseWinding
                ? SKPathFillType.InverseEvenOdd
                : SKPathFillType.EvenOdd,
        };
        result.ReplaceWith(copy);
        return true;
    }

    public SKPath ToWinding()
    {
        var result = new SKPath();
        ToWinding(result);
        return result;
    }

    public bool ToWinding(SKPath result)
    {
        if (result == null)
        {
            return false;
        }

        if (TrySimplifyLinearGeometry(windingOutput: true, out var winding))
        {
            ApplySolvedGeometry(result, winding);
            return true;
        }
        if (TrySolveWithActiveGpu(windingOutput: true, out winding))
        {
            ApplySolvedGeometry(result, winding);
            return true;
        }

        using var copy = new SKPath(this)
        {
            FillType = FillType is SKPathFillType.InverseEvenOdd or SKPathFillType.InverseWinding
                ? SKPathFillType.InverseWinding
                : SKPathFillType.Winding,
        };
        result.ReplaceWith(copy);
        return true;
    }

    public bool Op(SKPath other, SKPathOp op, SKPath result) =>
        Op(this, other, op, result);

    private bool TrySolveWithActiveGpu(
        bool windingOutput,
        out PathGeometry solved)
    {
        solved = null!;
        if (WgpuContext.Current == null &&
            !WgpuContext.TryGetFirstActiveContext(out _))
        {
            return false;
        }

        try
        {
            solved = PathOpGeometrySolver.Combine(
                Geometry,
                Geometry,
                (int)SKPathOp.Union);
            solved.FillRule = windingOutput ? FillRule.Nonzero : FillRule.EvenOdd;
            return true;
        }
        catch
        {
            solved = null!;
            return false;
        }
    }

    private bool TrySimplifyLinearGeometry(
        bool windingOutput,
        out PathGeometry result)
    {
        result = new PathGeometry
        {
            FillRule = windingOutput ? FillRule.Nonzero : FillRule.EvenOdd,
        };

        if (!TryCollectLinearEdges(out var edges, out var min, out var max))
        {
            return false;
        }
        if (edges.Count == 0)
        {
            return true;
        }

        var splitParameters = new List<float>[edges.Count];
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            splitParameters[edgeIndex] = [0f, 1f];
        }

        for (var first = 0; first < edges.Count; first++)
        {
            for (var second = first + 1; second < edges.Count; second++)
            {
                AddSegmentIntersections(
                    edges[first],
                    edges[second],
                    splitParameters[first],
                    splitParameters[second]);
            }
        }

        var scale = MathF.Max(1f, MathF.Max(max.X - min.X, max.Y - min.Y));
        var sampleOffset = scale * 0.0001f;
        var pointQuantum = scale * 0.00001f;
        var fragments = new List<DirectedEdge>();
        var uniqueFragments = new HashSet<DirectedEdgeKey>();
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            var parameters = splitParameters[edgeIndex];
            parameters.Sort();
            RemoveNearDuplicates(parameters);
            var edge = edges[edgeIndex];
            var delta = edge.End - edge.Start;
            for (var parameterIndex = 0; parameterIndex + 1 < parameters.Count; parameterIndex++)
            {
                var firstParameter = parameters[parameterIndex];
                var secondParameter = parameters[parameterIndex + 1];
                if (secondParameter - firstParameter <= 1e-6f)
                {
                    continue;
                }

                var start = edge.Start + delta * firstParameter;
                var end = edge.Start + delta * secondParameter;
                var direction = end - start;
                var length = direction.Length();
                if (length <= 1e-6f)
                {
                    continue;
                }

                var midpoint = (start + end) * 0.5f;
                var rightNormal = new Vector2(-direction.Y, direction.X) / length;
                if (!TryContainsOriginal(midpoint - rightNormal * sampleOffset, out var leftFilled) ||
                    !TryContainsOriginal(midpoint + rightNormal * sampleOffset, out var rightFilled) ||
                    leftFilled == rightFilled)
                {
                    continue;
                }

                var boundary = rightFilled
                    ? new DirectedEdge(start, end)
                    : new DirectedEdge(end, start);
                if (uniqueFragments.Add(CreateEdgeKey(boundary, pointQuantum)))
                {
                    fragments.Add(boundary);
                }
            }
        }

        if (fragments.Count == 0)
        {
            return true;
        }

        return TryStitchContours(fragments, scale, result);

        bool TryContainsOriginal(Vector2 point, out bool contains)
        {
            contains = ContainsLinearFill(edges, point, Geometry.FillRule);

            if (FillType is SKPathFillType.InverseEvenOdd or SKPathFillType.InverseWinding)
            {
                contains = !contains;
            }

            return true;
        }
    }

    private static bool ContainsLinearFill(
        List<DirectedEdge> edges,
        Vector2 point,
        FillRule fillRule)
    {
        var evenOdd = false;
        var winding = 0;
        foreach (var edge in edges)
        {
            var upward = edge.Start.Y <= point.Y && edge.End.Y > point.Y;
            var downward = edge.Start.Y > point.Y && edge.End.Y <= point.Y;
            if (!upward && !downward)
            {
                continue;
            }

            var intersectionX = edge.Start.X +
                (point.Y - edge.Start.Y) *
                (edge.End.X - edge.Start.X) /
                (edge.End.Y - edge.Start.Y);
            if (intersectionX <= point.X)
            {
                continue;
            }

            if (fillRule == FillRule.EvenOdd)
            {
                evenOdd = !evenOdd;
            }
            else
            {
                winding += upward ? 1 : -1;
            }
        }

        return fillRule == FillRule.EvenOdd ? evenOdd : winding != 0;
    }

    private bool TryCollectLinearEdges(
        out List<DirectedEdge> edges,
        out Vector2 min,
        out Vector2 max)
    {
        var collectedEdges = new List<DirectedEdge>();
        var minValue = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maxValue = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var figure in Geometry.Figures)
        {
            if (!figure.IsFilled || figure.Segments.Count == 0)
            {
                continue;
            }

            var start = figure.StartPoint;
            var current = start;
            Include(start);
            foreach (var segment in figure.Segments)
            {
                if (segment is not LineSegment line)
                {
                    edges = collectedEdges;
                    min = minValue;
                    max = maxValue;
                    return false;
                }

                if (!IsFinite(line.Point))
                {
                    edges = collectedEdges;
                    min = minValue;
                    max = maxValue;
                    return false;
                }

                AddEdge(current, line.Point);
                current = line.Point;
            }

            AddEdge(current, start);
        }

        edges = collectedEdges;
        min = minValue;
        max = maxValue;
        return true;

        void AddEdge(Vector2 start, Vector2 end)
        {
            Include(end);
            if (Vector2.DistanceSquared(start, end) > 1e-12f)
            {
                collectedEdges.Add(new DirectedEdge(start, end));
            }
        }

        void Include(Vector2 point)
        {
            if (!IsFinite(point))
            {
                return;
            }

            minValue = Vector2.Min(minValue, point);
            maxValue = Vector2.Max(maxValue, point);
        }
    }

    private static void AddSegmentIntersections(
        in DirectedEdge first,
        in DirectedEdge second,
        List<float> firstParameters,
        List<float> secondParameters)
    {
        const float epsilon = 1e-6f;
        var firstDelta = first.End - first.Start;
        var secondDelta = second.End - second.Start;
        var offset = second.Start - first.Start;
        var denominator = Cross(firstDelta, secondDelta);
        if (MathF.Abs(denominator) > epsilon)
        {
            var firstParameter = Cross(offset, secondDelta) / denominator;
            var secondParameter = Cross(offset, firstDelta) / denominator;
            if (firstParameter >= -epsilon && firstParameter <= 1f + epsilon &&
                secondParameter >= -epsilon && secondParameter <= 1f + epsilon)
            {
                firstParameters.Add(Math.Clamp(firstParameter, 0f, 1f));
                secondParameters.Add(Math.Clamp(secondParameter, 0f, 1f));
            }

            return;
        }

        if (MathF.Abs(Cross(offset, firstDelta)) > epsilon)
        {
            return;
        }

        AddProjectedParameter(second.Start, first, firstParameters);
        AddProjectedParameter(second.End, first, firstParameters);
        AddProjectedParameter(first.Start, second, secondParameters);
        AddProjectedParameter(first.End, second, secondParameters);
    }

    private static void AddProjectedParameter(
        Vector2 point,
        in DirectedEdge edge,
        List<float> parameters)
    {
        var delta = edge.End - edge.Start;
        var lengthSquared = delta.LengthSquared();
        if (lengthSquared <= 1e-12f)
        {
            return;
        }

        var parameter = Vector2.Dot(point - edge.Start, delta) / lengthSquared;
        if (parameter >= -1e-6f && parameter <= 1f + 1e-6f)
        {
            parameters.Add(Math.Clamp(parameter, 0f, 1f));
        }
    }

    private static void RemoveNearDuplicates(List<float> values)
    {
        var write = 1;
        for (var read = 1; read < values.Count; read++)
        {
            if (MathF.Abs(values[read] - values[write - 1]) > 1e-6f)
            {
                values[write++] = values[read];
            }
        }

        if (write < values.Count)
        {
            values.RemoveRange(write, values.Count - write);
        }
    }

    private static bool TryStitchContours(
        List<DirectedEdge> edges,
        float scale,
        PathGeometry result)
    {
        var used = new bool[edges.Count];
        var toleranceSquared = scale * scale * 1e-10f;
        var pointQuantum = scale * 0.00001f;
        var adjacency = new Dictionary<PointKey, List<int>>();
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            var key = CreatePointKey(edges[edgeIndex].Start, pointQuantum);
            if (!adjacency.TryGetValue(key, out var outgoing))
            {
                outgoing = new List<int>(2);
                adjacency.Add(key, outgoing);
            }
            outgoing.Add(edgeIndex);
        }

        for (var firstEdge = 0; firstEdge < edges.Count; firstEdge++)
        {
            if (used[firstEdge])
            {
                continue;
            }

            var contour = new List<Vector2> { edges[firstEdge].Start, edges[firstEdge].End };
            used[firstEdge] = true;
            var previous = edges[firstEdge].Start;
            var current = edges[firstEdge].End;
            while (Vector2.DistanceSquared(current, contour[0]) > toleranceSquared)
            {
                var nextEdge = FindNextBoundaryEdge(
                    edges,
                    used,
                    adjacency,
                    previous,
                    current,
                    pointQuantum,
                    toleranceSquared);
                if (nextEdge < 0)
                {
                    return false;
                }

                used[nextEdge] = true;
                previous = current;
                current = edges[nextEdge].End;
                contour.Add(current);
                if (contour.Count > edges.Count + 1)
                {
                    return false;
                }
            }

            if (contour.Count < 4)
            {
                continue;
            }

            var figure = new PathFigure(contour[0], isClosed: true);
            for (var pointIndex = 1; pointIndex < contour.Count - 1; pointIndex++)
            {
                figure.Segments.Add(new LineSegment(contour[pointIndex]));
            }
            result.Figures.Add(figure);
        }

        return true;
    }

    private static int FindNextBoundaryEdge(
        List<DirectedEdge> edges,
        bool[] used,
        Dictionary<PointKey, List<int>> adjacency,
        Vector2 previous,
        Vector2 current,
        float pointQuantum,
        float toleranceSquared)
    {
        if (!adjacency.TryGetValue(
                CreatePointKey(current, pointQuantum),
                out var candidates))
        {
            return -1;
        }

        var incoming = current - previous;
        var bestIndex = -1;
        var bestClockwiseAngle = float.PositiveInfinity;
        foreach (var edgeIndex in candidates)
        {
            if (used[edgeIndex] ||
                Vector2.DistanceSquared(edges[edgeIndex].Start, current) > toleranceSquared)
            {
                continue;
            }

            var outgoing = edges[edgeIndex].End - edges[edgeIndex].Start;
            var angle = MathF.Atan2(Cross(incoming, outgoing), Vector2.Dot(incoming, outgoing));
            if (angle < -1e-6f)
            {
                angle += MathF.Tau;
            }
            else if (MathF.Abs(angle) <= 1e-6f)
            {
                angle = 0f;
            }

            if (angle < bestClockwiseAngle)
            {
                bestClockwiseAngle = angle;
                bestIndex = edgeIndex;
            }
        }

        return bestIndex;
    }

    private static DirectedEdgeKey CreateEdgeKey(
        in DirectedEdge edge,
        float quantum) =>
        new(CreatePointKey(edge.Start, quantum), CreatePointKey(edge.End, quantum));

    private static PointKey CreatePointKey(Vector2 point, float quantum) =>
        new(Quantize(point.X, quantum), Quantize(point.Y, quantum));

    private static long Quantize(float value, float quantum)
    {
        var scaled = Math.Round((double)value / quantum);
        if (scaled >= long.MaxValue)
        {
            return long.MaxValue;
        }
        if (scaled <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)scaled;
    }

    private static float Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private readonly record struct DirectedEdge(Vector2 Start, Vector2 End);

    private readonly record struct PointKey(long X, long Y);

    private readonly record struct DirectedEdgeKey(PointKey Start, PointKey End);
}
