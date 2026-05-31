using Microsoft.UI.Xaml;
using ProGPU.Vector;
using ProGPU.Scene;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public abstract class Geometry : DependencyObject
{
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(
            "Transform",
            typeof(Transform),
            typeof(Geometry),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Transform? Transform
    {
        get => GetValue(TransformProperty) as Transform;
        set => SetValue(TransformProperty, value);
    }

    internal Matrix4x4? ParentTransformMatrix { get; set; }

    protected Matrix4x4 EffectiveTransform
    {
        get
        {
            Matrix4x4 local = Transform != null ? Transform.Value : Matrix4x4.Identity;
            if (ParentTransformMatrix.HasValue)
            {
                return local * ParentTransformMatrix.Value;
            }
            return local;
        }
    }

    protected bool HasTransform => Transform != null || ParentTransformMatrix.HasValue;

    public abstract void Draw(DrawingContext context, Brush? fill, Pen? pen);

    public abstract Rect Bounds { get; }

    protected Vector2 TransformPoint(Vector2 pt)
    {
        if (HasTransform)
        {
            return Vector2.Transform(pt, EffectiveTransform);
        }
        return pt;
    }
}
