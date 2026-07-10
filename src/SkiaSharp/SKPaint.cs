using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Scene;
using ProGPU.Vector;

namespace SkiaSharp;

public enum SKPaintStyle
{
    Fill = 0,
    Stroke = 1,
    StrokeAndFill = 2,
}

public class SKPaint : IDisposable
{
    private const float HairlineStrokeWidth = 1f;
    private SKShader? _shader;

    public SKPaintStyle Style { get; set; } = SKPaintStyle.Fill;
    public SKColor Color { get; set; } = SKColors.Black;
    public SKColorF ColorF
    {
        get => new(Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);
        set => Color = new SKColor(
            ToColorByte(value.R),
            ToColorByte(value.G),
            ToColorByte(value.B),
            ToColorByte(value.A));
    }
    public bool IsStroke
    {
        get => Style == SKPaintStyle.Stroke;
        set => Style = value ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
    }
    public float StrokeWidth { get; set; } = 1f;
    public float StrokeMiter { get; set; } = 4f;
    public SKStrokeCap StrokeCap { get; set; } = SKStrokeCap.Butt;
    public SKStrokeJoin StrokeJoin { get; set; } = SKStrokeJoin.Miter;
    public SKShader? Shader
    {
        get => _shader;
        set
        {
            if (ReferenceEquals(_shader, value))
            {
                return;
            }

            value?.AddReference();
            _shader?.ReleaseReference();
            _shader = value;
        }
    }
    public SKColorFilter? ColorFilter { get; set; }
    public SKImageFilter? ImageFilter { get; set; }
    public SKPathEffect? PathEffect { get; set; }
    public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;
    public bool IsAntialias { get; set; } = true;
    public SKTypeface? Typeface { get; set; }
    public float TextSize { get; set; } = 12f;

    public SKPaint Clone()
    {
        return new SKPaint
        {
            Style = Style,
            Color = Color,
            StrokeWidth = StrokeWidth,
            StrokeMiter = StrokeMiter,
            StrokeCap = StrokeCap,
            StrokeJoin = StrokeJoin,
            Shader = Shader,
            ColorFilter = ColorFilter,
            ImageFilter = ImageFilter,
            PathEffect = PathEffect,
            BlendMode = BlendMode,
            IsAntialias = IsAntialias,
            Typeface = Typeface,
            TextSize = TextSize
        };
    }

    public Brush? ToBrush()
    {
        if (Style == SKPaintStyle.Stroke) return null;

        if (Shader != null)
        {
            ThrowIfShaderColorFilter();
            return ApplyPaintAlphaToShaderBrush(Shader.ToBrush(), Color);
        }

        var color = GetFilteredColor();
        var c = new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
        return new SolidColorBrush(c);
    }

    public Pen? ToPen()
    {
        return ToPen(1f);
    }

    public Pen? ToPen(float strokeScale)
    {
        if (Style == SKPaintStyle.Fill) return null;

        var scaledStrokeWidth = ScaleStrokeWidth(StrokeWidth, strokeScale);
        Brush penBrush;
        if (Shader != null)
        {
            ThrowIfShaderColorFilter();
            penBrush = ApplyPaintAlphaToShaderBrush(Shader.ToBrush(), Color);
        }
        else
        {
            var color = GetFilteredColor();
            var c = new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
            penBrush = new SolidColorBrush(c);
        }
        var (dashArray, dashOffset) = MapDashEffect(PathEffect, scaledStrokeWidth);

        return new Pen(
            penBrush,
            scaledStrokeWidth,
            MapStrokeJoin(StrokeJoin),
            StrokeMiter,
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            dashArray,
            dashOffset);
    }

    internal Pen? ToLocalPen(float strokeScale)
    {
        if (Style == SKPaintStyle.Fill) return null;

        var localStrokeWidth = StrokeWidth;
        if (localStrokeWidth == 0f)
        {
            localStrokeWidth = float.IsFinite(strokeScale) && strokeScale > 0f
                ? HairlineStrokeWidth / strokeScale
                : HairlineStrokeWidth;
        }

        Brush penBrush;
        if (Shader != null)
        {
            ThrowIfShaderColorFilter();
            penBrush = ApplyPaintAlphaToShaderBrush(Shader.ToBrush(), Color);
        }
        else
        {
            var color = GetFilteredColor();
            penBrush = new SolidColorBrush(new Vector4(
                color.R / 255.0f,
                color.G / 255.0f,
                color.B / 255.0f,
                color.A / 255.0f));
        }

        var (dashArray, dashOffset) = MapDashEffect(PathEffect, localStrokeWidth);
        return new Pen(
            penBrush,
            localStrokeWidth,
            MapStrokeJoin(StrokeJoin),
            StrokeMiter,
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            dashArray,
            dashOffset);
    }

