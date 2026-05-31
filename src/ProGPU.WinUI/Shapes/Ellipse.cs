using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace Microsoft.UI.Xaml.Shapes;

public class Ellipse : Shape
{
    private readonly EllipseGeometry _geometry = new();

    public override Geometry DefiningGeometry
    {
        get
        {
            float w = float.IsNaN(Width) ? (Size.X > 0f ? Size.X : 0f) : Width;
            float h = float.IsNaN(Height) ? (Size.Y > 0f ? Size.Y : 0f) : Height;

            _geometry.Center = new Vector2(w / 2f, h / 2f);
            _geometry.RadiusX = w / 2f;
            _geometry.RadiusY = h / 2f;
            return _geometry;
        }
    }
}
