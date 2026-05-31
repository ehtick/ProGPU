using Microsoft.UI.Xaml;
using ProGPU.Vector;
using ProGPU.Scene;
using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class EllipseGeometry : Geometry
{
    public static readonly DependencyProperty CenterProperty =
        DependencyProperty.Register(
            "Center",
            typeof(Vector2),
            typeof(EllipseGeometry),
            new PropertyMetadata(Vector2.Zero) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Vector2 Center
    {
        get => (Vector2)(GetValue(CenterProperty) ?? Vector2.Zero);
        set => SetValue(CenterProperty, value);
    }

    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(
            "RadiusX",
            typeof(float),
            typeof(EllipseGeometry),
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
            typeof(EllipseGeometry),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float RadiusY
    {
        get => (float)(GetValue(RadiusYProperty) ?? 0f);
        set => SetValue(RadiusYProperty, value);
    }

    public override void Draw(DrawingContext context, Brush? fill, Pen? pen)
    {
        Vector2 center = TransformPoint(Center);
        float rx = RadiusX;
        float ry = RadiusY;

        if (HasTransform)
        {
            var val = EffectiveTransform;
            rx *= new Vector2(val.M11, val.M12).Length();
            ry *= new Vector2(val.M21, val.M22).Length();
        }

        context.DrawEllipse(fill, pen, center, rx, ry);
    }

    public override Rect Bounds
    {
        get
        {
            Vector2 center = TransformPoint(Center);
            float rx = RadiusX;
            float ry = RadiusY;

            if (HasTransform)
            {
                var val = EffectiveTransform;
                rx *= new Vector2(val.M11, val.M12).Length();
                ry *= new Vector2(val.M21, val.M22).Length();
            }

            return new Rect(center.X - rx, center.Y - ry, rx * 2f, ry * 2f);
        }
    }
}