    internal Pen ToPen(Brush brush, float strokeScale)
    {
        var scaledStrokeWidth = ScaleStrokeWidth(StrokeWidth, strokeScale);
        var (dashArray, dashOffset) = MapDashEffect(PathEffect, scaledStrokeWidth);
        return new Pen(
            brush,
            scaledStrokeWidth,
            MapStrokeJoin(StrokeJoin),
            StrokeMiter,
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            MapStrokeCap(StrokeCap),
            dashArray,
            dashOffset);
    }

    public void Reset()
    {
        Style = SKPaintStyle.Fill;
        Color = SKColors.Black;
        StrokeWidth = 1f;
        StrokeMiter = 4f;
        StrokeCap = SKStrokeCap.Butt;
        StrokeJoin = SKStrokeJoin.Miter;
        Shader = null;
        ColorFilter = null;
        ImageFilter = null;
        PathEffect = null;
        BlendMode = SKBlendMode.SrcOver;
        IsAntialias = true;
        Typeface = null;
        TextSize = 12f;
    }

    public void Dispose()
    {
        Shader = null;
    }

    public bool GetFillPath(SKPath source, SKPath destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Reset();
        if (Style == SKPaintStyle.Fill)
        {
            destination.AddPath(source);
            return true;
        }

        if (Style == SKPaintStyle.StrokeAndFill)
        {
            destination.AddPath(source);
        }

        var halfWidth = MathF.Max(StrokeWidth, HairlineStrokeWidth) / 2f;
        foreach (var figure in source.Geometry.Figures)
        {
            if (TryAddOvalStroke(destination, figure, halfWidth))
            {
                continue;
            }

            var points = FlattenFigure(figure);
            if (PathEffect is { Intervals.Length: > 0 } pathEffect)
            {
                AddDashedStrokeSegments(destination, points, figure.IsClosed, halfWidth, pathEffect);
                continue;
            }

            for (var i = 1; i < points.Count; i++)
            {
                AddStrokeSegment(destination, points[i - 1], points[i], halfWidth);
            }

            if (figure.IsClosed && points.Count > 1)
            {
                AddStrokeSegment(destination, points[^1], points[0], halfWidth);
            }

            if (StrokeJoin == SKStrokeJoin.Round || StrokeCap == SKStrokeCap.Round)
            {
                foreach (var point in points)
                {
                    destination.AddCircle(point.X, point.Y, halfWidth);
                }
            }
        }

        return !destination.IsEmpty;
    }

    private static bool TryAddOvalStroke(SKPath destination, PathFigure figure, float halfWidth)
    {
        if (!figure.IsClosed || figure.Segments.Count != 2
            || figure.Segments[0] is not ArcSegment first
            || figure.Segments[1] is not ArcSegment second
            || !first.IsLargeArc
            || !second.IsLargeArc
            || first.SweepDirection != second.SweepDirection
            || MathF.Abs(first.RotationAngle) > 0.0001f
            || MathF.Abs(second.RotationAngle) > 0.0001f
            || Vector2.DistanceSquared(first.Size, second.Size) > 0.0001f
            || Vector2.DistanceSquared(second.Point, figure.StartPoint) > 0.0001f
            || MathF.Abs(MathF.Abs(first.Point.X - figure.StartPoint.X) - 2f * first.Size.X) > 0.0001f
            || MathF.Abs(first.Point.Y - figure.StartPoint.Y) > 0.0001f)
        {
            return false;
        }

        var center = (figure.StartPoint + first.Point) / 2f;
        var radiusX = MathF.Abs(first.Size.X);
        var radiusY = MathF.Abs(first.Size.Y);
        var direction = first.SweepDirection == SweepDirection.Clockwise
            ? SKPathDirection.Clockwise
            : SKPathDirection.CounterClockwise;
        destination.AddOval(
            new SKRect(
                center.X - radiusX - halfWidth,
                center.Y - radiusY - halfWidth,
                center.X + radiusX + halfWidth,
                center.Y + radiusY + halfWidth),
            direction);

        var innerRadiusX = radiusX - halfWidth;
        var innerRadiusY = radiusY - halfWidth;
        if (innerRadiusX > 0f && innerRadiusY > 0f)
        {
            destination.AddOval(
                new SKRect(
                    center.X - innerRadiusX,
                    center.Y - innerRadiusY,
                    center.X + innerRadiusX,
                    center.Y + innerRadiusY),
                direction == SKPathDirection.Clockwise
                    ? SKPathDirection.CounterClockwise
                    : SKPathDirection.Clockwise);
        }

        return true;
    }

