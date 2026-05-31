using Microsoft.UI.Xaml;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class MatrixTransform : Transform
{
    public static readonly DependencyProperty MatrixProperty =
        DependencyProperty.Register(
            "Matrix",
            typeof(Matrix4x4),
            typeof(MatrixTransform),
            new PropertyMetadata(Matrix4x4.Identity) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Matrix4x4 Matrix
    {
        get => (Matrix4x4)(GetValue(MatrixProperty) ?? Matrix4x4.Identity);
        set => SetValue(MatrixProperty, value);
    }

    public override Matrix4x4 Value => Matrix;
}
