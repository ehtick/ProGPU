using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Xaml.Controls;

public class VirtualizingPanel : Panel
{
    private float _scrollOffset = 0f;
    private readonly ScrollBarOverlay _scrollbar;

    public float ScrollOffset
    {
        get
        {
            var sv = ScrollViewerOwner;
            if (sv != null)
            {
                return sv.VerticalOffset;
            }
            return _scrollOffset;
        }
        set
        {
            var sv = ScrollViewerOwner;
            if (sv != null)
            {
                sv.VerticalOffset = value;
            }
            else
            {
                float maxScroll = Math.Max(0f, TotalVirtualHeight - Size.Y);
                float clamped = Math.Clamp(value, 0f, maxScroll);
                if (_scrollOffset != clamped)
                {
                    _scrollOffset = clamped;
                    OnScrollOffsetChanged(clamped);
                    Invalidate();
                }
            }
        }
    }

    public virtual float TotalVirtualHeight => 0f;

    public ScrollViewer? ScrollViewerOwner
    {
        get
        {
            DependencyObject? parent = Parent as DependencyObject;
            while (parent != null)
            {
                if (parent is ScrollViewer sv) return sv;
                parent = parent.Parent as DependencyObject;
            }
            return null;
        }
    }

    public float ViewportWidth
    {
        get
        {
            var sv = ScrollViewerOwner;
            if (sv != null)
            {
                // Subtract vertical scrollbar gutter if vertical scrollbar is visible to prevent items from underlapping it
                float scrollbarGutter = (sv.ContentHeight > sv.Size.Y) ? 12f : 0f;
                return Math.Max(0f, sv.Size.X - scrollbarGutter);
            }
            return Size.X;
        }
    }
    public float ViewportHeight => ScrollViewerOwner != null ? ScrollViewerOwner.Size.Y : Size.Y;

    public VirtualizingPanel()
    {
        _scrollbar = new ScrollBarOverlay(this);
    }

    protected virtual void OnScrollOffsetChanged(float newOffset)
    {
    }

    public ItemsControl? ItemsControlOwner
    {
        get
        {
            DependencyObject? parent = Parent as DependencyObject;
            while (parent != null)
            {
                if (parent is ItemsControl ic) return ic;
                parent = parent.Parent as DependencyObject;
            }
            return null;
        }
    }

    public int GetItemsCount()
    {
        return ItemsControlOwner?.Items.Count ?? 0;
    }

    public object? GetItemAt(int index)
    {
        var ic = ItemsControlOwner;
        if (ic != null && index >= 0 && index < ic.Items.Count)
        {
            return ic.Items[index];
        }
        return null;
    }

    public Visual? GetOrCreateContainerForItem(int index)
    {
        var ic = ItemsControlOwner;
        if (ic == null) return null;

        var item = GetItemAt(index);
        if (item == null) return null;

        if (ic.ItemTemplate != null)
        {
            var container = ic.ItemTemplate();
            return container;
        }

        // Default container
        return new Border();
    }

    public virtual void ForceRebind()
    {
        InvalidateMeasure();
        Invalidate();
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        // Enforce boundary clipping to prevent scrolled-out items from leaking
        ClipBounds = new Rect(0f, 0f, arrangeRect.Width, arrangeRect.Height);
        base.ArrangeOverride(arrangeRect);

        // Notify of scroll offset change to update viewport during layout arrange pass
        OnScrollOffsetChanged(ScrollOffset);

        // Keep scrollbar overlay layout sized correctly and always topmost in Z-order
        if (ScrollViewerOwner == null && _scrollbar != null)
        {
            _scrollbar.Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
            
            // Re-adding it moves it to the end of the children list, putting it on top of all item cards!
            base.AddChild(_scrollbar);
            
            _scrollbar.Measure(new Vector2(arrangeRect.Width, arrangeRect.Height));
            _scrollbar.Arrange(new Rect(0f, 0f, arrangeRect.Width, arrangeRect.Height));
        }
        else if (ScrollViewerOwner != null && _scrollbar != null)
        {
            RemoveChild(_scrollbar);
        }
    }