    private void AddDashedStrokeSegments(
        SKPath destination,
        List<Vector2> points,
        bool isClosed,
        float halfWidth,
        SKPathEffect pathEffect)
    {
        if (points.Count < 2)
        {
            return;
        }

        var intervals = pathEffect.Intervals;
        var patternLength = 0f;
        for (var i = 0; i < intervals.Length; i++)
        {
            if (float.IsFinite(intervals[i]) && intervals[i] > 0f)
            {
                patternLength += intervals[i];
            }
        }

        if (patternLength <= 0f)
        {
            return;
        }

        var phase = pathEffect.Phase % patternLength;
        if (phase < 0f)
        {
            phase += patternLength;
        }

        var patternIndex = 0;
        while (phase >= intervals[patternIndex] && intervals[patternIndex] > 0f)
        {
            phase -= intervals[patternIndex];
            patternIndex = (patternIndex + 1) % intervals.Length;
        }

        var remainingInPattern = MathF.Max(0f, intervals[patternIndex] - phase);
        var segmentCount = isClosed ? points.Count : points.Count - 1;
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var start = points[segmentIndex];
            var end = points[(segmentIndex + 1) % points.Count];
            var delta = end - start;
            var length = delta.Length();
            if (!float.IsFinite(length) || length <= 0.0001f)
            {
                continue;
            }

            var direction = delta / length;
            var distance = 0f;
            while (distance < length - 0.0001f)
            {
                if (remainingInPattern <= 0.0001f)
                {
                    AdvanceDashPattern(intervals, ref patternIndex, ref remainingInPattern);
                }

                var step = MathF.Min(remainingInPattern, length - distance);
                if ((patternIndex & 1) == 0 && step > 0.0001f)
                {
                    var dashStart = start + direction * distance;
                    var dashEnd = start + direction * (distance + step);
                    if (StrokeCap == SKStrokeCap.Square)
                    {
                        dashStart -= direction * halfWidth;
                        dashEnd += direction * halfWidth;
                    }

                    AddStrokeSegment(destination, dashStart, dashEnd, halfWidth);
                    if (StrokeCap == SKStrokeCap.Round)
                    {
                        destination.AddCircle(dashStart.X, dashStart.Y, halfWidth);
                        destination.AddCircle(dashEnd.X, dashEnd.Y, halfWidth);
                    }
                }

                distance += step;
                remainingInPattern -= step;
            }
        }
    }

    private static void AdvanceDashPattern(
        float[] intervals,
        ref int patternIndex,
        ref float remainingInPattern)
    {
        for (var i = 0; i < intervals.Length; i++)
        {
            patternIndex = (patternIndex + 1) % intervals.Length;
            remainingInPattern = intervals[patternIndex];
            if (remainingInPattern > 0.0001f)
            {
                return;
            }
        }
    }

    private static List<Vector2> FlattenFigure(PathFigure figure)
    {
        const int curveSegments = 24;
        var result = new List<Vector2> { figure.StartPoint };
        var current = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment line:
                    result.Add(line.Point);
                    current = line.Point;
                    break;
                case QuadraticBezierSegment quadratic:
                    for (var i = 1; i <= curveSegments; i++)
                    {
                        result.Add(BezierSegmentGeometry.EvaluateQuadratic(
                            current,
                            quadratic.ControlPoint,
                            quadratic.Point,
                            (float)i / curveSegments));
                    }

                    current = quadratic.Point;
                    break;
                case CubicBezierSegment cubic:
                    for (var i = 1; i <= curveSegments; i++)
                    {
                        result.Add(BezierSegmentGeometry.EvaluateCubic(
                            current,
                            cubic.ControlPoint1,
                            cubic.ControlPoint2,
                            cubic.Point,
                            (float)i / curveSegments));
                    }

                    current = cubic.Point;
                    break;
                case ArcSegment arc:
                    var arcPoints = ArcSegmentGeometry.FlattenArc(current, arc, MathF.PI / 24f);
                    for (var i = 1; i < arcPoints.Length; i++)
                    {
                        result.Add(arcPoints[i]);
                    }

                    current = arc.Point;
                    break;
            }
        }

        return result;
    }

    private static void AddStrokeSegment(SKPath path, Vector2 start, Vector2 end, float halfWidth)
    {
        var direction = end - start;
        if (direction.LengthSquared() <= 0.0000001f)
        {
            return;
        }

        direction = Vector2.Normalize(direction);
        var normal = new Vector2(-direction.Y, direction.X) * halfWidth;
        path.MoveTo(start.X + normal.X, start.Y + normal.Y);
        path.LineTo(end.X + normal.X, end.Y + normal.Y);
        path.LineTo(end.X - normal.X, end.Y - normal.Y);
        path.LineTo(start.X - normal.X, start.Y - normal.Y);
        path.Close();
    }

    private static byte ToColorByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }

    public void ThrowIfImageColorFilter()
    {
        if (ColorFilter != null)
        {
            throw new NotSupportedException("SKPaint.ColorFilter on image draws requires a native texture color-filter pipeline.");
        }
    }

    private SKColor GetFilteredColor()
    {
        return ColorFilter?.Apply(Color) ?? Color;
    }

    private static Brush ApplyPaintAlphaToShaderBrush(Brush brush, SKColor paintColor)
    {
        brush.Opacity *= paintColor.A / 255.0f;
        return brush;
    }

    private static float ScaleStrokeWidth(float strokeWidth, float strokeScale)
    {
        if (strokeWidth == 0f)
        {
            return HairlineStrokeWidth;
        }

        if (!float.IsFinite(strokeScale) || strokeScale <= 0f)
        {
            return strokeWidth;
        }

        return strokeWidth * strokeScale;
    }

    private void ThrowIfShaderColorFilter()
    {
        if (ColorFilter != null)
        {
            throw new NotSupportedException("SKPaint.ColorFilter combined with SKShader requires a native shader color-filter pipeline.");
        }
    }

    private static PenLineCap MapStrokeCap(SKStrokeCap cap)
    {
        return cap switch
        {
            SKStrokeCap.Round => PenLineCap.Round,
            SKStrokeCap.Square => PenLineCap.Square,
            _ => PenLineCap.Flat
        };
    }

    private static PenLineJoin MapStrokeJoin(SKStrokeJoin join)
    {
        return join switch
        {
            SKStrokeJoin.Round => PenLineJoin.Round,
            SKStrokeJoin.Bevel => PenLineJoin.Bevel,
            _ => PenLineJoin.Miter
        };
    }

    private static (double[]? DashArray, double DashOffset) MapDashEffect(SKPathEffect? pathEffect, float strokeWidth)
    {
        if (pathEffect == null)
        {
            return (null, 0.0);
        }

        if (!float.IsFinite(strokeWidth) || strokeWidth <= 0f)
        {
            throw new NotSupportedException("Dash path effects require a positive finite stroke width.");
        }

        if (pathEffect.Intervals.Length == 0 || (pathEffect.Intervals.Length % 2) != 0)
        {
            throw new NotSupportedException("Dash path effects require an even number of intervals.");
        }

        var dashArray = new double[pathEffect.Intervals.Length];
        for (var i = 0; i < pathEffect.Intervals.Length; i++)
        {
            var interval = pathEffect.Intervals[i];
            if (!float.IsFinite(interval) || interval < 0f)
            {
                throw new NotSupportedException("Dash path effect intervals must be finite and non-negative.");
            }

            dashArray[i] = interval / strokeWidth;
        }

        if (!float.IsFinite(pathEffect.Phase))
        {
            throw new NotSupportedException("Dash path effect phase must be finite.");
        }

        return (dashArray, pathEffect.Phase / strokeWidth);
    }
}

