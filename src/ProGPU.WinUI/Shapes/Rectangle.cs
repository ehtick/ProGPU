using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Shapes;

public class Rectangle : Shape
{
    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(
            "RadiusX",
            typeof(float),
            typeof(Rectangle),
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
            typeof(Rectangle),
            new PropertyMetadata(0f) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public float RadiusY
    {
        get => (float)(GetValue(RadiusYProperty) ?? 0f);
        set => SetValue(RadiusYProperty, value);
    }

    private readonly RectangleGeometry _geometry = new();

    public override Geometry DefiningGeometry
    {
        get
        {
            float w = float.IsNaN(Width) ? (Size.X > 0f ? Size.X : 0f) : Width;
            float h = float.IsNaN(Height) ? (Size.Y > 0f ? Size.Y : 0f) : Height;

            _geometry.Rect = new Rect(0f, 0f, w, h);
            _geometry.RadiusX = RadiusX;
            _geometry.RadiusY = RadiusY;
            return _geometry;
        }
    }
}
