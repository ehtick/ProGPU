using Microsoft.UI.Xaml;
using ProGPU.Vector;
using ProGPU.Scene;
using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class LineGeometry : Geometry
{
    public static readonly DependencyProperty StartPointProperty =
        DependencyProperty.Register(
            "StartPoint",
            typeof(Vector2),
            typeof(LineGeometry),
            new PropertyMetadata(Vector2.Zero) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Vector2 StartPoint
    {
        get => (Vector2)(GetValue(StartPointProperty) ?? Vector2.Zero);
        set => SetValue(StartPointProperty, value);
    }

    public static readonly DependencyProperty EndPointProperty =
        DependencyProperty.Register(
            "EndPoint",
            typeof(Vector2),
            typeof(LineGeometry),
            new PropertyMetadata(Vector2.Zero) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Vector2 EndPoint
    {
        get => (Vector2)(GetValue(EndPointProperty) ?? Vector2.Zero);
        set => SetValue(EndPointProperty, value);
    }

    public override void Draw(DrawingContext context, Brush? fill, Pen? pen)
    {
        if (pen == null) return; // Lines only render with pens/strokes

        Vector2 p1 = TransformPoint(StartPoint);
        Vector2 p2 = TransformPoint(EndPoint);

        context.DrawLine(pen, p1, p2);
    }

    public override Rect Bounds
    {
        get
        {
            Vector2 p1 = TransformPoint(StartPoint);
            Vector2 p2 = TransformPoint(EndPoint);

            float x = Math.Min(p1.X, p2.X);
            float y = Math.Min(p1.Y, p2.Y);
            float w = Math.Abs(p2.X - p1.X);
            float h = Math.Abs(p2.Y - p1.Y);

            return new Rect(x, y, w, h);
        }
    }
}