public class SKShader : IDisposable
{
    private readonly Func<Brush>? _brushCreator;
    private readonly PictureShaderData? _picture;
    private readonly ImageShaderData? _image;
    private readonly ComposedShaderData? _composed;
    private SKColorFilter? _colorFilter;
    private int _referenceCount = 1;
    private bool _disposed;

    private SKShader(Func<Brush> brushCreator)
    {
        _brushCreator = brushCreator;
    }

    private SKShader(PictureShaderData picture)
    {
        _picture = picture;
    }

    private SKShader(ImageShaderData image)
    {
        _image = image;
    }

    private SKShader(ComposedShaderData composed)
    {
        _composed = composed;
    }

    public Brush ToBrush()
    {
        if (_brushCreator == null)
        {
            throw new NotSupportedException("Picture shaders are rendered by SKCanvas and cannot be converted to a vector brush.");
        }

        return ApplyColorFilter(_brushCreator(), _colorFilter);
    }

    internal PictureShaderData? Picture => _picture;
    internal ImageShaderData? Image => _image;
    internal ComposedShaderData? Composed => _composed;
    internal SKColorFilter? ColorFilter => _colorFilter;

    internal static SKShader CreatePicture(
        GpuPicture picture,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix,
        SKRect tileRect)
    {
        return new SKShader(new PictureShaderData(picture, tileModeX, tileModeY, localMatrix, tileRect));
    }