    public override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if (ScrollViewerOwner == null && IsEnabled)
        {
            float maxScroll = Math.Max(0f, TotalVirtualHeight - Size.Y);
            if (maxScroll > 0f)
            {
                float delta = -e.WheelDelta * 40f; // Scroll by 40px per wheel tick
                float targetOffset = Math.Clamp(ScrollOffset + delta, 0f, maxScroll);
                if (targetOffset != ScrollOffset)
                {
                    ScrollOffset = targetOffset;
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnPointerWheelChanged(e);
    }

    private class ScrollBarOverlay : FrameworkElement
    {
        private readonly VirtualizingPanel _panel;
        private bool _isDragging = false;
        private bool _isHovered = false;
        private float _dragStartOffset = 0f;
        private float _dragStartMouseY = 0f;

        public ScrollBarOverlay(VirtualizingPanel panel)
        {
            _panel = panel;
        }

        public override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            if (IsEnabled)
            {
                var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
                float scrollbarWidth = 8f;
                float contentHeight = _panel.TotalVirtualHeight;
                float viewportHeight = Size.Y;

                if (contentHeight > viewportHeight && localPos.X >= Size.X - scrollbarWidth - 4f)
                {
                    float thumbHeight = Math.Max(24f, (viewportHeight / contentHeight) * viewportHeight);
                    float scrollableHeight = contentHeight - viewportHeight;
                    float thumbY = (_panel.ScrollOffset / scrollableHeight) * (viewportHeight - thumbHeight);

                    if (localPos.Y >= thumbY && localPos.Y <= thumbY + thumbHeight)
                    {
                        _isDragging = true;
                        _dragStartOffset = _panel.ScrollOffset;
                        _dragStartMouseY = localPos.Y;
                        InputSystem.CapturePointer(this);
                        e.Handled = true;
                        Invalidate();
                        return;
                    }
                }
            }
            base.OnPointerPressed(e);
        }

        public override void OnPointerReleased(PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                InputSystem.ReleasePointerCapture();
                Invalidate();
            }
            base.OnPointerReleased(e);
        }

        public override void OnPointerEntered(PointerRoutedEventArgs e)
        {
            if (IsEnabled)
            {
                _isHovered = true;
                Invalidate();
            }
            base.OnPointerEntered(e);
        }

        public override void OnPointerExited(PointerRoutedEventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnPointerExited(e);
        }

        public override void OnPointerMoved(PointerRoutedEventArgs e)
        {
            if (IsEnabled)
            {
                var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
                bool over = localPos.X >= Size.X - 12f;
                if (_isHovered != over)
                {
                    _isHovered = over;
                    Invalidate();
                }
            }

            if (_isDragging && IsEnabled)
            {
                var localPos = InputSystem.GetLocalPosition(this, e.ScreenPosition);
                float contentHeight = _panel.TotalVirtualHeight;
                float viewportHeight = Size.Y;
                float thumbHeight = Math.Max(24f, (viewportHeight / contentHeight) * viewportHeight);
                float scrollableHeight = contentHeight - viewportHeight;
                float trackLength = viewportHeight - thumbHeight;

                if (trackLength > 0f)
                {
                    float deltaY = localPos.Y - _dragStartMouseY;
                    _panel.ScrollOffset = _dragStartOffset + (deltaY / trackLength) * scrollableHeight;
                }
                e.Handled = true;
                return;
            }
            base.OnPointerMoved(e);
        }

        public override void OnRender(DrawingContext context)
        {
            float contentHeight = _panel.TotalVirtualHeight;
            float viewportHeight = Size.Y;

            if (contentHeight > viewportHeight)
            {
                float scrollbarWidth = (_isHovered || _isDragging) ? 8f : 3f;
                float padding = (_isHovered || _isDragging) ? 2f : 4f;

                float thumbHeight = Math.Max(24f, (viewportHeight / contentHeight) * viewportHeight);
                float scrollableHeight = contentHeight - viewportHeight;
                float thumbY = (_panel.ScrollOffset / scrollableHeight) * (viewportHeight - thumbHeight);

                Rect trackRect = new Rect(Size.X - scrollbarWidth - padding, 0f, scrollbarWidth, viewportHeight);
                Rect thumbRect = new Rect(Size.X - scrollbarWidth - padding, thumbY, scrollbarWidth, thumbHeight);

                // Draw track (subtle translucent backdrop line)
                Brush trackBg = (_isHovered || _isDragging) 
                    ? ThemeManager.GetBrush("ControlBackgroundHover") 
                    : ThemeManager.GetBrush("ControlBackground");
                context.DrawRectangle(trackBg, null, trackRect);

                // Draw thumb (glassmorphic capsule)
                Brush thumbBg = (_isHovered || _isDragging)
                    ? ThemeManager.GetBrush("ScrollbarThumbHover")
                    : ThemeManager.GetBrush("ScrollbarThumb");
                context.DrawRoundedRectangle(thumbBg, null, thumbRect, scrollbarWidth / 2f);
            }
        }
    }
}
