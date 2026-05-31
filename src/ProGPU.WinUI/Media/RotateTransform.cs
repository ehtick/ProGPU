using Microsoft.UI.Xaml;
using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class RotateTransform : Transform
{
    public static readonly DependencyProperty AngleProperty =
        DependencyProperty.Register(
            "Angle",
            typeof(float),
            typeof(RotateTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float Angle
    {
        get => (float)(GetValue(AngleProperty) ?? 0f);
        set => SetValue(AngleProperty, value);
    }

    public static readonly DependencyProperty CenterXProperty =
        DependencyProperty.Register(
            "CenterX",
            typeof(float),
            typeof(RotateTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float CenterX
    {
        get => (float)(GetValue(CenterXProperty) ?? 0f);
        set => SetValue(CenterXProperty, value);
    }

    public static readonly DependencyProperty CenterYProperty =
        DependencyProperty.Register(
            "CenterY",
            typeof(float),
            typeof(RotateTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float CenterY
    {
        get => (float)(GetValue(CenterYProperty) ?? 0f);
        set => SetValue(CenterYProperty, value);
    }

    public override Matrix4x4 Value
    {
        get
        {
            float radians = (float)(Angle * Math.PI / 180.0);
            if (CenterX == 0f && CenterY == 0f)
            {
                return Matrix4x4.CreateRotationZ(radians);
            }

            return Matrix4x4.CreateTranslation(-CenterX, -CenterY, 0f) *
                   Matrix4x4.CreateRotationZ(radians) *
                   Matrix4x4.CreateTranslation(CenterX, CenterY, 0f);
        }
    }
}