    internal static SKShader CreateImage(
        SKImage image,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix)
    {
        return new SKShader(new ImageShaderData(image, tileModeX, tileModeY, localMatrix));
    }

    internal void AddReference()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SKShader));
        }

        checked
        {
            _referenceCount++;
        }
    }

    internal void ReleaseReference()
    {
        if (_referenceCount <= 0)
        {
            return;
        }

        _referenceCount--;
        if (_referenceCount == 0)
        {
            _picture?.Dispose();
            _image?.Dispose();
            _composed?.Dispose();
        }
    }

    public static SKShader CreateColor(SKColor color)
    {
        return new SKShader(() =>
        {
            var c = new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
            return new SolidColorBrush(c);
        });
    }

    public static SKShader CreateLinearGradient(
        SKPoint start,
        SKPoint end,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() =>
        {
            var stops = CreateGradientStops(colors, colorPos);
            return new LinearGradientBrush(new Vector2(start.X, start.Y), new Vector2(end.X, end.Y), stops)
            {
                SpreadMethod = spreadMethod
            };
        });
    }

    public static SKShader CreateLinearGradient(
        SKPoint start,
        SKPoint end,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode,
        SKMatrix localMatrix)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() => new LinearGradientBrush(
            new Vector2(start.X, start.Y),
            new Vector2(end.X, end.Y),
            CreateGradientStops(colors, colorPos))
        {
            SpreadMethod = spreadMethod,
            CoordinateTransform = GetShaderCoordinateTransform(localMatrix)
        });
    }

    public static SKShader CreateRadialGradient(
        SKPoint center,
        float radius,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() =>
        {
            var stops = CreateGradientStops(colors, colorPos);
            return new RadialGradientBrush(new Vector2(center.X, center.Y), radius, stops)
            {
                SpreadMethod = spreadMethod
            };
        });
    }

    public static SKShader CreateRadialGradient(
        SKPoint center,
        float radius,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode,
        SKMatrix localMatrix)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() => new RadialGradientBrush(
            new Vector2(center.X, center.Y),
            radius,
            CreateGradientStops(colors, colorPos))
        {
            SpreadMethod = spreadMethod,
            CoordinateTransform = GetShaderCoordinateTransform(localMatrix)
        });
    }

    public static SKShader CreateTwoPointConicalGradient(
        SKPoint start,
        float startRadius,
        SKPoint end,
        float endRadius,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() =>
        {
            var stops = CreateGradientStops(colors, colorPos);

            return new TwoPointConicalGradientBrush(
                new Vector2(start.X, start.Y),
                startRadius,
                new Vector2(end.X, end.Y),
                endRadius,
                stops)
            {
                SpreadMethod = spreadMethod
            };
        });
    }

    public static SKShader CreateTwoPointConicalGradient(
        SKPoint start,
        float startRadius,
        SKPoint end,
        float endRadius,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode mode,
        SKMatrix localMatrix)
    {
        var spreadMethod = MapTileMode(mode);
        return new SKShader(() => new TwoPointConicalGradientBrush(
            new Vector2(start.X, start.Y),
            startRadius,
            new Vector2(end.X, end.Y),
            endRadius,
            CreateGradientStops(colors, colorPos))
        {
            SpreadMethod = spreadMethod,
            CoordinateTransform = GetShaderCoordinateTransform(localMatrix)
        });
    }

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColor[] colors,
        float[]? colorPos,
        SKMatrix localMatrix)
    {
        return new SKShader(() => new SweepGradientBrush(
            new Vector2(center.X, center.Y),
            CreateGradientStops(colors, colorPos))
        {
            CoordinateTransform = GetShaderCoordinateTransform(localMatrix)
        });
    }

    public static SKShader CreateBitmap(
        SKBitmap bitmap,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return CreateImage(SKImage.FromBitmap(bitmap), tileModeX, tileModeY, SKMatrix.Identity);
    }

    public static SKShader CreateCompose(SKShader destination, SKShader source)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        return new SKShader(new ComposedShaderData(destination, source));
    }

    public SKShader WithColorFilter(SKColorFilter colorFilter)
    {
        _colorFilter = colorFilter ?? throw new ArgumentNullException(nameof(colorFilter));
        return this;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseReference();
    }

    ~SKShader()
    {
        if (!_disposed)
        {
            _disposed = true;
            ReleaseReference();
        }
    }

    internal sealed class PictureShaderData : IDisposable
    {
        public PictureShaderData(
            GpuPicture picture,
            SKShaderTileMode tileModeX,
            SKShaderTileMode tileModeY,
            SKMatrix localMatrix,
            SKRect tileRect)
        {
            Picture = picture;
            TileModeX = tileModeX;
            TileModeY = tileModeY;
            LocalMatrix = localMatrix;
            TileRect = tileRect;
        }

        public GpuPicture Picture { get; }
        public SKShaderTileMode TileModeX { get; }
        public SKShaderTileMode TileModeY { get; }
        public SKMatrix LocalMatrix { get; }
        public SKRect TileRect { get; }

        public void Dispose()
        {
            Picture.Dispose();
        }
    }

    internal sealed class ImageShaderData : IDisposable
    {
        public ImageShaderData(
            SKImage image,
            SKShaderTileMode tileModeX,
            SKShaderTileMode tileModeY,
            SKMatrix localMatrix)
        {
            Image = image;
            TileModeX = tileModeX;
            TileModeY = tileModeY;
            LocalMatrix = localMatrix;
        }

        public SKImage Image { get; }
        public SKShaderTileMode TileModeX { get; }
        public SKShaderTileMode TileModeY { get; }
        public SKMatrix LocalMatrix { get; }
        public SKRect TileRect => new(0f, 0f, Image.Width, Image.Height);

        public void Dispose()
        {
            Image.Dispose();
        }
    }

    internal sealed class ComposedShaderData : IDisposable
    {
        public ComposedShaderData(SKShader destination, SKShader source)
        {
            Destination = destination;
            Source = source;
            Destination.AddReference();
            Source.AddReference();
        }

        public SKShader Destination { get; }
        public SKShader Source { get; }

        public void Dispose()
        {
            Destination.ReleaseReference();
            Source.ReleaseReference();
        }
    }

    private static Brush ApplyColorFilter(Brush brush, SKColorFilter? colorFilter)
    {
        if (colorFilter == null)
        {
            return brush;
        }

        switch (brush)
        {
            case SolidColorBrush solid:
                solid.Color = ApplyColorFilter(solid.Color, colorFilter);
                break;
            case LinearGradientBrush linear:
                ApplyColorFilter(linear.Stops, colorFilter);
                break;
            case RadialGradientBrush radial:
                ApplyColorFilter(radial.Stops, colorFilter);
                break;
            case TwoPointConicalGradientBrush conical:
                ApplyColorFilter(conical.Stops, colorFilter);
                break;
            case SweepGradientBrush sweep:
                ApplyColorFilter(sweep.Stops, colorFilter);
                break;
        }

        return brush;
    }

    private static void ApplyColorFilter(GradientStop[] stops, SKColorFilter colorFilter)
    {
        for (var i = 0; i < stops.Length; i++)
        {
            var stop = stops[i];
            stop.Color = ApplyColorFilter(stop.Color, colorFilter);
            stops[i] = stop;
        }
    }

    private static Vector4 ApplyColorFilter(Vector4 color, SKColorFilter colorFilter)
    {
        var filtered = colorFilter.Apply(new SKColor(
            ToByte(color.X),
            ToByte(color.Y),
            ToByte(color.Z),
            ToByte(color.W)));
        return new Vector4(
            filtered.R / 255f,
            filtered.G / 255f,
            filtered.B / 255f,
            filtered.A / 255f);
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
    }

    private static GradientStop[] CreateGradientStops(SKColor[] colors, float[]? colorPos)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colorPos != null && colorPos.Length < colors.Length)
        {
            throw new ArgumentException("Color position array must have at least as many entries as the color array.", nameof(colorPos));
        }

        var stops = new GradientStop[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = new Vector4(colors[i].R / 255.0f, colors[i].G / 255.0f, colors[i].B / 255.0f, colors[i].A / 255.0f);
            float offset = colorPos != null
                ? colorPos[i]
                : colors.Length <= 1
                    ? 0f
                    : (float)i / (colors.Length - 1);
            stops[i] = new GradientStop(c, offset);
        }

        return stops;
    }

    private static GradientSpreadMethod MapTileMode(SKShaderTileMode mode)
    {
        return mode switch
        {
            SKShaderTileMode.Clamp => GradientSpreadMethod.Pad,
            SKShaderTileMode.Repeat => GradientSpreadMethod.Repeat,
            SKShaderTileMode.Mirror => GradientSpreadMethod.Reflect,
            SKShaderTileMode.Decal => throw new NotSupportedException(
                "Decal gradient tiling cannot be represented by the ProGPU gradient brush."),
            _ => GradientSpreadMethod.Pad
        };
    }

    private static Matrix4x4 GetShaderCoordinateTransform(SKMatrix localMatrix)
    {
        return Matrix4x4.Invert(localMatrix.ToMatrix4x4(), out var inverse)
            ? inverse
            : Matrix4x4.Identity;
    }
}

