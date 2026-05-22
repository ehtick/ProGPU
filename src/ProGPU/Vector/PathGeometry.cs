using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace ProGPU.Vector;

public abstract class PathSegment
{
    public abstract void Flatten(Vector2 startPoint, List<Vector2> points, ref Vector2 currentPoint, float tolerance);
}

public class LineSegment : PathSegment
{
    public Vector2 Point { get; set; }

    public LineSegment(Vector2 point)
    {
        Point = point;
    }

    public override void Flatten(Vector2 startPoint, List<Vector2> points, ref Vector2 currentPoint, float tolerance)
    {
        points.Add(Point);
        currentPoint = Point;
    }
}

public class QuadraticBezierSegment : PathSegment
{
    public Vector2 ControlPoint { get; set; }
    public Vector2 Point { get; set; }

    public QuadraticBezierSegment(Vector2 controlPoint, Vector2 point)
    {
        ControlPoint = controlPoint;
        Point = point;
    }

    public override void Flatten(Vector2 startPoint, List<Vector2> points, ref Vector2 currentPoint, float tolerance)
    {
        FlattenQuadratic(startPoint, ControlPoint, Point, points, tolerance);
        currentPoint = Point;
    }

    private static void FlattenQuadratic(Vector2 p0, Vector2 p1, Vector2 p2, List<Vector2> points, float tolerance)
    {
        // De Casteljau subdivision based on curve flatness
        float d = Vector2.Distance(p0 - 2 * p1 + p2, Vector2.Zero);
        if (d <= tolerance)
        {
            points.Add(p2);
            return;
        }

        Vector2 l0 = p0;
        Vector2 l1 = (p0 + p1) / 2.0f;
        Vector2 l2 = (p0 + 2 * p1 + p2) / 4.0f;

        Vector2 r0 = l2;
        Vector2 r1 = (p1 + p2) / 2.0f;
        Vector2 r2 = p2;

        FlattenQuadratic(l0, l1, l2, points, tolerance);
        FlattenQuadratic(r0, r1, r2, points, tolerance);
    }
}

public class CubicBezierSegment : PathSegment
{
    public Vector2 ControlPoint1 { get; set; }
    public Vector2 ControlPoint2 { get; set; }
    public Vector2 Point { get; set; }

    public CubicBezierSegment(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 point)
    {
        ControlPoint1 = controlPoint1;
        ControlPoint2 = controlPoint2;
        Point = point;
    }

    public override void Flatten(Vector2 startPoint, List<Vector2> points, ref Vector2 currentPoint, float tolerance)
    {
        FlattenCubic(startPoint, ControlPoint1, ControlPoint2, Point, points, tolerance);
        currentPoint = Point;
    }

    private static void FlattenCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, List<Vector2> points, float tolerance)
    {
        // Flatness check
        float d1 = Vector2.Distance(p0 - 2 * p1 + p2, Vector2.Zero);
        float d2 = Vector2.Distance(p1 - 2 * p2 + p3, Vector2.Zero);
        if (d1 + d2 <= tolerance)
        {
            points.Add(p3);
            return;
        }

        Vector2 l0 = p0;
        Vector2 l1 = (p0 + p1) / 2.0f;
        Vector2 l2 = (p0 + 2 * p1 + p2) / 4.0f;
        Vector2 l3 = (p0 + 3 * p1 + 3 * p2 + p3) / 8.0f;

        Vector2 r0 = l3;
        Vector2 r1 = (p1 + 2 * p2 + p3) / 4.0f;
        Vector2 r2 = (p2 + p3) / 2.0f;
        Vector2 r3 = p3;

        FlattenCubic(l0, l1, l2, l3, points, tolerance);
        FlattenCubic(r0, r1, r2, r3, points, tolerance);
    }
}

public class PathFigure
{
    public Vector2 StartPoint { get; set; }
    public List<PathSegment> Segments { get; } = new();
    public bool IsClosed { get; set; }
    public bool IsFilled { get; set; } = true;

    public PathFigure() { }

    public PathFigure(Vector2 startPoint, bool isClosed = false)
    {
        StartPoint = startPoint;
        IsClosed = isClosed;
    }

    public List<Vector2> Flatten(float tolerance = 0.5f)
    {
        var points = new List<Vector2> { StartPoint };
        var currentPoint = StartPoint;

        foreach (var segment in Segments)
        {
            segment.Flatten(currentPoint, points, ref currentPoint, tolerance);
        }

        if (IsClosed && points.Count > 1 && points[0] != points[^1])
        {
            points.Add(points[0]);
        }

        return points;
    }
}

public class PathGeometry
{
    public List<PathFigure> Figures { get; } = new();

    public List<List<Vector2>> Flatten(float tolerance = 0.5f)
    {
        var list = new List<List<Vector2>>();
        foreach (var figure in Figures)
        {
            list.Add(figure.Flatten(tolerance));
        }
        return list;
    }

