using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public interface IScrollViewportAware
{
    void OnScrollViewportChanged();
}

[ContentProperty(Name = "Content")]
public class ScrollViewer : ContentControl
{
    private float _verticalOffset;
    private float _horizontalOffset;
    private readonly SceneTransformHandle _contentTranslation = new();
    private readonly SceneTransformHandle _scrollbarThumbTranslation = new();
    private readonly GpuPictureRecorder _scrollbarRecorder = new();
    private GpuPicture? _scrollbarThumbPicture;
    private bool _useRetainedContentTranslation;
    private bool _hasViewportDeferredContent;
    private int _retainedViewportBandX = int.MinValue;
    private int _retainedViewportBandY = int.MinValue;
    private bool _scrollbarChromeDirty = true;
    private ElementTheme _scrollbarPictureTheme;
    private VisualThemeFamily _scrollbarPictureThemeFamily;
    private bool _scrollbarPictureHovered;
    
    private bool _isDraggingVert;
    private float _dragStartOffset;
    private float _dragStartMouseY;
    private bool _isPointerOverScrollbar;

    public new FrameworkElement? Content
    {
        get => base.Content as FrameworkElement;
        set
        {
            if (base.Content is FrameworkElement oldContent && !ReferenceEquals(oldContent, value))
            {
                oldContent.LayoutTranslation = Vector2.Zero;
                oldContent.RetainedTransform = null;
            }

            base.Content = value;
            _useRetainedContentTranslation = false;
            _hasViewportDeferredContent = false;
            _retainedViewportBandX = int.MinValue;
            _retainedViewportBandY = int.MinValue;
            ChildrenRetainedTransform = null;
            UpdateContentTranslation();
        }
    }

    private bool IsInsidePopup()
    {
        Visual? parent = Parent;
        while (parent != null)
        {
            if (parent is FrameworkElement fe && PopupService.ActivePopups.Contains(fe))
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    public float VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            float maxScroll = Math.Max(0f, ContentHeight - Size.Y);
            float clamped = Math.Clamp(value, 0f, maxScroll);
            if (_verticalOffset != clamped)
            {
                _verticalOffset = clamped;
                if (PopupService.ActivePopups.Count != 0 && !IsInsidePopup())
                {
                    PopupService.DismissNonDialogPopups();
                }
                NotifyVirtualizingContent();
                UpdateContentTranslation();
                UpdateScrollbarThumbTranslation();
                InvalidateForScroll();
                OnPropertyChanged();
            }
        }
    }

