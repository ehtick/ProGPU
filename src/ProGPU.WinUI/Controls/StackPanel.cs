using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public class StackPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            "Orientation",
            typeof(Orientation),
            typeof(StackPanel),
            new PropertyMetadata(Orientation.Vertical, (d, e) => {
                var sp = (StackPanel)d;
                sp.Invalidate();
                sp.InvalidateMeasure();
            }));

    public Orientation Orientation
    {
        get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Vertical);
        set => SetValue(OrientationProperty, value);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float totalWidth = 0f;
        float totalHeight = 0f;

        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                if (Orientation == Orientation.Vertical)
                {
                    node.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
                    var desired = node.DesiredSize;
                    totalWidth = Math.Max(totalWidth, desired.X);
                    totalHeight += desired.Y;
                }
                else
                {
                    node.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));
                    var desired = node.DesiredSize;
                    totalWidth += desired.X;
                    totalHeight = Math.Max(totalHeight, desired.Y);
                }
            }
        }

        return new Vector2(totalWidth, totalHeight);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        float offset = 0f;

        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                var desired = node.DesiredSize;
                
                if (Orientation == Orientation.Vertical)
                {
                    float childHeight = desired.Y;
                    node.Arrange(new Rect(arrangeRect.X, arrangeRect.Y + offset, arrangeRect.Width, childHeight));
                    offset += childHeight;
                }
                else
                {
                    float childWidth = desired.X;
                    node.Arrange(new Rect(arrangeRect.X + offset, arrangeRect.Y, childWidth, arrangeRect.Height));
                    offset += childWidth;
                }
            }
        }
    }
}
