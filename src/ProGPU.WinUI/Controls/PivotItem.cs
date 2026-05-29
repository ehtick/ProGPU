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

public class PivotItem : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            "Header",
            typeof(object),
            typeof(PivotItem),
            new PropertyMetadata(null, (d, e) => {
                var pi = (PivotItem)d;
                pi.Invalidate();
                pi.InvalidateMeasure();
            }));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public PivotItem()
    {
        IsTabStop = false;
    }

    public PivotItem(object header, object? content = null) : this()
    {
        Header = header;
        Content = content;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            contentVisual.Measure(availableSize);
            return contentVisual.DesiredSize;
        }
        return Vector2.Zero;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            contentVisual.Arrange(arrangeRect);
        }
    }
}
