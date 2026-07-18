using System.Runtime.InteropServices;
using ProGPU.Text;

namespace ProGPU.Compute;

/// <summary>A half-open range of identical Unicode shaping properties.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct GpuUnicodePropertyRange(
    uint Start,
    uint End,
    uint PropertiesA,
    uint PropertiesB);

/// <summary>
/// Process-wide Unicode 17 shaping properties compressed into ranges suitable
/// for binary search by WebGPU compute shaders.
/// </summary>
public static class GpuUnicodeShapingPlan
{
    private static readonly Lazy<ReadOnlyMemory<GpuUnicodePropertyRange>> s_ranges =
        new(CreateRanges, LazyThreadSafetyMode.ExecutionAndPublication);

    public static ReadOnlyMemory<GpuUnicodePropertyRange> Ranges => s_ranges.Value;

    private static ReadOnlyMemory<GpuUnicodePropertyRange> CreateRanges()
    {
        var ranges = new List<GpuUnicodePropertyRange>(4096);
        uint start = 0;
        (uint A, uint B) current = GetProperties(0);
        for (uint codePoint = 1; codePoint <= 0x10ffffu; codePoint++)
        {
            (uint A, uint B) next = GetProperties(codePoint);
            if (next == current) continue;
            ranges.Add(new GpuUnicodePropertyRange(start, codePoint, current.A, current.B));
            start = codePoint;
            current = next;
        }
        ranges.Add(new GpuUnicodePropertyRange(start, 0x110000u, current.A, current.B));
        return ranges.ToArray();
    }

    private static (uint A, uint B) GetProperties(uint codePoint)
    {
        uint propertiesA = UnicodeShapingProperties.GetArabicJoiningType(codePoint) |
            ((uint)UnicodeShapingProperties.GetCanonicalCombiningClass(codePoint) << 8) |
            ((uint)UnicodeShapingProperties.GetIndicProperties(codePoint) << 16);
        uint propertiesB = UnicodeShapingProperties.GetUseCategory(codePoint) |
            (UnicodeShapingProperties.IsMark(codePoint) ? 1u << 8 : 0u);
        return (propertiesA, propertiesB);
    }
}
