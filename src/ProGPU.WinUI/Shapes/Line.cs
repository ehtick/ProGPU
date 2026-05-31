using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace Microsoft.UI.Xaml.Shapes;

public class Line : Shape
{
    public static readonly DependencyProperty X1Property =
        DependencyProperty.Register(
            "X1",
            typeof(float),
            typeof(Line),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float X1
    {
        get => (float)(GetValue(X1Property) ?? 0f);
        set => SetValue(X1Property, value);
    }

    public static readonly DependencyProperty Y1Property =
        DependencyProperty.Register(
            "Y1",
            typeof(float),
            typeof(Line),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float Y1
    {
        get => (float)(GetValue(Y1Property) ?? 0f);
        set => SetValue(Y1Property, value);
    }

    public static readonly DependencyProperty X2Property =
        DependencyProperty.Register(
            "X2",
            typeof(float),
            typeof(Line),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float X2
    {
        get => (float)(GetValue(X2Property) ?? 0f);
        set => SetValue(X2Property, value);
    }

    public static readonly DependencyProperty Y2Property =
        DependencyProperty.Register(
            "Y2",
            typeof(float),
            typeof(Line),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float Y2
    {
        get => (float)(GetValue(Y2Property) ?? 0f);
        set => SetValue(Y2Property, value);
    }

    private readonly LineGeometry _geometry = new();

    public override Geometry DefiningGeometry
    {
        get
        {
            _geometry.StartPoint = new Vector2(X1, Y1);
            _geometry.EndPoint = new Vector2(X2, Y2);
            return _geometry;
        }
    }
}
