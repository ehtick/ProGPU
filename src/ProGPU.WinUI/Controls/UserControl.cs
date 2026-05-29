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

[ContentProperty(Name = "Content")]
public class UserControl : ContentControl
{
    public new FrameworkElement? Content
    {
        get => base.Content as FrameworkElement;
        set => base.Content = value;
    }

    public UserControl()
    {
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
