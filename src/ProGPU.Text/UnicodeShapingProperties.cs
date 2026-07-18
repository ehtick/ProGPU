using System.Globalization;
using System.Text;

namespace ProGPU.Text;

/// <summary>
/// Provides the generated Unicode properties shared by managed and GPU text
/// shaping implementations.
/// </summary>
public static class UnicodeShapingProperties
{
    public static byte GetArabicJoiningType(uint codePoint) =>
        IsScalar(codePoint) ? (byte)ArabicJoiningData.GetJoiningType(codePoint) : (byte)0;

    public static byte GetCanonicalCombiningClass(uint codePoint) =>
        IsScalar(codePoint) ? UnicodeCombiningClassData.GetCanonicalClass(codePoint) : (byte)0;

    public static ushort GetIndicProperties(uint codePoint) =>
        IsScalar(codePoint) ? IndicShapingData.GetProperties(codePoint) : (ushort)0;

    public static byte GetUseCategory(uint codePoint) =>
        IsScalar(codePoint) ? UseShapingData.GetCategory(codePoint) : (byte)0;

    public static bool IsMark(uint codePoint)
    {
        if (!IsScalar(codePoint)) return false;
        UnicodeCategory category = Rune.GetUnicodeCategory(new Rune(checked((int)codePoint)));
        return category is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsScalar(uint codePoint) =>
        codePoint <= 0x10ffffu && (codePoint < 0xd800u || codePoint > 0xdfffu);
}
