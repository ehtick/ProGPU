namespace ProGPU.Text.Shaping;

/// <summary>
/// Applies the compatibility policy used by HarfBuzz for fonts whose GDEF
/// metadata is known to contradict their GSUB or GPOS content.
/// </summary>
public static class OpenTypeGdefPolicy
{
    /// <summary>
    /// Returns whether GDEF must be ignored for this exact table-length tuple.
    /// The fixed comparison set is allocation-free and independent of glyph count.
    /// </summary>
    public static bool IsBlocklisted(int gdefLength, int gsubLength, int gposLength) =>
        (gdefLength, gsubLength, gposLength) is
            (442, 2874, 42038) or
            (430, 2874, 40662) or
            (442, 2874, 39116) or
            (430, 2874, 39374) or
            (490, 3046, 41638) or
            (478, 3046, 41902) or
            (898, 12554, 46470) or
            (910, 12566, 47732) or
            (928, 23298, 59332) or
            (940, 23310, 60732) or
            (964, 23836, 60072) or
            (976, 23832, 61456) or
            (994, 24474, 60336) or
            (1006, 24470, 61740) or
            (1006, 24576, 61346) or
            (1018, 24572, 62828) or
            (1006, 24576, 61352) or
            (1018, 24572, 62834) or
            (832, 7324, 47162) or
            (844, 7302, 45474) or
            (180, 13054, 7254) or
            (192, 12638, 7254) or
            (192, 12690, 7254) or
            (188, 248, 3852) or
            (188, 264, 3426) or
            (1058, 47032, 11818) or
            (1046, 47030, 12600) or
            (1058, 71796, 16770) or
            (1046, 71790, 17862) or
            (1046, 71788, 17112) or
            (1058, 71794, 17514) or
            (1330, 109904, 57938) or
            (1330, 109904, 58972) or
            (1004, 59092, 14836) or
            (588, 5078, 14418) or
            (588, 5078, 14238) or
            (894, 17162, 33960) or
            (894, 17154, 34472) or
            (816, 7868, 17052) or
            (816, 7868, 17138);
}