public class SKColorFilter : IDisposable
{
    public SKColor Color { get; }
    public SKBlendMode Mode { get; }
    private readonly byte[]? _alphaTable;
    private readonly byte[]? _redTable;
    private readonly byte[]? _greenTable;
    private readonly byte[]? _blueTable;

    private SKColorFilter(SKColor color, SKBlendMode mode)
    {
        Color = color;
        Mode = mode;
    }

    private SKColorFilter(byte[] alpha, byte[] red, byte[] green, byte[] blue)
    {
        _alphaTable = (byte[])alpha.Clone();
        _redTable = (byte[])red.Clone();
        _greenTable = (byte[])green.Clone();
        _blueTable = (byte[])blue.Clone();
    }

    public static SKColorFilter CreateBlendMode(SKColor color, SKBlendMode mode)
    {
        return new SKColorFilter(color, mode);
    }

    public static SKColorFilter CreateTable(byte[] alpha, byte[] red, byte[] green, byte[] blue)
    {
        ArgumentNullException.ThrowIfNull(alpha);
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(green);
        ArgumentNullException.ThrowIfNull(blue);
        if (alpha.Length < 256 || red.Length < 256 || green.Length < 256 || blue.Length < 256)
        {
            throw new ArgumentException("Color filter tables must contain 256 entries.");
        }

        return new SKColorFilter(alpha, red, green, blue);
    }

