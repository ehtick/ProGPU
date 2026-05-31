using Microsoft.UI.Xaml;
using System.Collections.Generic;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class TransformGroup : Transform
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            "Children",
            typeof(List<Transform>),
            typeof(TransformGroup),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public List<Transform> Children
    {
        get
        {
            var list = (List<Transform>)GetValue(ChildrenProperty);
            if (list == null)
            {
                list = new List<Transform>();
                SetValue(ChildrenProperty, list);
            }
            return list;
        }
    }

    public override Matrix4x4 Value
    {
        get
        {
            var result = Matrix4x4.Identity;
            foreach (var child in Children)
            {
                if (child != null)
                {
                    result *= child.Value;
                }
            }
            return result;
        }
    }
}
