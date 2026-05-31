using Microsoft.UI.Xaml;
using ProGPU.Vector;
using ProGPU.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public class GeometryGroup : Geometry
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            "Children",
            typeof(List<Geometry>),
            typeof(GeometryGroup),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public List<Geometry> Children
    {
        get
        {
            var list = (List<Geometry>)GetValue(ChildrenProperty);
            if (list == null)
            {
                list = new List<Geometry>();
                SetValue(ChildrenProperty, list);
            }
            return list;
        }
    }

    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(
            "FillRule",
            typeof(FillRule),
            typeof(GeometryGroup),
            new PropertyMetadata(FillRule.EvenOdd) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public FillRule FillRule
    {
        get => (FillRule)(GetValue(FillRuleProperty) ?? FillRule.EvenOdd);
        set => SetValue(FillRuleProperty, value);
    }

    public override void Draw(DrawingContext context, Brush? fill, Pen? pen)
    {
        var groupTransform = EffectiveTransform;
        foreach (var child in Children)
        {
            if (child != null)
            {
                child.ParentTransformMatrix = groupTransform;
                child.Draw(context, fill, pen);
                child.ParentTransformMatrix = null;
            }
        }
    }

    public override Rect Bounds
    {
        get
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            var groupTransform = EffectiveTransform;
            foreach (var child in Children)
            {
                if (child != null)
                {
                    child.ParentTransformMatrix = groupTransform;
                    Rect b = child.Bounds;
                    child.ParentTransformMatrix = null;

                    if (!b.IsEmpty)
                    {
                        minX = Math.Min(minX, b.X);
                        minY = Math.Min(minY, b.Y);
                        maxX = Math.Max(maxX, b.Right);
                        maxY = Math.Max(maxY, b.Bottom);
                    }
                }
            }

            if (minX == float.MaxValue) return Rect.Empty;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