    public SKColor Apply(SKColor destination)
    {
        if (_alphaTable != null && _redTable != null && _greenTable != null && _blueTable != null)
        {
            return new SKColor(
                _redTable[destination.R],
                _greenTable[destination.G],
                _blueTable[destination.B],
                _alphaTable[destination.A]);
        }

        var source = ToPremultiplied(Color);
        var dest = ToPremultiplied(destination);
        var result = Mode switch
        {
            SKBlendMode.Clear => Vector4.Zero,
            SKBlendMode.Src => source,
            SKBlendMode.Dst => dest,
            SKBlendMode.SrcOver => SourceOver(source, dest),
            SKBlendMode.DstOver => SourceOver(dest, source),
            SKBlendMode.SrcIn => source * dest.W,
            SKBlendMode.DstIn => dest * source.W,
            SKBlendMode.SrcOut => source * (1f - dest.W),
            SKBlendMode.DstOut => dest * (1f - source.W),
            SKBlendMode.SrcATop => (source * dest.W) + (dest * (1f - source.W)),
            SKBlendMode.DstATop => (dest * source.W) + (source * (1f - dest.W)),
            SKBlendMode.Xor => (source * (1f - dest.W)) + (dest * (1f - source.W)),
            SKBlendMode.Plus => Vector4.Min(source + dest, Vector4.One),
            SKBlendMode.Modulate => source * dest,
            SKBlendMode.Multiply => BlendSeparable(source, dest, static (s, d) => s * d),
            SKBlendMode.Screen => BlendSeparable(source, dest, static (s, d) => s + d - (s * d)),
            _ => throw new NotSupportedException($"SKColorFilter blend mode '{Mode}' is not supported.")
        };

        return FromPremultiplied(result);
    }

