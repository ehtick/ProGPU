using Microsoft.UI.Xaml;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class ScaleTransform : Transform
{
    public static readonly DependencyProperty ScaleXProperty =
        DependencyProperty.Register(
            "ScaleX",
            typeof(float),
            typeof(ScaleTransform),
            new PropertyMetadata(1f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float ScaleX
    {
        get => (float)(GetValue(ScaleXProperty) ?? 1f);
        set => SetValue(ScaleXProperty, value);
    }

    public static readonly DependencyProperty ScaleYProperty =
        DependencyProperty.Register(
            "ScaleY",
            typeof(float),
            typeof(ScaleTransform),
            new PropertyMetadata(1f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float ScaleY
    {
        get => (float)(GetValue(ScaleYProperty) ?? 1f);
        set => SetValue(ScaleYProperty, value);
    }

    public static readonly DependencyProperty CenterXProperty =
        DependencyProperty.Register(
            "CenterX",
            typeof(float),
            typeof(ScaleTransform),
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
            typeof(ScaleTransform),
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
            if (CenterX == 0f && CenterY == 0f)
            {
                return Matrix4x4.CreateScale(ScaleX, ScaleY, 1f);
            }

            return Matrix4x4.CreateTranslation(-CenterX, -CenterY, 0f) *
                   Matrix4x4.CreateScale(ScaleX, ScaleY, 1f) *
                   Matrix4x4.CreateTranslation(CenterX, CenterY, 0f);
        }
    }
}
