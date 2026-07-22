using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;

namespace Microsoft.UI.Xaml.Controls;

internal readonly record struct ScrollBarMetrics(
    float TrackStart,
    float TrackLength,
    float ThumbStart,
    float ThumbLength,
    float Maximum);

internal static class ScrollBarInteraction
{
    public const float CollapsedThickness = 3f;
    public const float ExpandedThickness = 8f;
    public const float ExpandedPadding = 2f;
    public const float CollapsedPadding = 4f;
    public const float MinimumThumbLength = 24f;

    public static float GetCrossAxisHitTarget(PointerDeviceType deviceType) => deviceType switch
    {
        PointerDeviceType.Touch => 32f,
        PointerDeviceType.Pen => 20f,
        _ => 12f
    };

    public static float GetAlongAxisHitTarget(PointerDeviceType deviceType) => deviceType switch
    {
        PointerDeviceType.Touch => 44f,
        PointerDeviceType.Pen => 28f,
        _ => MinimumThumbLength
    };

    public static bool CapturePointer(FrameworkElement owner, PointerRoutedEventArgs e)
    {
        if (e.Pointer.IsInContact) return owner.CapturePointer(e.Pointer);
        InputSystem.CapturePointer(owner);
        return true;
    }

    public static void ReleasePointer(FrameworkElement owner, PointerRoutedEventArgs e)
    {
        if (e.Pointer.IsInContact) owner.ReleasePointerCapture(e.Pointer);
        else InputSystem.ReleasePointerCapture();
    }

    public static bool TryCreateMetrics(
        float trackStart,
        float viewportLength,
        float extentLength,
        float value,
        out ScrollBarMetrics metrics)
    {
        metrics = default;
        if (!float.IsFinite(trackStart) || !float.IsFinite(viewportLength) ||
            !float.IsFinite(extentLength) || viewportLength <= 0f || extentLength <= viewportLength)
        {
            return false;
        }

        var maximum = extentLength - viewportLength;
        var thumbLength = Math.Min(viewportLength,
            Math.Max(MinimumThumbLength, (viewportLength / extentLength) * viewportLength));
        var travel = Math.Max(0f, viewportLength - thumbLength);
        var clampedValue = Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, maximum);
        var thumbStart = trackStart + (maximum > 0f ? (clampedValue / maximum) * travel : 0f);
        metrics = new ScrollBarMetrics(trackStart, viewportLength, thumbStart, thumbLength, maximum);
        return true;
    }

    public static bool IsVerticalTrackHit(float x, float width, PointerDeviceType deviceType) =>
        x >= Math.Max(0f, width - GetCrossAxisHitTarget(deviceType)) && x <= width;

    public static bool IsHorizontalTrackHit(float y, float height, PointerDeviceType deviceType) =>
        y >= Math.Max(0f, height - GetCrossAxisHitTarget(deviceType)) && y <= height;

    public static bool IsThumbHit(float position, ScrollBarMetrics metrics, PointerDeviceType deviceType)
    {
        var hitLength = Math.Max(metrics.ThumbLength, GetAlongAxisHitTarget(deviceType));
        var expansion = (hitLength - metrics.ThumbLength) * 0.5f;
        var hitStart = Math.Max(metrics.TrackStart, metrics.ThumbStart - expansion);
        var hitEnd = Math.Min(metrics.TrackStart + metrics.TrackLength,
            metrics.ThumbStart + metrics.ThumbLength + expansion);
        return position >= hitStart && position <= hitEnd;
    }

    public static float ValueFromDrag(float startValue, float delta, ScrollBarMetrics metrics)
    {
        var travel = metrics.TrackLength - metrics.ThumbLength;
        if (travel <= 0f || metrics.Maximum <= 0f) return 0f;
        return Math.Clamp(startValue + (delta / travel) * metrics.Maximum, 0f, metrics.Maximum);
    }

    public static float ValueFromTrackPress(
        float currentValue,
        float position,
        ScrollBarMetrics metrics,
        float viewportLength)
    {
        var page = Math.Max(1f, viewportLength * 0.9f);
        if (position < metrics.ThumbStart) return Math.Max(0f, currentValue - page);
        if (position > metrics.ThumbStart + metrics.ThumbLength) return Math.Min(metrics.Maximum, currentValue + page);
        return currentValue;
    }
}