    public void Dispose() { }

    private static Vector4 ToPremultiplied(SKColor color)
    {
        var alpha = color.A / 255f;
        return new Vector4(
            color.R / 255f * alpha,
            color.G / 255f * alpha,
            color.B / 255f * alpha,
            alpha);
    }

    private static SKColor FromPremultiplied(Vector4 color)
    {
        var alpha = Clamp01(color.W);
        if (alpha <= 0f)
        {
            return SKColor.Empty;
        }

        return new SKColor(
            ToByte(color.X / alpha),
            ToByte(color.Y / alpha),
            ToByte(color.Z / alpha),
            ToByte(alpha));
    }

    private static Vector4 SourceOver(Vector4 source, Vector4 dest)
    {
        return source + (dest * (1f - source.W));
    }

    private static Vector4 BlendSeparable(Vector4 source, Vector4 dest, Func<float, float, float> blend)
    {
        var sourceAlpha = source.W;
        var destAlpha = dest.W;
        var alpha = sourceAlpha + destAlpha - (sourceAlpha * destAlpha);
        var rgb = new Vector3(
            BlendComponent(source.X, dest.X, sourceAlpha, destAlpha, blend),
            BlendComponent(source.Y, dest.Y, sourceAlpha, destAlpha, blend),
            BlendComponent(source.Z, dest.Z, sourceAlpha, destAlpha, blend));
        return new Vector4(rgb, alpha);
    }

    private static float BlendComponent(float source, float dest, float sourceAlpha, float destAlpha, Func<float, float, float> blend)
    {
        var straightSource = sourceAlpha > 0f ? source / sourceAlpha : 0f;
        var straightDest = destAlpha > 0f ? dest / destAlpha : 0f;
        return (source * (1f - destAlpha))
            + (dest * (1f - sourceAlpha))
            + (sourceAlpha * destAlpha * blend(straightSource, straightDest));
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp(MathF.Round(Clamp01(value) * 255f), 0f, 255f);
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}

public class SKImageFilter : IDisposable
{
    public bool IsBlur { get; }
    public float SigmaX { get; }
    public float SigmaY { get; }
    
    public bool IsDropShadow { get; }
    public float Dx { get; }
    public float Dy { get; }
    public SKColor ShadowColor { get; }

    private SKImageFilter(float sigmaX, float sigmaY)
    {
        IsBlur = true;
        SigmaX = sigmaX;
        SigmaY = sigmaY;
    }

    private SKImageFilter(float dx, float dy, float sigmaX, float sigmaY, SKColor color)
    {
        IsDropShadow = true;
        Dx = dx;
        Dy = dy;
        SigmaX = sigmaX;
        SigmaY = sigmaY;
        ShadowColor = color;
    }

    public static SKImageFilter CreateBlur(float sigmaX, float sigmaY, SKImageFilter? input = null)
    {
        return new SKImageFilter(sigmaX, sigmaY);
    }

    public static SKImageFilter CreateDropShadow(float dx, float dy, float sigmaX, float sigmaY, SKColor color, SKImageFilter? input = null)
    {
        return new SKImageFilter(dx, dy, sigmaX, sigmaY, color);
    }

    public void Dispose() { }
}

public class SKPathEffect : IDisposable
{
    public float[] Intervals { get; }
    public float Phase { get; }

    private SKPathEffect(float[] intervals, float phase)
    {
        Intervals = (float[])intervals.Clone();
        Phase = phase;
    }

    public static SKPathEffect CreateDash(float[] intervals, float phase)
    {
        return new SKPathEffect(intervals, phase);
    }

    public void Dispose() { }
}

public class SKMaskFilter : IDisposable
{
    public float Sigma { get; }

    private SKMaskFilter(float sigma)
    {
        Sigma = sigma;
    }

    public static SKMaskFilter CreateBlur(SKBlurStyle style, float sigma)
    {
        return new SKMaskFilter(sigma);
    }

    public void Dispose() { }
}

public enum SKBlurStyle
{
    Normal = 0,
    Solid = 1,
    Outer = 2,
    Inner = 3,
}
