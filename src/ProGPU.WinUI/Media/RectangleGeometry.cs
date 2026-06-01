using Microsoft.UI.Xaml;
using ProGPU.Vector;
using ProGPU.Scene;
using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class RectangleGeometry : Geometry
{
    public static readonly DependencyProperty RectProperty =
        DependencyProperty.Register(
            "Rect",
            typeof(Rect),
            typeof(RectangleGeometry),
            new PropertyMetadata(Rect.Empty) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Rect Rect
    {
        get => (Rect)(GetValue(RectProperty) ?? Rect.Empty);
        set => SetValue(RectProperty, value);
    }

    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(
            "RadiusX",
            typeof(float),
            typeof(RectangleGeometry),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float RadiusX
    {
        get => (float)(GetValue(RadiusXProperty) ?? 0f);
        set => SetValue(RadiusXProperty, value);
    }

    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register(
            "RadiusY",
            typeof(float),
            typeof(RectangleGeometry),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float RadiusY
    {
        get => (float)(GetValue(RadiusYProperty) ?? 0f);
        set => SetValue(RadiusYProperty, value);
    }

    private Rect GetTransformedRect()
    {
        Rect rect = Rect;
        if (rect.IsEmpty) return Rect.Empty;

        Vector2 p1 = TransformPoint(new Vector2(rect.X, rect.Y));
        Vector2 p2 = TransformPoint(new Vector2(rect.Right, rect.Y));
        Vector2 p3 = TransformPoint(new Vector2(rect.X, rect.Bottom));
        Vector2 p4 = TransformPoint(new Vector2(rect.Right, rect.Bottom));

        float minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        float maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        float minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        float maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public override void Draw(DrawingContext context, Brush? fill, Pen? pen)
    {
        Rect r = GetTransformedRect();
        if (r.IsEmpty) return;

        float rx = RadiusX;
        float ry = RadiusY;

        if (HasTransform)
        {
            var val = EffectiveTransform;
            rx *= new Vector2(val.M11, val.M12).Length();
            ry *= new Vector2(val.M21, val.M22).Length();
        }

        float radius = Math.Max(rx, ry);
        if (radius > 0f)
        {
            context.DrawRoundedRectangle(fill, pen, r, radius);
        }
        else
        {
            context.DrawRectangle(fill, pen, r);
        }
    }

    public override Rect Bounds => GetTransformedRect();
}
