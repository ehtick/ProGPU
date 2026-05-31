using Microsoft.UI.Xaml;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class TranslateTransform : Transform
{
    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(
            "X",
            typeof(float),
            typeof(TranslateTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float X
    {
        get => (float)(GetValue(XProperty) ?? 0f);
        set => SetValue(XProperty, value);
    }

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(
            "Y",
            typeof(float),
            typeof(TranslateTransform),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float Y
    {
        get => (float)(GetValue(YProperty) ?? 0f);
        set => SetValue(YProperty, value);
    }

    public override Matrix4x4 Value => Matrix4x4.CreateTranslation(X, Y, 0f);
}
