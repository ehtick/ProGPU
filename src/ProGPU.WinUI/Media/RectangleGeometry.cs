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

        Vector2 topLeft = TransformPoint(new Vector2(rect.X, rect.Y));
        Vector2 bottomRight = TransformPoint(new Vector2(rect.Right, rect.Bottom));

        float x = Math.Min(topLeft.X, bottomRight.X);
        float y = Math.Min(topLeft.Y, bottomRight.Y);
        float w = Math.Abs(bottomRight.X - topLeft.X);
        float h = Math.Abs(bottomRight.Y - topLeft.Y);

        return new Rect(x, y, w, h);
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
