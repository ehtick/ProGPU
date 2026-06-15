using System;
using System.Collections.Generic;
using System.IO;
using ProGPU.Text;

namespace SkiaSharp;

public enum SKFontStyleWeight
{
    Invisible = 0,
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900,
    ExtraBlack = 1000,
}

public enum SKFontStyleWidth
{
    UltraCondensed = 1,
    ExtraCondensed = 2,
    Condensed = 3,
    SemiCondensed = 4,
    Normal = 5,
    SemiExpanded = 6,
    Expanded = 7,
    ExtraExpanded = 8,
    UltraExpanded = 9,
}

public class SKFontStyle
{
    public SKFontStyleWeight Weight { get; }
    public SKFontStyleWidth Width { get; }
    public SKFontStyleSlant Slant { get; }

    public SKFontStyle(SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
    {
        Weight = weight;
        Width = width;
        Slant = slant;
    }

    public static readonly SKFontStyle Normal = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    public static readonly SKFontStyle Italic = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
    public static readonly SKFontStyle Bold = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    public static readonly SKFontStyle BoldItalic = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
}

public class SKTypeface : IDisposable
{
    public TtfFont Font { get; }
    public string FamilyName { get; }
    public bool IsBold { get; }
    public bool IsItalic { get; }

    public SKTypeface(TtfFont font, string familyName, bool isBold = false, bool isItalic = false)
    {
        Font = font;
        FamilyName = familyName;
        IsBold = isBold;
        IsItalic = isItalic;
    }

    private static SKTypeface? _default;
    public static SKTypeface Default
    {
        get
        {
            if (_default == null)
            {
                // Fallback to standard system Arial or first available font
                var systemFonts = FontApi.GetSystemFonts();
                FontInfo? selectedFont = null;
                foreach (var f in systemFonts)
                {
                    if (f.FamilyName.Equals("Arial", StringComparison.OrdinalIgnoreCase) ||
                        f.FamilyName.Equals("Helvetica", StringComparison.OrdinalIgnoreCase) ||
                        f.FamilyName.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedFont = f;
                        break;
                    }
                }
                if (selectedFont == null && systemFonts.Count > 0)
                {
                    selectedFont = systemFonts[0];
                }
                if (selectedFont != null && !string.IsNullOrEmpty(selectedFont.FilePath))
                {
                    _default = new SKTypeface(CreateFont(selectedFont), "Default");
                }
                else
                {
                    throw new InvalidOperationException("No system fonts found to initialize default typeface.");
                }
            }
            return _default;
        }
    }

    public static SKTypeface FromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var font = new TtfFont(ms.ToArray());
        return new SKTypeface(font, "StreamFont");
    }

    public static SKTypeface FromFile(string path)
    {
        var font = new TtfFont(path);
        return new SKTypeface(font, Path.GetFileNameWithoutExtension(path));
    }

    public static SKTypeface FromFamilyName(string familyName, SKFontStyle style)
    {
        var systemFonts = FontApi.GetSystemFonts();
        FontInfo? fallback = null;
        foreach (var font in systemFonts)
        {
            if (!font.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallback ??= font;
            if (!MatchesStyle(font, style))
            {
                continue;
            }

            try
            {
                var ttf = CreateFont(font);
                bool isBold = style.Weight >= SKFontStyleWeight.SemiBold;
                bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                return new SKTypeface(ttf, font.FamilyName, isBold, isItalic);
            }
            catch
            {
                // Skip and try next
            }
        }

        if (fallback != null)
        {
            try
            {
                var ttf = CreateFont(fallback);
                bool isBold = style.Weight >= SKFontStyleWeight.SemiBold;
                bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                return new SKTypeface(ttf, fallback.FamilyName, isBold, isItalic);
            }
            catch
            {
                // Fall through to default.
            }
        }

        return Default;
    }

    private static bool MatchesStyle(FontInfo font, SKFontStyle style)
    {
        var name = font.Name;
        var wantsBold = style.Weight >= SKFontStyleWeight.SemiBold;
        var wantsItalic = style.Slant != SKFontStyleSlant.Upright;
        var isBold = ContainsStyleToken(name, "bold") || ContainsStyleToken(name, "semibold") || ContainsStyleToken(name, "demibold") || ContainsStyleToken(name, "black");
        var isItalic = ContainsStyleToken(name, "italic") || ContainsStyleToken(name, "oblique");
        return wantsBold == isBold && wantsItalic == isItalic;
    }

    private static bool ContainsStyleToken(string value, string token)
    {
        return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public SKFont CreateSKFont(float size)
    {
        return new SKFont(this, size);
    }

    public void Dispose() { }

    internal static TtfFont CreateFont(FontInfo font)
    {
        return new TtfFont(font.FilePath, font.FaceIndex);
    }
}

public class SKFontManager : IDisposable
{
    private static readonly SKFontManager _defaultInstance = new();
    public static SKFontManager Default => _defaultInstance;

    public static SKFontManager CreateDefault() => new();

    public string[] GetFontFamilies()
    {
        var list = FontApi.GetSystemFonts();
        var names = new List<string>();
        foreach (var f in list)
        {
            if (!names.Contains(f.FamilyName))
            {
                names.Add(f.FamilyName);
            }
        }
        return names.ToArray();
    }

    public SKTypeface MatchFamily(string familyName, SKFontStyle style)
    {
        return SKTypeface.FromFamilyName(familyName, style);
    }

    public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[] bcp47, int codepoint)
    {
        var systemFonts = FontApi.GetSystemFonts();
        // First try the requested family
        if (!string.IsNullOrEmpty(familyName))
        {
            foreach (var font in systemFonts)
            {
                if (font.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var ttf = SKTypeface.CreateFont(font);
                        if (ttf.GetGlyphIndex((uint)codepoint) != 0)
                        {
                            bool isBold = style.Weight >= SKFontStyleWeight.SemiBold;
                            bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                            return new SKTypeface(ttf, font.FamilyName, isBold, isItalic);
                        }
                    }
                    catch { }
                }
            }
        }

        // Search other fonts that support the character
        foreach (var font in systemFonts)
        {
            try
            {
                var ttf = SKTypeface.CreateFont(font);
                if (ttf.GetGlyphIndex((uint)codepoint) != 0)
                {
                    bool isBold = style.Weight >= SKFontStyleWeight.SemiBold;
                    bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                    return new SKTypeface(ttf, font.FamilyName, isBold, isItalic);
                }
            }
            catch { }
        }

        return null;
    }

    public List<SKFontStyle> GetFontStyles(string familyName)
    {
        var styles = new List<SKFontStyle>();
        var systemFonts = FontApi.GetSystemFonts();
        foreach (var font in systemFonts)
        {
            if (font.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
            {
                // In this basic outline we fallback to normal style or try to parse bold/italic from name
                var slant = SKFontStyleSlant.Upright;
                var weight = SKFontStyleWeight.Normal;
                
                if (font.Name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || font.Name.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
                    slant = SKFontStyleSlant.Italic;
                if (font.Name.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                    weight = SKFontStyleWeight.Bold;
                
                styles.Add(new SKFontStyle(weight, SKFontStyleWidth.Normal, slant));
            }
        }
        if (styles.Count == 0)
        {
            styles.Add(SKFontStyle.Normal);
        }
        return styles;
    }

    public void Dispose() { }

}
