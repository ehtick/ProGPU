namespace ProGPU.Android;

internal readonly record struct AndroidScrollDelta(float X, float Y, bool IsPrecise);

internal static class AndroidScrollDeltaPolicy
{
    /// <summary>
    /// Converts Android scroll axes to ProGPU logical-pixel deltas. Android 14+
    /// gesture-distance axes are already expressed in display pixels. Legacy mouse
    /// wheel axes are normalized device units and therefore use ViewConfiguration's
    /// pixel factors. Both paths produce logical pixels, so consumers must not apply
    /// an additional line-height multiplier.
    /// </summary>
    public static AndroidScrollDelta Convert(
        float density,
        float horizontalScrollFactor,
        float verticalScrollFactor,
        float rawHorizontal,
        float rawVertical,
        float gestureHorizontalPixels,
        float gestureVerticalPixels)
    {
        if (!float.IsFinite(density) || density <= 0f)
            density = 1f;

        float horizontalPixels = gestureHorizontalPixels != 0f
            ? gestureHorizontalPixels
            : rawHorizontal * horizontalScrollFactor;
        float verticalPixels = gestureVerticalPixels != 0f
            ? gestureVerticalPixels
            : rawVertical * verticalScrollFactor;

        float x = float.IsFinite(horizontalPixels) ? horizontalPixels / density : 0f;
        float y = float.IsFinite(verticalPixels) ? verticalPixels / density : 0f;
        return new AndroidScrollDelta(x, y, IsPrecise: x != 0f || y != 0f);
    }
}
