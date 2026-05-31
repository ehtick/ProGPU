using Microsoft.UI.Xaml;
using System;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class SkewTransform : Transform
{
    public static readonly DependencyProperty AngleXProperty =
        DependencyProperty.Register(
            "AngleX",
            typeof(float),
            typeof(SkewTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float AngleX
    {
        get => (float)(GetValue(AngleXProperty) ?? 0f);
        set => SetValue(AngleXProperty, value);
    }

    public static readonly DependencyProperty AngleYProperty =
        DependencyProperty.Register(
            "AngleY",
            typeof(float),
            typeof(SkewTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float AngleY
    {
        get => (float)(GetValue(AngleYProperty) ?? 0f);
        set => SetValue(AngleYProperty, value);
    }

    public static readonly DependencyProperty CenterXProperty =
        DependencyProperty.Register(
            "CenterX",
            typeof(float),
            typeof(SkewTransform),
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
            typeof(SkewTransform),
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
            float radX = (float)(AngleX * Math.PI / 180.0);
            float radY = (float)(AngleY * Math.PI / 180.0);
            
            var skew = Matrix4x4.Identity;
            skew.M21 = (float)Math.Tan(radX);
            skew.M12 = (float)Math.Tan(radY);

            if (CenterX == 0f && CenterY == 0f)
            {
                return skew;
            }

            return Matrix4x4.CreateTranslation(-CenterX, -CenterY, 0f) *
                   skew *
                   Matrix4x4.CreateTranslation(CenterX, CenterY, 0f);
        }
    }
}
