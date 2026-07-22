namespace ProGPU.iOS;

/// <summary>
/// Converts UIKit indirect-input units to ProGPU wheel units without platform types.
/// Each conversion is fixed O(1) work and allocation-free.
/// </summary>
public static class IosIndirectScrollPolicy
{
    public const float PinchWheelUnitsPerNaturalLog = 120f;

    public static float NormalizeScrollDelta(float value, bool precise) =>
        precise || value == 0f ? value : MathF.CopySign(1f, value);

    public static float PinchScaleToWheelDelta(double relativeScale)
    {
        if (!double.IsFinite(relativeScale) || relativeScale <= 0d) return 0f;
        return (float)(Math.Log(relativeScale) * PinchWheelUnitsPerNaturalLog);
    }

    public static float WheelDeltaToScale(float wheelDelta) =>
        MathF.Exp(wheelDelta / PinchWheelUnitsPerNaturalLog);
}