    /// <summary>
    /// Parses SVG path data string (e.g. "M 10,10 L 20,20 C ... Z") into a PathGeometry object.
    /// </summary>
    public static PathGeometry Parse(string svgPathData)
    {
        var geometry = new PathGeometry();
        if (string.IsNullOrWhiteSpace(svgPathData)) return geometry;

        var tokens = Tokenize(svgPathData);
        int index = 0;

        PathFigure? currentFigure = null;
        Vector2 currentPoint = Vector2.Zero;
        Vector2 lastControlPoint = Vector2.Zero;
        char lastCommand = '\0';

        while (index < tokens.Count)
        {
            string token = tokens[index];
            char command = token[0];

            if (char.IsLetter(command))
            {
                index++;
                lastCommand = command;
            }
            else
            {
                // Implicit command (same as last)
                if (lastCommand == '\0')
                {
                    throw new FormatException($"Invalid path data: expected command at token '{token}'");
                }
                command = lastCommand;
            }

            bool isRelative = char.IsLower(command);
            char cmdUpper = char.ToUpperInvariant(command);

            switch (cmdUpper)
            {
                case 'M': // MoveTo
                    {
                        var pt = ReadVector2(tokens, ref index);
                        if (isRelative) pt += currentPoint;
                        
                        currentFigure = new PathFigure(pt);
                        geometry.Figures.Add(currentFigure);
                        currentPoint = pt;
                        lastCommand = isRelative ? 'l' : 'L'; // Subsequent points are lines
                    }
                    break;

                case 'L': // LineTo
                    {
                        var pt = ReadVector2(tokens, ref index);
                        if (isRelative) pt += currentPoint;

                        if (currentFigure == null)
                        {
                            currentFigure = new PathFigure(currentPoint);
                            geometry.Figures.Add(currentFigure);
                        }
                        currentFigure.Segments.Add(new LineSegment(pt));
                        currentPoint = pt;
                    }
                    break;

                case 'H': // Horizontal LineTo
                    {
                        float x = ReadFloat(tokens, ref index);
                        if (isRelative) x += currentPoint.X;

                        var pt = new Vector2(x, currentPoint.Y);
                        if (currentFigure == null)
                        {
                            currentFigure = new PathFigure(currentPoint);
                            geometry.Figures.Add(currentFigure);
                        }
                        currentFigure.Segments.Add(new LineSegment(pt));
                        currentPoint = pt;
                    }
                    break;

                case 'V': // Vertical LineTo
                    {
                        float y = ReadFloat(tokens, ref index);
                        if (isRelative) y += currentPoint.Y;

                        var pt = new Vector2(currentPoint.X, y);
                        if (currentFigure == null)
                        {
                            currentFigure = new PathFigure(currentPoint);
                            geometry.Figures.Add(currentFigure);
                        }
                        currentFigure.Segments.Add(new LineSegment(pt));
                        currentPoint = pt;
                    }
                    break;

                case 'Q': // Quadratic Bezier
                    {
                        var ctrl = ReadVector2(tokens, ref index);
                        var to = ReadVector2(tokens, ref index);
                        if (isRelative)
                        {
                            ctrl += currentPoint;
                            to += currentPoint;
                        }

                        if (currentFigure == null)
                        {
                            currentFigure = new PathFigure(currentPoint);
                            geometry.Figures.Add(currentFigure);
                        }
                        currentFigure.Segments.Add(new QuadraticBezierSegment(ctrl, to));
                        lastControlPoint = ctrl;
                        currentPoint = to;
                    }
                    break;

                case 'C': // Cubic Bezier
                    {
                        var ctrl1 = ReadVector2(tokens, ref index);
                        var ctrl2 = ReadVector2(tokens, ref index);
                        var to = ReadVector2(tokens, ref index);
                        if (isRelative)
                        {
                            ctrl1 += currentPoint;
                            ctrl2 += currentPoint;
                            to += currentPoint;
                        }

                        if (currentFigure == null)
                        {
                            currentFigure = new PathFigure(currentPoint);
                            geometry.Figures.Add(currentFigure);
                        }
                        currentFigure.Segments.Add(new CubicBezierSegment(ctrl1, ctrl2, to));
                        lastControlPoint = ctrl2;
                        currentPoint = to;
                    }
                    break;

                case 'Z': // ClosePath
                    {
                        if (currentFigure != null)
                        {
                            currentFigure.IsClosed = true;
                            currentPoint = currentFigure.StartPoint;
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException($"Command '{command}' is not supported in path data parsing.");
            }
        }

        return geometry;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            // Parse numbers (including negative numbers and floats)
            int start = i;
            if (text[i] == '-' || text[i] == '+') i++;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || (i > 0 && (text[i-1] == 'e' || text[i-1] == 'E') && (text[i] == '-' || text[i] == '+'))))
            {
                i++;
            }
            if (i > start)
            {
                tokens.Add(text.Substring(start, i - start));
            }
            else
            {
                i++; // Fallback to avoid infinite loops on invalid chars
            }
        }
        return tokens;
    }

    private static float ReadFloat(List<string> tokens, ref int index)
    {
        if (index >= tokens.Count) throw new FormatException("Missing number in path data");
        float value = float.Parse(tokens[index++], CultureInfo.InvariantCulture);
        return value;
    }

    private static Vector2 ReadVector2(List<string> tokens, ref int index)
    {
        float x = ReadFloat(tokens, ref index);
        float y = ReadFloat(tokens, ref index);
        return new Vector2(x, y);
    }
}
