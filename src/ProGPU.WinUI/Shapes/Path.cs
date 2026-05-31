using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Xaml.Shapes;

public class Path : Shape
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            "Data",
            typeof(Geometry),
            typeof(Path),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Geometry? Data
    {
        get => GetValue(DataProperty) as Geometry;
        set => SetValue(DataProperty, value);
    }

    public override Geometry? DefiningGeometry => Data;
}
