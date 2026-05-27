using Thickness = Microsoft.UI.Xaml.Thickness;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Scene;
using ProGPU.Layout;

namespace ProGPU.Designer;

public class SelectionAdorner : Panel
{
    private readonly Thumb _topLeftThumb = new();
    private readonly Thumb _topCenterThumb = new();
    private readonly Thumb _topRightThumb = new();
    private readonly Thumb _middleLeftThumb = new();
    private readonly Thumb _middleRightThumb = new();
    private readonly Thumb _bottomLeftThumb = new();
    private readonly Thumb _bottomCenterThumb = new();
    private readonly Thumb _bottomRightThumb = new();

    public FrameworkElement? AssociatedElement { get; }
    public DesignerCanvas? ParentCanvas { get; }

    public SelectionAdorner(FrameworkElement associatedElement, DesignerCanvas parentCanvas)
    {
        AssociatedElement = associatedElement;
        ParentCanvas = parentCanvas;

        IsHitTestVisible = true;

        Children.Add(_topLeftThumb);
        Children.Add(_topCenterThumb);
        Children.Add(_topRightThumb);
        Children.Add(_middleLeftThumb);
        Children.Add(_middleRightThumb);
        Children.Add(_bottomLeftThumb);
        Children.Add(_bottomCenterThumb);
        Children.Add(_bottomRightThumb);

        ApplyThumbStyle(_topLeftThumb);
        ApplyThumbStyle(_topCenterThumb);
        ApplyThumbStyle(_topRightThumb);
        ApplyThumbStyle(_middleLeftThumb);
        ApplyThumbStyle(_middleRightThumb);
        ApplyThumbStyle(_bottomLeftThumb);
        ApplyThumbStyle(_bottomCenterThumb);
        ApplyThumbStyle(_bottomRightThumb);

        _topLeftThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _topCenterThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _topRightThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _middleLeftThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _middleRightThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _bottomLeftThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _bottomCenterThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);
        _bottomRightThumb.DragDelta += (s, e) => HandleDragDelta((Thumb)s, e.HorizontalChange, e.VerticalChange);

