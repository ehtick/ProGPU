namespace ProGPU.Text;

/// <summary>
/// Immutable Unicode 17 presentation-form and compatibility-ligature data used
/// by deterministic shaping backends when an Arabic font has no form lookups.
/// </summary>
public static class ArabicFallbackPlan
{
    public const uint FirstCodePoint = ArabicFallbackData.FirstCodePoint;
    public const uint LastCodePoint = ArabicFallbackData.LastCodePoint;
    public static ReadOnlySpan<ushort> ShapingForms => ArabicFallbackData.ShapingForms;
    public static ReadOnlySpan<ushort> ThreeComponentLigatures => ArabicFallbackData.ThreeComponentLigatures;
    public static ReadOnlySpan<ushort> TwoComponentLigatures => ArabicFallbackData.TwoComponentLigatures;
    public static ReadOnlySpan<ushort> MarkLigatures => ArabicFallbackData.MarkLigatures;
}
