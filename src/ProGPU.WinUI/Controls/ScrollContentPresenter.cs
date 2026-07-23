using System;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>Content presenter that exposes the WinUI scrolling surface contract.</summary>
public sealed class ScrollContentPresenter : ContentPresenter
{
    private double _horizontalOffset;
    private double _verticalOffset;

    public static readonly DependencyProperty CanContentRenderOutsideBoundsProperty = DependencyProperty.Register(
        nameof(CanContentRenderOutsideBounds), typeof(bool), typeof(ScrollContentPresenter),
        new PropertyMetadata(false) { AffectsRender = true });

    public static readonly DependencyProperty SizesContentToTemplatedParentProperty = DependencyProperty.Register(
        nameof(SizesContentToTemplatedParent), typeof(bool), typeof(ScrollContentPresenter),
        new PropertyMetadata(false) { AffectsMeasure = true, AffectsArrange = true });

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => ContentVisual?.DesiredSize.X ?? 0d;
    public double ExtentHeight => ContentVisual?.DesiredSize.Y ?? 0d;
    public double ViewportWidth => Size.X;
    public double ViewportHeight => Size.Y;
    public double HorizontalOffset => ScrollOwner is ScrollViewer owner ? owner.HorizontalOffset : _horizontalOffset;
    public double VerticalOffset => ScrollOwner is ScrollViewer owner ? owner.VerticalOffset : _verticalOffset;
    public object? ScrollOwner { get; set; }

    public bool CanContentRenderOutsideBounds
    {
        get => (bool)(GetValue(CanContentRenderOutsideBoundsProperty) ?? false);
        set => SetValue(CanContentRenderOutsideBoundsProperty, value);
    }

    public bool SizesContentToTemplatedParent
    {
        get => (bool)(GetValue(SizesContentToTemplatedParentProperty) ?? false);
        set => SetValue(SizesContentToTemplatedParentProperty, value);
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - 16d);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 16d);
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 16d);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + 16d);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelUp() => LineUp();
    public void MouseWheelDown() => LineDown();
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();

    public void SetHorizontalOffset(double offset)
    {
        var clamped = Math.Clamp(offset, 0d, Math.Max(0d, ExtentWidth - ViewportWidth));
        if (ScrollOwner is ScrollViewer owner) owner.HorizontalOffset = (float)clamped;
        else _horizontalOffset = clamped;
        InvalidateArrange();
        Invalidate();
    }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Clamp(offset, 0d, Math.Max(0d, ExtentHeight - ViewportHeight));
        if (ScrollOwner is ScrollViewer owner) owner.VerticalOffset = (float)clamped;
        else _verticalOffset = clamped;
        InvalidateArrange();
        Invalidate();
    }

    public Windows.Foundation.Rect MakeVisible(UIElement visual, Windows.Foundation.Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(visual);
        SetHorizontalOffset(rectangle.X);
        SetVerticalOffset(rectangle.Y);
        return rectangle;
    }
}