    public float HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            float maxScroll = Math.Max(0f, ContentWidth - Size.X);
            float clamped = Math.Clamp(value, 0f, maxScroll);
            if (_horizontalOffset != clamped)
            {
                _horizontalOffset = clamped;
                if (PopupService.ActivePopups.Count != 0 && !IsInsidePopup())
                {
                    PopupService.DismissNonDialogPopups();
                }
                NotifyVirtualizingContent();
                UpdateContentTranslation();
                InvalidateForScroll();
                OnPropertyChanged();
            }
        }
    }

    public float ContentHeight => Content?.DesiredSize.Y ?? Size.Y;
    public float ContentWidth => Content?.DesiredSize.X ?? Size.X;

    public ScrollViewer()
    {
        Padding = new Thickness(0);
        
        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    public override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            float maxScroll = Math.Max(0f, ContentHeight - Size.Y);
            if (maxScroll > 0f)
            {
                float delta = -e.WheelDelta * 30f;
                float targetOffset = Math.Clamp(_verticalOffset + delta, 0f, maxScroll);
                if (targetOffset != _verticalOffset)
                {
                    VerticalOffset = targetOffset;
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnPointerWheelChanged(e);
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
            float scrollbarWidth = 8f;
            float contentHeight = ContentHeight;
            float viewportHeight = Size.Y;

            if (contentHeight > viewportHeight && localPos.X >= Size.X - scrollbarWidth - 4f)
            {
                float thumbHeight = Math.Max(20f, (viewportHeight / contentHeight) * viewportHeight);
                float scrollableHeight = contentHeight - viewportHeight;
                float thumbY = (VerticalOffset / scrollableHeight) * (viewportHeight - thumbHeight);

                if (localPos.Y >= thumbY && localPos.Y <= thumbY + thumbHeight)
                {
                    _isDraggingVert = true;
                    _dragStartOffset = VerticalOffset;
                    _dragStartMouseY = localPos.Y;
                    InputSystem.CapturePointer(this);
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnPointerPressed(e);
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_isDraggingVert)
        {
            _isDraggingVert = false;
            InputSystem.ReleasePointerCapture();
        }
        base.OnPointerReleased(e);
    }

    public override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
            UpdateScrollbarHover(localPos.X >= Size.X - 12f);
        }
        base.OnPointerEntered(e);
    }

    public override void OnPointerExited(PointerRoutedEventArgs e)
    {
        UpdateScrollbarHover(false);
        base.OnPointerExited(e);
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
            UpdateScrollbarHover(localPos.X >= Size.X - 12f);
        }

        if (_isDraggingVert && IsEnabled)
        {
            var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
            float contentHeight = ContentHeight;
            float viewportHeight = Size.Y;
            float thumbHeight = Math.Max(20f, (viewportHeight / contentHeight) * viewportHeight);
            float scrollableHeight = contentHeight - viewportHeight;
            float trackLength = viewportHeight - thumbHeight;

            if (trackLength > 0f)
            {
                float deltaY = localPos.Y - _dragStartMouseY;
                VerticalOffset = _dragStartOffset + (deltaY / trackLength) * scrollableHeight;
            }
            e.Handled = true;
            return;
        }
        base.OnPointerMoved(e);
    }

    private void UpdateScrollbarHover(bool isPointerOverScrollbar)
    {
        if (_isPointerOverScrollbar == isPointerOverScrollbar)
        {
            return;
        }

        _isPointerOverScrollbar = isPointerOverScrollbar;
        _scrollbarChromeDirty = true;
        Invalidate();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (HasTemplate)
        {
            return base.MeasureOverride(availableSize);
        }
        if (Content != null)
        {
            // Measure child with infinite bounds to let it compute its full desired sizing
            Content.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
        }
        
        float w = WidthConstraint ?? availableSize.X;
        float h = HeightConstraint ?? availableSize.Y;
        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            return;
        }
        
        if (Content != null)
        {
            float contentW = Content.DesiredSize.X;
            float contentH = Content.DesiredSize.Y;
 
            float viewportW = arrangeRect.Width;
            float viewportH = arrangeRect.Height;
 
            // Keep the child's layout rectangle stable. The post-layout translation moves
            // retained render/input content without recursively arranging the subtree for
            // each wheel or trackpad offset.
            Rect childRect = new Rect(
                arrangeRect.X,
                arrangeRect.Y,
                Math.Max(viewportW, contentW),
                Math.Max(viewportH, contentH)
            );
            Content.Arrange(childRect);
            _useRetainedContentTranslation = Content.SupportsRetainedTransformSubtree();
            _hasViewportDeferredContent = ContainsViewportDeferredContent(Content);
            UpdateRetainedViewportBand();
            ChildrenRetainedTransform = _useRetainedContentTranslation ? _contentTranslation : null;
            NotifyVirtualizingContent();
            UpdateContentTranslation();
        }
        ClipBounds = new Rect(0f, 0f, Size.X, Size.Y);
        _scrollbarChromeDirty = true;
        UpdateScrollbarThumbTranslation();
    }

    private void UpdateContentTranslation()
    {
        if (Content != null)
        {
            var translation = new Vector2(-_horizontalOffset, -_verticalOffset);
            if (_useRetainedContentTranslation)
            {
                Content.LayoutTranslation = Vector2.Zero;
                Content.RetainedTransform = null;
                ChildrenRetainedTransform = _contentTranslation;
                _contentTranslation.Translation = translation;
            }
            else
            {
                Content.RetainedTransform = null;
                ChildrenRetainedTransform = null;
                Content.LayoutTranslation = translation;
            }
        }
    }

    private void InvalidateForScroll()
    {
        if (_useRetainedContentTranslation)
        {
            if (_hasViewportDeferredContent && UpdateRetainedViewportBand())
            {
                // Deferred content is compiled for the viewport plus one viewport of overscan.
                // Crossing a half-viewport band refreshes residency before the visible edge can
                // reach content omitted by the previous compilation.
                Invalidate();
            }
            else
            {
                InvalidateRetainedTransform();
            }
        }
        else
        {
            Invalidate();
        }
    }

    private bool UpdateRetainedViewportBand()
    {
        int bandX = GetRetainedViewportBand(_horizontalOffset, Size.X);
        int bandY = GetRetainedViewportBand(_verticalOffset, Size.Y);
        bool changed = bandX != _retainedViewportBandX || bandY != _retainedViewportBandY;
        _retainedViewportBandX = bandX;
        _retainedViewportBandY = bandY;
        return changed;
    }

    private static int GetRetainedViewportBand(float offset, float viewportExtent)
    {
        float bandExtent = MathF.Max(1f, viewportExtent * 0.5f);
        return (int)MathF.Floor(MathF.Max(0f, offset) / bandExtent);
    }

    private static bool ContainsViewportDeferredContent(Visual visual)
    {
        if (visual.IsViewportDeferred)
        {
            return true;
        }
        if (visual is not ContainerVisual container)
        {
            return false;
        }

        var children = container.Children;
        for (int index = 0; index < children.Count; index++)
        {
            if (ContainsViewportDeferredContent(children[index]))
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateScrollbarThumbTranslation()
    {
        float contentHeight = ContentHeight;
        float viewportHeight = Size.Y;
        if (contentHeight <= viewportHeight || viewportHeight <= 0f)
        {
            _scrollbarThumbTranslation.Translation = Vector2.Zero;
            return;
        }

        float thumbHeight = Math.Max(24f, (viewportHeight / contentHeight) * viewportHeight);
        float scrollableHeight = contentHeight - viewportHeight;
        float thumbY = (VerticalOffset / scrollableHeight) * (viewportHeight - thumbHeight);
        _scrollbarThumbTranslation.Translation = new Vector2(0f, thumbY);
    }

    private void NotifyVirtualizingContent()
    {
        if (Content is IScrollViewportAware scrollAwareContent)
        {
            scrollAwareContent.OnScrollViewportChanged();
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (HasTemplate)
        {
            base.OnRender(context);
            return;
        }
        // Draw main background
        var bg = Background ?? ThemeManager.GetBrush("PageBackground");
        context.DrawRectangle(bg, null, new Rect(Vector2.Zero, Size));

        context.PushClip(new Rect(Vector2.Zero, Size));
        base.OnRender(context);
        context.PopClip();

        // Draw vertical scrollbar if content overflows viewport height
        float contentHeight = ContentHeight;
        float viewportHeight = Size.Y;

        if (contentHeight > viewportHeight)
        {
            // Dynamic expanding scrollbar thickness based on hover state
            float scrollbarWidth = (_isPointerOverScrollbar || _isDraggingVert) ? 8f : 3f;
            float padding = (_isPointerOverScrollbar || _isDraggingVert) ? 2f : 4f;

            float thumbHeight = Math.Max(24f, (viewportHeight / contentHeight) * viewportHeight);
            Rect trackRect = new Rect(Size.X - scrollbarWidth - padding, 0f, scrollbarWidth, viewportHeight);

            // Draw track (subtle translucent backdrop line)
            Brush trackBg = (_isPointerOverScrollbar || _isDraggingVert) 
                ? ThemeManager.GetBrush("ControlBackgroundHover") 
                : ThemeManager.GetBrush("ControlBackground");
            context.DrawRectangle(trackBg, null, trackRect);

            bool hovered = _isPointerOverScrollbar || _isDraggingVert;
            if (_scrollbarThumbPicture == null ||
                _scrollbarChromeDirty ||
                _scrollbarPictureTheme != ActualTheme ||
                _scrollbarPictureThemeFamily != ActualThemeFamily ||
                _scrollbarPictureHovered != hovered)
            {
                _scrollbarThumbPicture?.Dispose();
                var recorder = _scrollbarRecorder.BeginRecording(
                    new Rect(Size.X - scrollbarWidth - padding, 0f, scrollbarWidth, thumbHeight));
                Brush thumbBg = hovered
                    ? ThemeManager.GetBrush("ScrollbarThumbHover")
                    : ThemeManager.GetBrush("ScrollbarThumb");
                recorder.DrawRoundedRectangle(
                    thumbBg,
                    null,
                    new Rect(Size.X - scrollbarWidth - padding, 0f, scrollbarWidth, thumbHeight),
                    scrollbarWidth / 2f);
                _scrollbarThumbPicture = _scrollbarRecorder.EndRecording();
                _scrollbarPictureTheme = ActualTheme;
                _scrollbarPictureThemeFamily = ActualThemeFamily;
                _scrollbarPictureHovered = hovered;
                _scrollbarChromeDirty = false;
            }
            context.DrawPicture(_scrollbarThumbPicture, _scrollbarThumbTranslation);
        }
    }
}
