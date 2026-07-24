using System;
using System.Numerics;
using Microsoft.UI.Xaml.Input;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>
/// A range control that exposes and manipulates a scroll viewport position.
/// </summary>
public sealed class ScrollBar : RangeBase
{
    private readonly Brush _trackBrush = new ThemeResourceBrush("ScrollBarTrackFill");
    private readonly Brush _thumbBrush = new ThemeResourceBrush("ScrollBarThumbFill");
    private bool _isDragging;
    private float _dragOrigin;
    private double _dragValue;

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(ScrollBar),
        new PropertyMetadata(Orientation.Vertical) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ViewportSizeProperty = DependencyProperty.Register(
        nameof(ViewportSize),
        typeof(double),
        typeof(ScrollBar),
        new PropertyMetadata(0d) { AffectsRender = true });

    public static readonly DependencyProperty IndicatorModeProperty = DependencyProperty.Register(
        nameof(IndicatorMode),
        typeof(ScrollingIndicatorMode),
        typeof(ScrollBar),
        new PropertyMetadata(ScrollingIndicatorMode.None) { AffectsRender = true });

    public Orientation Orientation
    {
        get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Vertical);
        set => SetValue(OrientationProperty, value);
    }

    public double ViewportSize
    {
        get => (double)(GetValue(ViewportSizeProperty) ?? 0d);
        set
        {
            if (!double.IsFinite(value) || value < 0d)
                throw new ArgumentOutOfRangeException(nameof(value), "ViewportSize must be finite and non-negative.");
            SetValue(ViewportSizeProperty, value);
        }
    }

    public ScrollingIndicatorMode IndicatorMode
    {
        get => (ScrollingIndicatorMode)(GetValue(IndicatorModeProperty) ?? ScrollingIndicatorMode.None);
        set => SetValue(IndicatorModeProperty, value);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var thickness = 12f;
        return Orientation == Orientation.Horizontal
            ? new Vector2(float.IsFinite(availableSize.X) ? availableSize.X : 100f, thickness)
            : new Vector2(thickness, float.IsFinite(availableSize.Y) ? availableSize.Y : 100f);
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled)
            return;

        var coordinate = GetCoordinate(e.Position);
        var thumb = GetThumbMetrics();
        if (coordinate < thumb.Offset)
        {
            Value -= LargeChange;
            return;
        }

        if (coordinate > thumb.Offset + thumb.Extent)
        {
            Value += LargeChange;
            return;
        }

        _isDragging = true;
        _dragOrigin = coordinate;
        _dragValue = Value;
        InputSystem.CapturePointer(this);
        e.Handled = true;
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var thumb = GetThumbMetrics();
            var movable = Math.Max(0f, thumb.TrackExtent - thumb.Extent);
            var range = Maximum - Minimum;
            if (movable > 0f && range > 0d)
                Value = _dragValue + ((GetCoordinate(e.Position) - _dragOrigin) / movable * range);
            e.Handled = true;
        }

        base.OnPointerMoved(e);
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        EndDrag();
        base.OnPointerReleased(e);
    }

    public override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        EndDrag();
        base.OnPointerCanceled(e);
    }

    public override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        _isDragging = false;
        base.OnPointerCaptureLost(e);
    }

    public override void OnRender(DrawingContext context)
    {
        if (!HasTemplate)
        {
            var track = new Rect(Vector2.Zero, Size);
            context.FillRoundedRectangle(Background ?? _trackBrush, track, Math.Min(track.Width, track.Height) * 0.5f);

            var thumb = GetThumbMetrics();
            var thumbRect = Orientation == Orientation.Horizontal
                ? new Rect(thumb.Offset, 0f, thumb.Extent, Size.Y)
                : new Rect(0f, thumb.Offset, Size.X, thumb.Extent);
            context.FillRoundedRectangle(BorderBrush ?? _thumbBrush, thumbRect, Math.Min(thumbRect.Width, thumbRect.Height) * 0.5f);
        }

        base.OnRender(context);
    }

    private void EndDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        InputSystem.ReleasePointerCapture();
    }

    private float GetCoordinate(Vector2 position) =>
        Orientation == Orientation.Horizontal ? position.X : position.Y;

    private ThumbMetrics GetThumbMetrics()
    {
        var trackExtent = Math.Max(0f, Orientation == Orientation.Horizontal ? Size.X : Size.Y);
        var scrollableRange = Math.Max(0d, Maximum - Minimum);
        var contentExtent = scrollableRange + ViewportSize;
        var fraction = contentExtent > 0d ? ViewportSize / contentExtent : 1d;
        var minimumThumb = Math.Min(trackExtent, 8f);
        var thumbExtent = Math.Clamp((float)(trackExtent * fraction), minimumThumb, trackExtent);
        var movable = Math.Max(0f, trackExtent - thumbExtent);
        var normalized = scrollableRange > 0d ? (Value - Minimum) / scrollableRange : 0d;
        return new ThumbMetrics(trackExtent, thumbExtent, (float)(movable * normalized));
    }

    private readonly record struct ThumbMetrics(float TrackExtent, float Extent, float Offset);
}