        void ClearSnapGuidelines(object sender, DragCompletedEventArgs e)
        {
            if (ParentCanvas != null)
            {
                ParentCanvas.ActiveVerticalSnapX = null;
                ParentCanvas.ActiveHorizontalSnapY = null;
                ParentCanvas.Invalidate();
            }
        }
        _topLeftThumb.DragCompleted += ClearSnapGuidelines;
        _topCenterThumb.DragCompleted += ClearSnapGuidelines;
        _topRightThumb.DragCompleted += ClearSnapGuidelines;
        _middleLeftThumb.DragCompleted += ClearSnapGuidelines;
        _middleRightThumb.DragCompleted += ClearSnapGuidelines;
        _bottomLeftThumb.DragCompleted += ClearSnapGuidelines;
        _bottomCenterThumb.DragCompleted += ClearSnapGuidelines;
        _bottomRightThumb.DragCompleted += ClearSnapGuidelines;
    }

    private void ApplyThumbStyle(Thumb thumb)
    {
        thumb.Background = new ThemeResourceBrush("SystemAccentColor");
        thumb.BorderBrush = new ThemeResourceBrush("PageBackground");
        thumb.BorderThickness = new Thickness(1f);
        thumb.CornerRadius = 4f;
    }

    public void UpdatePositionAndSize()
    {
        if (AssociatedElement == null) return;
        
        float left = Canvas.GetLeft(AssociatedElement);
        float top = Canvas.GetTop(AssociatedElement);
        float width = float.IsNaN(AssociatedElement.Width) ? AssociatedElement.Size.X : AssociatedElement.Width;
        float height = float.IsNaN(AssociatedElement.Height) ? AssociatedElement.Size.Y : AssociatedElement.Height;
        
        Canvas.SetLeft(this, left);
        Canvas.SetTop(this, top);
        this.Width = width;
        this.Height = height;
        
        InvalidateMeasure();
        InvalidateArrange();
        Invalidate();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float handleSize = 8f;
        Vector2 handleAvailable = new Vector2(handleSize, handleSize);
        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                node.Measure(handleAvailable);
            }
        }
        return availableSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        float handleSize = 8f;
        float halfSize = handleSize / 2f;
        float w = arrangeRect.Width;
        float h = arrangeRect.Height;

        _topLeftThumb.Arrange(new Rect(-halfSize, -halfSize, handleSize, handleSize));
        _topCenterThumb.Arrange(new Rect(w / 2f - halfSize, -halfSize, handleSize, handleSize));
        _topRightThumb.Arrange(new Rect(w - halfSize, -halfSize, handleSize, handleSize));

        _middleLeftThumb.Arrange(new Rect(-halfSize, h / 2f - halfSize, handleSize, handleSize));
        _middleRightThumb.Arrange(new Rect(w - halfSize, h / 2f - halfSize, handleSize, handleSize));

        _bottomLeftThumb.Arrange(new Rect(-halfSize, h - halfSize, handleSize, handleSize));
        _bottomCenterThumb.Arrange(new Rect(w / 2f - halfSize, h - halfSize, handleSize, handleSize));
        _bottomRightThumb.Arrange(new Rect(w - halfSize, h - halfSize, handleSize, handleSize));
    }

    private void HandleDragDelta(Thumb thumb, float dx, float dy)
    {
        if (AssociatedElement == null || ParentCanvas == null) return;

        var element = AssociatedElement;
        float minWidth = 20f;
        float minHeight = 20f;

        float currentLeft = Canvas.GetLeft(element);
        float currentTop = Canvas.GetTop(element);
        float currentWidth = float.IsNaN(element.Width) ? element.Size.X : element.Width;
        float currentHeight = float.IsNaN(element.Height) ? element.Size.Y : element.Height;

        float newLeft = currentLeft;
        float newTop = currentTop;
        float newWidth = currentWidth;
        float newHeight = currentHeight;

        if (thumb == _topLeftThumb)
        {
            float targetLeft = currentLeft + dx;
            float targetTop = currentTop + dy;
            float targetWidth = currentWidth - dx;
            float targetHeight = currentHeight - dy;

            Vector2 snappedLeftTop = ParentCanvas.SnapPosition(element, new Vector2(targetLeft, targetTop));
            
            float snapDx = snappedLeftTop.X - currentLeft;
            float snapDy = snappedLeftTop.Y - currentTop;
            
            newWidth = MathF.Max(minWidth, currentWidth - snapDx);
            newLeft = snappedLeftTop.X;
            
            newHeight = MathF.Max(minHeight, currentHeight - snapDy);
            newTop = snappedLeftTop.Y;
        }
        else if (thumb == _topCenterThumb)
        {
            float targetTop = currentTop + dy;
            float targetHeight = currentHeight - dy;

            Vector2 snapped = ParentCanvas.SnapPosition(element, new Vector2(currentLeft, targetTop));
            float snapDy = snapped.Y - currentTop;

            newHeight = MathF.Max(minHeight, currentHeight - snapDy);
            newTop = snapped.Y;
        }
        else if (thumb == _topRightThumb)
        {
            float targetTop = currentTop + dy;
            float targetWidth = currentWidth + dx;

            Vector2 snapped = ParentCanvas.SnapPosition(element, new Vector2(currentLeft, targetTop));
            float snapDy = snapped.Y - currentTop;
            
            newHeight = MathF.Max(minHeight, currentHeight - snapDy);
            newTop = snapped.Y;
            
            float candidateRight = currentLeft + targetWidth;
            float? snapX = ParentCanvas.GetSnapX(element, candidateRight);
            if (snapX != null)
            {
                newWidth = MathF.Max(minWidth, snapX.Value - currentLeft);
            }
            else
            {
                newWidth = MathF.Max(minWidth, targetWidth);
            }
        }
        else if (thumb == _middleLeftThumb)
        {
            float targetLeft = currentLeft + dx;
            Vector2 snapped = ParentCanvas.SnapPosition(element, new Vector2(targetLeft, currentTop));
            float snapDx = snapped.X - currentLeft;

            newWidth = MathF.Max(minWidth, currentWidth - snapDx);
            newLeft = snapped.X;
        }
        else if (thumb == _middleRightThumb)
        {
            float targetWidth = currentWidth + dx;
            float candidateRight = currentLeft + targetWidth;
            float? snapX = ParentCanvas.GetSnapX(element, candidateRight);
            if (snapX != null)
            {
                newWidth = MathF.Max(minWidth, snapX.Value - currentLeft);
            }
            else
            {
                newWidth = MathF.Max(minWidth, targetWidth);
            }
        }
        else if (thumb == _bottomLeftThumb)
        {
            float targetLeft = currentLeft + dx;
            float targetHeight = currentHeight + dy;

            Vector2 snapped = ParentCanvas.SnapPosition(element, new Vector2(targetLeft, currentTop));
            float snapDx = snapped.X - currentLeft;
            newWidth = MathF.Max(minWidth, currentWidth - snapDx);
            newLeft = snapped.X;

            float candidateBottom = currentTop + targetHeight;
            float? snapYVal = ParentCanvas.GetSnapY(element, candidateBottom);
            if (snapYVal != null)
            {
                newHeight = MathF.Max(minHeight, snapYVal.Value - currentTop);
            }
            else
            {
                newHeight = MathF.Max(minHeight, targetHeight);
            }
        }
        else if (thumb == _bottomCenterThumb)
        {
            float targetHeight = currentHeight + dy;
            float candidateBottom = currentTop + targetHeight;
            float? snapYVal = ParentCanvas.GetSnapY(element, candidateBottom);
            if (snapYVal != null)
            {
                newHeight = MathF.Max(minHeight, snapYVal.Value - currentTop);
            }
            else
            {
                newHeight = MathF.Max(minHeight, targetHeight);
            }
        }
        else if (thumb == _bottomRightThumb)
        {
            float targetWidth = currentWidth + dx;
            float targetHeight = currentHeight + dy;

            float candidateRight = currentLeft + targetWidth;
            float? snapX = ParentCanvas.GetSnapX(element, candidateRight);
            if (snapX != null)
            {
                newWidth = MathF.Max(minWidth, snapX.Value - currentLeft);
            }
            else
            {
                newWidth = MathF.Max(minWidth, targetWidth);
            }

            float candidateBottom = currentTop + targetHeight;
            float? snapYVal = ParentCanvas.GetSnapY(element, candidateBottom);
            if (snapYVal != null)
            {
                newHeight = MathF.Max(minHeight, snapYVal.Value - currentTop);
            }
            else
            {
                newHeight = MathF.Max(minHeight, targetHeight);
            }
        }

        Canvas.SetLeft(element, newLeft);
        Canvas.SetTop(element, newTop);
        element.Width = newWidth;
        element.Height = newHeight;

        UpdatePositionAndSize();
        element.InvalidateMeasure();
        element.InvalidateArrange();
        element.Invalidate();
        
        ParentCanvas.InvalidateArrange();
        ParentCanvas.Invalidate();
    }

    public override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var borderBrush = ThemeManager.GetBrush("SystemAccentColor", ActualTheme);
        var borderPen = new Pen(borderBrush, 1.5f);
        
        Rect borderRect = new Rect(0, 0, Size.X, Size.Y);
        context.DrawRectangle(null, borderPen, borderRect);
    }
}
