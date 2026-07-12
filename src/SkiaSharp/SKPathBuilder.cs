using System.Numerics;
using ProGPU.Vector;

namespace SkiaSharp;

public class SKPathBuilder : SKObject
{
    private const int ConicSubdivisionCount = 16;
    private const float Epsilon = 0.00001f;

    private SKPath _path;
    private PathFigure? _currentFigure;
    private Vector2 _currentPoint;
    private Vector2 _contourStart;

    public SKPathFillType FillType
    {
        get => _path.FillType;
        set => _path.FillType = value;
    }

    public SKPathBuilder()
        : base(SKObjectHandle.Create(), owns: true)
    {
        _path = new SKPath();
    }

    public SKPathBuilder(SKPath path)
        : base(SKObjectHandle.Create(), owns: true)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = new SKPath(path);
        RestoreCurrentState();
    }

    public SKPath Detach()
    {
        var result = _path;
        _path = new SKPath();
        ResetCurrentState();
        return result;
    }

    public SKPath Snapshot() => new(_path);

    public void Reset()
    {
        _path.Reset();
        ResetCurrentState();
    }

    public void MoveTo(SKPoint point) => MoveTo(point.X, point.Y);

    public void MoveTo(float x, float y)
    {
        var point = new Vector2(x, y);
        _currentFigure = new PathFigure(point);
        _path.Geometry.Figures.Add(_currentFigure);
        _currentPoint = point;
        _contourStart = point;
    }

    public void RMoveTo(SKPoint point) => RMoveTo(point.X, point.Y);

    public void RMoveTo(float dx, float dy) =>
        MoveTo(_currentPoint.X + dx, _currentPoint.Y + dy);

    public void LineTo(SKPoint point) => LineTo(point.X, point.Y);

    public void LineTo(float x, float y)
    {
        EnsureContour();
        var point = new Vector2(x, y);
        _currentFigure!.Segments.Add(new LineSegment(point));
        _currentPoint = point;
    }

    public void RLineTo(SKPoint point) => RLineTo(point.X, point.Y);

    public void RLineTo(float dx, float dy) =>
        LineTo(_currentPoint.X + dx, _currentPoint.Y + dy);

    public void QuadTo(SKPoint point0, SKPoint point1) =>
        QuadTo(point0.X, point0.Y, point1.X, point1.Y);

    public void QuadTo(float x0, float y0, float x1, float y1)
    {
        EnsureContour();
        var end = new Vector2(x1, y1);
        _currentFigure!.Segments.Add(new QuadraticBezierSegment(new Vector2(x0, y0), end));
        _currentPoint = end;
    }

    public void RQuadTo(SKPoint point0, SKPoint point1) =>
        RQuadTo(point0.X, point0.Y, point1.X, point1.Y);

    public void RQuadTo(float dx0, float dy0, float dx1, float dy1)
    {
        var start = _currentPoint;
        QuadTo(
            start.X + dx0,
            start.Y + dy0,
            start.X + dx1,
            start.Y + dy1);
    }

    public void ConicTo(SKPoint point0, SKPoint point1, float w) =>
        ConicTo(point0.X, point0.Y, point1.X, point1.Y, w);

    public void ConicTo(float x0, float y0, float x1, float y1, float w)
    {
        EnsureContour();
        var control = new Vector2(x0, y0);
        var end = new Vector2(x1, y1);
        if (!float.IsFinite(w) || w <= 0f || MathF.Abs(w - 1f) <= Epsilon)
        {
            _currentFigure!.Segments.Add(new QuadraticBezierSegment(control, end));
            _currentPoint = end;
            return;
        }

        // Each span interpolates the rational conic at both ends and its midpoint.
        // Sixteen fixed spans match Skia's SVG conversion quality with bounded work.
        var start = _currentPoint;
        for (var index = 0; index < ConicSubdivisionCount; index++)
        {
            var t0 = index / (float)ConicSubdivisionCount;
            var t1 = (index + 1f) / ConicSubdivisionCount;
            var spanStart = EvaluateConic(start, control, end, w, t0);
            var spanEnd = EvaluateConic(start, control, end, w, t1);
            var midpoint = EvaluateConic(start, control, end, w, (t0 + t1) * 0.5f);
            var spanControl = 2f * midpoint - 0.5f * (spanStart + spanEnd);
            _currentFigure!.Segments.Add(new QuadraticBezierSegment(spanControl, spanEnd));
        }

        _currentPoint = end;
    }

    public void RConicTo(SKPoint point0, SKPoint point1, float w) =>
        RConicTo(point0.X, point0.Y, point1.X, point1.Y, w);

    public void RConicTo(float dx0, float dy0, float dx1, float dy1, float w)
    {
        var start = _currentPoint;
        ConicTo(
            start.X + dx0,
            start.Y + dy0,
            start.X + dx1,
            start.Y + dy1,
            w);
    }

    public void CubicTo(SKPoint point0, SKPoint point1, SKPoint point2) =>
        CubicTo(point0.X, point0.Y, point1.X, point1.Y, point2.X, point2.Y);

    public void CubicTo(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        EnsureContour();
        var end = new Vector2(x2, y2);
        _currentFigure!.Segments.Add(new CubicBezierSegment(
            new Vector2(x0, y0),
            new Vector2(x1, y1),
            end));
        _currentPoint = end;
    }

    public void RCubicTo(SKPoint point0, SKPoint point1, SKPoint point2) =>
        RCubicTo(point0.X, point0.Y, point1.X, point1.Y, point2.X, point2.Y);

    public void RCubicTo(float dx0, float dy0, float dx1, float dy1, float dx2, float dy2)
    {
        var start = _currentPoint;
        CubicTo(
            start.X + dx0,
            start.Y + dy0,
            start.X + dx1,
            start.Y + dy1,
            start.X + dx2,
            start.Y + dy2);
    }

    public void ArcTo(
        SKPoint r,
        float xAxisRotate,
        SKPathArcSize largeArc,
        SKPathDirection sweep,
        SKPoint xy) =>
        ArcTo(r.X, r.Y, xAxisRotate, largeArc, sweep, xy.X, xy.Y);

    public void ArcTo(
        float rx,
        float ry,
        float xAxisRotate,
        SKPathArcSize largeArc,
        SKPathDirection sweep,
        float x,
        float y)
    {
        EnsureContour();
        var end = new Vector2(x, y);
        if (!float.IsFinite(rx) || !float.IsFinite(ry) || MathF.Abs(rx) <= Epsilon || MathF.Abs(ry) <= Epsilon)
        {
            LineTo(x, y);
            return;
        }

        _currentFigure!.Segments.Add(new ArcSegment(
            end,
            new Vector2(MathF.Abs(rx), MathF.Abs(ry)),
            xAxisRotate,
            largeArc == SKPathArcSize.Large,
            ToSweepDirection(sweep)));
        _currentPoint = end;
    }

    public void ArcTo(SKRect oval, float startAngle, float sweepAngle, bool forceMoveTo) =>
        AppendOvalArc(oval, startAngle, sweepAngle, forceMoveTo);

    public void ArcTo(SKPoint point1, SKPoint point2, float radius) =>
        ArcTo(point1.X, point1.Y, point2.X, point2.Y, radius);

    public void ArcTo(float x1, float y1, float x2, float y2, float radius)
    {
        EnsureContour();
        var corner = new Vector2(x1, y1);
        var next = new Vector2(x2, y2);
        var incoming = _currentPoint - corner;
        var outgoing = next - corner;
        var incomingLength = incoming.Length();
        var outgoingLength = outgoing.Length();
        var normalizedRadius = MathF.Abs(radius);
        if (!float.IsFinite(normalizedRadius) || normalizedRadius <= Epsilon ||
            incomingLength <= Epsilon || outgoingLength <= Epsilon)
        {
            LineTo(x1, y1);
            return;
        }

        incoming /= incomingLength;
        outgoing /= outgoingLength;
        var dot = Math.Clamp(Vector2.Dot(incoming, outgoing), -1f, 1f);
        var cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
        var halfAngle = MathF.Acos(dot) * 0.5f;
        var tangent = MathF.Tan(halfAngle);
        if (MathF.Abs(cross) <= Epsilon || MathF.Abs(tangent) <= Epsilon)
        {
            LineTo(x1, y1);
            return;
        }

        var distance = normalizedRadius / tangent;
        if (!float.IsFinite(distance))
        {
            LineTo(x1, y1);
            return;
        }

        var tangentStart = corner + incoming * distance;
        var tangentEnd = corner + outgoing * distance;
        if (!Near(_currentPoint, tangentStart))
        {
            LineTo(tangentStart.X, tangentStart.Y);
        }

        _currentFigure!.Segments.Add(new ArcSegment(
            tangentEnd,
            new Vector2(normalizedRadius),
            0f,
            false,
            cross < 0f ? SweepDirection.Clockwise : SweepDirection.Counterclockwise));
        _currentPoint = tangentEnd;
    }

    public void RArcTo(
        SKPoint r,
        float xAxisRotate,
        SKPathArcSize largeArc,
        SKPathDirection sweep,
        SKPoint xy) =>
        RArcTo(r.X, r.Y, xAxisRotate, largeArc, sweep, xy.X, xy.Y);

    public void RArcTo(
        float rx,
        float ry,
        float xAxisRotate,
        SKPathArcSize largeArc,
        SKPathDirection sweep,
        float x,
        float y)
    {
        var start = _currentPoint;
        ArcTo(rx, ry, xAxisRotate, largeArc, sweep, start.X + x, start.Y + y);
    }

    public void Close()
    {
        if (_currentFigure is null)
        {
            return;
        }

        _currentFigure.IsClosed = true;
        _currentPoint = _contourStart;
        _currentFigure = null;
    }

    public void AddRect(SKRect rect, SKPathDirection direction = SKPathDirection.Clockwise) =>
        AddRect(rect, direction, 0);

    public void AddRect(SKRect rect, SKPathDirection direction, uint startIndex)
    {
        if (startIndex > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startIndex),
                "Starting index must be in the range of 0..3 (inclusive).");
        }

        Span<Vector2> points =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        var index = (int)startIndex;
        MoveTo(points[index].X, points[index].Y);
        var step = direction == SKPathDirection.Clockwise ? 1 : -1;
        for (var count = 0; count < 3; count++)
        {
            index = (index + step + 4) % 4;
            LineTo(points[index].X, points[index].Y);
        }

        Close();
    }

    public void AddRoundRect(SKRoundRect rect, SKPathDirection direction = SKPathDirection.Clockwise)
    {
        ArgumentNullException.ThrowIfNull(rect);
        AddRoundRect(
            rect,
            direction,
            direction == SKPathDirection.Clockwise ? 6u : 7u);
    }

    public void AddRoundRect(SKRoundRect rect, SKPathDirection direction, uint startIndex)
    {
        ArgumentNullException.ThrowIfNull(rect);
        AppendRoundRect(rect.Rect, rect.CornerRadii, direction, startIndex % 8);
    }

    public void AddOval(SKRect rect, SKPathDirection direction = SKPathDirection.Clockwise)
    {
        AppendOvalArc(
            rect,
            0f,
            direction == SKPathDirection.Clockwise ? 360f : -360f,
            forceMoveTo: true);
        Close();
    }

    public void AddArc(SKRect oval, float startAngle, float sweepAngle) =>
        AppendOvalArc(oval, startAngle, sweepAngle, forceMoveTo: true);

    public void AddRoundRect(
        SKRect rect,
        float rx,
        float ry,
        SKPathDirection dir = SKPathDirection.Clockwise)
    {
        using var roundRect = new SKRoundRect(rect, rx, ry);
        AddRoundRect(roundRect, dir);
    }

    public void AddCircle(
        float x,
        float y,
        float radius,
        SKPathDirection dir = SKPathDirection.Clockwise) =>
        AddOval(new SKRect(x - radius, y - radius, x + radius, y + radius), dir);

    public void AddPoly(ReadOnlySpan<SKPoint> points, bool close = true)
    {
        if (points.IsEmpty)
        {
            return;
        }

        MoveTo(points[0]);
        for (var index = 1; index < points.Length; index++)
        {
            LineTo(points[index]);
        }

        if (close)
        {
            Close();
        }
    }

    public void AddPoly(SKPoint[] points, bool close = true)
    {
        ArgumentNullException.ThrowIfNull(points);
        AddPoly(points.AsSpan(), close);
    }

    public void AddPath(
        SKPath other,
        float dx,
        float dy,
        SKPathAddMode mode = SKPathAddMode.Append)
    {
        ArgumentNullException.ThrowIfNull(other);
        AddPathCore(other, new Vector2(dx, dy), mode);
    }

    public void AddPath(
        SKPath other,
        in SKMatrix matrix,
        SKPathAddMode mode = SKPathAddMode.Append)
    {
        ArgumentNullException.ThrowIfNull(other);
        using var transformed = new SKPath(other);
        transformed.Transform(matrix);
        AddPathCore(transformed, Vector2.Zero, mode);
    }

    public void AddPath(SKPath other, SKPathAddMode mode = SKPathAddMode.Append)
    {
        ArgumentNullException.ThrowIfNull(other);
        AddPathCore(other, Vector2.Zero, mode);
    }

    public void ReverseAddPath(SKPath other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var figures = other.Geometry.Figures;
        for (var index = figures.Count - 1; index >= 0; index--)
        {
            var reversed = ReverseFigure(figures[index]);
            _path.Geometry.Figures.Add(reversed);
            SetCurrentFigure(reversed);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _path.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureContour()
    {
        if (_currentFigure is not null)
        {
            return;
        }

        _currentFigure = new PathFigure(_currentPoint);
        _path.Geometry.Figures.Add(_currentFigure);
        _contourStart = _currentPoint;
    }

    private void AppendOvalArc(SKRect oval, float startAngle, float sweepAngle, bool forceMoveTo)
    {
        var radiusX = MathF.Abs(oval.Width) * 0.5f;
        var radiusY = MathF.Abs(oval.Height) * 0.5f;
        var center = new Vector2(oval.MidX, oval.MidY);
        var sweep = Math.Clamp(sweepAngle, -360f, 360f);
        var start = GetOvalPoint(center, radiusX, radiusY, startAngle);
        if (forceMoveTo || _path.Geometry.Figures.Count == 0)
        {
            MoveTo(start.X, start.Y);
        }
        else
        {
            EnsureContour();
            if (!Near(_currentPoint, start))
            {
                LineTo(start.X, start.Y);
            }
        }

        if (!float.IsFinite(startAngle) || !float.IsFinite(sweep) ||
            radiusX <= Epsilon || radiusY <= Epsilon || MathF.Abs(sweep) <= Epsilon)
        {
            return;
        }

        var segmentCount = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / 180f));
        var segmentSweep = sweep / segmentCount;
        var direction = segmentSweep >= 0f ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        for (var index = 1; index <= segmentCount; index++)
        {
            var end = GetOvalPoint(center, radiusX, radiusY, startAngle + segmentSweep * index);
            _currentFigure!.Segments.Add(new ArcSegment(
                end,
                new Vector2(radiusX, radiusY),
                0f,
                MathF.Abs(segmentSweep) >= 180f - Epsilon,
                direction));
            _currentPoint = end;
        }
    }

    private void AppendRoundRect(
        SKRect rect,
        IReadOnlyList<SKPoint> sourceRadii,
        SKPathDirection direction,
        uint startIndex)
    {
        Span<Vector2> radii = stackalloc Vector2[4];
        for (var index = 0; index < radii.Length; index++)
        {
            radii[index] = new Vector2(
                MathF.Abs(sourceRadii[index].X),
                MathF.Abs(sourceRadii[index].Y));
        }

        NormalizeRoundRectRadii(rect, radii);
        Span<Vector2> points =
        [
            new(rect.Left + radii[0].X, rect.Top),
            new(rect.Right - radii[1].X, rect.Top),
            new(rect.Right, rect.Top + radii[1].Y),
            new(rect.Right, rect.Bottom - radii[2].Y),
            new(rect.Right - radii[2].X, rect.Bottom),
            new(rect.Left + radii[3].X, rect.Bottom),
            new(rect.Left, rect.Bottom - radii[3].Y),
            new(rect.Left, rect.Top + radii[0].Y),
        ];

        var currentIndex = (int)startIndex;
        MoveTo(points[currentIndex].X, points[currentIndex].Y);
        var step = direction == SKPathDirection.Clockwise ? 1 : -1;
        for (var count = 0; count < 8; count++)
        {
            var nextIndex = (currentIndex + step + 8) % 8;
            var isArc = direction == SKPathDirection.Clockwise
                ? currentIndex % 2 == 1
                : currentIndex % 2 == 0;
            if (isArc)
            {
                var cornerIndex = direction == SKPathDirection.Clockwise
                    ? ((currentIndex + 1) / 2) % 4
                    : currentIndex / 2;
                var radius = radii[cornerIndex];
                if (radius.X > Epsilon && radius.Y > Epsilon)
                {
                    _currentFigure!.Segments.Add(new ArcSegment(
                        points[nextIndex],
                        radius,
                        0f,
                        false,
                        ToSweepDirection(direction)));
                    _currentPoint = points[nextIndex];
                }
                else
                {
                    LineTo(points[nextIndex].X, points[nextIndex].Y);
                }
            }
            else
            {
                LineTo(points[nextIndex].X, points[nextIndex].Y);
            }

            currentIndex = nextIndex;
        }

        Close();
    }

    private void AddPathCore(SKPath other, Vector2 offset, SKPathAddMode mode)
    {
        var figures = other.Geometry.Figures;
        var firstIndex = 0;
        if (mode == SKPathAddMode.Extend && _currentFigure is not null && figures.Count > 0)
        {
            var source = figures[0];
            var sourceStart = source.StartPoint + offset;
            LineTo(sourceStart.X, sourceStart.Y);
            foreach (var segment in source.Segments)
            {
                var clone = CloneSegment(segment, offset);
                _currentFigure!.Segments.Add(clone);
                _currentPoint = GetSegmentEnd(clone);
            }

            if (source.IsClosed)
            {
                Close();
            }

            firstIndex = 1;
        }

        for (var index = firstIndex; index < figures.Count; index++)
        {
            var clone = CloneFigure(figures[index], offset);
            _path.Geometry.Figures.Add(clone);
            SetCurrentFigure(clone);
        }
    }

    private void SetCurrentFigure(PathFigure figure)
    {
        _contourStart = figure.StartPoint;
        if (figure.IsClosed)
        {
            _currentFigure = null;
            _currentPoint = figure.StartPoint;
        }
        else
        {
            _currentFigure = figure;
            _currentPoint = GetFigureEnd(figure);
        }
    }

    private void RestoreCurrentState()
    {
        var figures = _path.Geometry.Figures;
        if (figures.Count == 0)
        {
            ResetCurrentState();
            return;
        }

        SetCurrentFigure(figures[^1]);
    }

    private void ResetCurrentState()
    {
        _currentFigure = null;
        _currentPoint = Vector2.Zero;
        _contourStart = Vector2.Zero;
    }

    private static PathFigure CloneFigure(PathFigure source, Vector2 offset)
    {
        var clone = new PathFigure(source.StartPoint + offset, source.IsClosed)
        {
            IsFilled = source.IsFilled,
        };
        foreach (var segment in source.Segments)
        {
            clone.Segments.Add(CloneSegment(segment, offset));
        }

        return clone;
    }

    private static PathSegment CloneSegment(PathSegment segment, Vector2 offset) => segment switch
    {
        LineSegment line => new LineSegment(line.Point + offset, line.IsSmoothJoin, line.IsStroked),
        QuadraticBezierSegment quad => new QuadraticBezierSegment(
            quad.ControlPoint + offset,
            quad.Point + offset,
            quad.IsSmoothJoin,
            quad.IsStroked),
        CubicBezierSegment cubic => new CubicBezierSegment(
            cubic.ControlPoint1 + offset,
            cubic.ControlPoint2 + offset,
            cubic.Point + offset,
            cubic.IsSmoothJoin,
            cubic.IsStroked),
        ArcSegment arc => new ArcSegment(
            arc.Point + offset,
            arc.Size,
            arc.RotationAngle,
            arc.IsLargeArc,
            arc.SweepDirection,
            arc.IsSmoothJoin,
            arc.IsStroked),
        _ => throw new NotSupportedException($"Unsupported path segment '{segment.GetType().FullName}'."),
    };

    private static PathFigure ReverseFigure(PathFigure source)
    {
        var starts = new Vector2[source.Segments.Count];
        var current = source.StartPoint;
        for (var index = 0; index < source.Segments.Count; index++)
        {
            starts[index] = current;
            current = GetSegmentEnd(source.Segments[index]);
        }

        var reversed = new PathFigure(current, source.IsClosed)
        {
            IsFilled = source.IsFilled,
        };
        for (var index = source.Segments.Count - 1; index >= 0; index--)
        {
            var end = starts[index];
            reversed.Segments.Add(source.Segments[index] switch
            {
                LineSegment line => new LineSegment(end, line.IsSmoothJoin, line.IsStroked),
                QuadraticBezierSegment quad => new QuadraticBezierSegment(
                    quad.ControlPoint,
                    end,
                    quad.IsSmoothJoin,
                    quad.IsStroked),
                CubicBezierSegment cubic => new CubicBezierSegment(
                    cubic.ControlPoint2,
                    cubic.ControlPoint1,
                    end,
                    cubic.IsSmoothJoin,
                    cubic.IsStroked),
                ArcSegment arc => new ArcSegment(
                    end,
                    arc.Size,
                    arc.RotationAngle,
                    arc.IsLargeArc,
                    arc.SweepDirection == SweepDirection.Clockwise
                        ? SweepDirection.Counterclockwise
                        : SweepDirection.Clockwise,
                    arc.IsSmoothJoin,
                    arc.IsStroked),
                _ => throw new NotSupportedException(
                    $"Unsupported path segment '{source.Segments[index].GetType().FullName}'."),
            });
        }

        return reversed;
    }

    private static Vector2 GetFigureEnd(PathFigure figure) =>
        figure.Segments.Count == 0 ? figure.StartPoint : GetSegmentEnd(figure.Segments[^1]);

    private static Vector2 GetSegmentEnd(PathSegment segment) => segment switch
    {
        LineSegment line => line.Point,
        QuadraticBezierSegment quad => quad.Point,
        CubicBezierSegment cubic => cubic.Point,
        ArcSegment arc => arc.Point,
        _ => throw new NotSupportedException($"Unsupported path segment '{segment.GetType().FullName}'."),
    };

    private static Vector2 EvaluateConic(
        Vector2 start,
        Vector2 control,
        Vector2 end,
        float weight,
        float t)
    {
        var inverse = 1f - t;
        var startFactor = inverse * inverse;
        var controlFactor = 2f * weight * inverse * t;
        var endFactor = t * t;
        var denominator = startFactor + controlFactor + endFactor;
        return (startFactor * start + controlFactor * control + endFactor * end) / denominator;
    }

    private static Vector2 GetOvalPoint(
        Vector2 center,
        float radiusX,
        float radiusY,
        float angleDegrees)
    {
        var angle = angleDegrees * MathF.PI / 180f;
        return new Vector2(
            center.X + radiusX * MathF.Cos(angle),
            center.Y + radiusY * MathF.Sin(angle));
    }

    private static void NormalizeRoundRectRadii(SKRect rect, Span<Vector2> radii)
    {
        var width = MathF.Abs(rect.Width);
        var height = MathF.Abs(rect.Height);
        var scale = 1f;
        ScaleToFit(width, radii[0].X + radii[1].X, ref scale);
        ScaleToFit(width, radii[3].X + radii[2].X, ref scale);
        ScaleToFit(height, radii[0].Y + radii[3].Y, ref scale);
        ScaleToFit(height, radii[1].Y + radii[2].Y, ref scale);
        if (scale >= 1f)
        {
            return;
        }

        for (var index = 0; index < radii.Length; index++)
        {
            radii[index] *= scale;
        }
    }

    private static void ScaleToFit(float available, float requested, ref float scale)
    {
        if (requested > available && requested > Epsilon)
        {
            scale = MathF.Min(scale, available / requested);
        }
    }

    private static SweepDirection ToSweepDirection(SKPathDirection direction) =>
        direction == SKPathDirection.Clockwise
            ? SweepDirection.Clockwise
            : SweepDirection.Counterclockwise;

    private static bool Near(Vector2 left, Vector2 right) =>
        Vector2.DistanceSquared(left, right) <= Epsilon * Epsilon;
}
