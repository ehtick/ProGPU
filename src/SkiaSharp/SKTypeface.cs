using System;
using System.Collections.Concurrent;
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

public class SKFontStyle : IDisposable
{
    public IntPtr Handle { get; } = SKObjectHandle.Create();
    public int Weight { get; }
    public int Width { get; }
    public SKFontStyleSlant Slant { get; }

    public SKFontStyle(SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant)
        : this((int)weight, (int)width, slant)
    {
    }

    public SKFontStyle(int weight, int width, SKFontStyleSlant slant)
    {
        Weight = weight;
        Width = width;
        Slant = slant;
    }

    public static readonly SKFontStyle Normal = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    public static readonly SKFontStyle Italic = new(SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);
    public static readonly SKFontStyle Bold = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    public static readonly SKFontStyle BoldItalic = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Italic);

    public void Dispose() { }
}

public class SKTypeface : IDisposable
{
    private static readonly ConcurrentDictionary<(string Path, int FaceIndex), Lazy<TtfFont>> s_systemFonts = new();
    private readonly int? _requestedWeight;
    private readonly int? _requestedWidth;
    private readonly SKFontStyleSlant? _requestedSlant;

    public IntPtr Handle { get; } = SKObjectHandle.Create();
    public TtfFont Font { get; }
    public string FamilyName { get; }
    public bool IsBold { get; }
    public bool IsItalic { get; }
    public int FontWeight => _requestedWeight ??
        (Font.WeightClass == 0 ? (int)SKFontStyleWeight.Normal : Font.WeightClass);
    public int FontWidth => _requestedWidth ??
        (Font.WidthClass == 0 ? (int)SKFontStyleWidth.Normal : Font.WidthClass);
    public SKFontStyleSlant FontSlant => _requestedSlant ??
        (Font.IsItalic || IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    public int UnitsPerEm => Font.UnitsPerEm;
    public SKFontStyle FontStyle => new(
        FontWeight,
        Math.Clamp(FontWidth, (int)SKFontStyleWidth.UltraCondensed, (int)SKFontStyleWidth.UltraExpanded),
        FontSlant);

    public SKTypeface(
        TtfFont font,
        string familyName,
        bool isBold = false,
        bool isItalic = false,
        int? requestedWeight = null,
        int? requestedWidth = null,
        SKFontStyleSlant? requestedSlant = null)
    {
        Font = font;
        FamilyName = familyName;
        IsBold = isBold;
        IsItalic = isItalic;
        _requestedWeight = requestedWeight;
        _requestedWidth = requestedWidth;
        _requestedSlant = requestedSlant;
    }

    private static SKTypeface? _default;
    public static SKTypeface Default
    {
        get
        {
            if (_default == null)
            {
                var systemFonts = FontApi.GetSystemFonts();
                var selectedFont = FindPreferredFont(systemFonts, GetGenericFamilyPreferences(GenericFontFamily.SansSerif));
                if (selectedFont == null && systemFonts.Count > 0)
                {
                    selectedFont = systemFonts[0];
                }
                if (selectedFont != null && !string.IsNullOrEmpty(selectedFont.FilePath))
                {
                    _default = new SKTypeface(CreateFont(selectedFont), selectedFont.FamilyName);
                }
                else
                {
                    throw new InvalidOperationException("No system fonts found to initialize default typeface.");
                }
            }
            return _default;
        }
    }

    public static SKTypeface? FromStream(Stream stream, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        try
        {
            var font = new TtfFont(ms.ToArray(), index);
            return new SKTypeface(font, font.FamilyName);
        }
        catch (Exception ex) when (IsInvalidFontException(ex))
        {
            return null;
        }
    }

    public static SKTypeface? FromStream(SKStreamAsset stream, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FromStream(new MemoryStream(stream.Data, writable: false), index);
    }

    public static SKTypeface? FromFile(string path, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            var font = new TtfFont(path, index);
            return new SKTypeface(font, font.FamilyName);
        }
        catch (Exception ex) when (ex is FileNotFoundException || IsInvalidFontException(ex))
        {
            return null;
        }
    }

    private static bool IsInvalidFontException(Exception exception) =>
        exception is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException;

    public static SKTypeface FromFamilyName(string familyName, SKFontStyle style)
    {
        var systemFonts = FontApi.GetSystemFonts();
        if (TryGetGenericFontFamily(familyName, out var genericFamily))
        {
            var genericFont = FindPreferredFont(systemFonts, GetGenericFamilyPreferences(genericFamily));
            if (genericFont != null)
            {
                return CreateSystemTypeface(genericFont, style);
            }
        }

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
                return CreateSystemTypeface(font, style);
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
                return CreateSystemTypeface(fallback, style);
            }
            catch
            {
                // Fall through to default.
            }
        }

        var defaultTypeface = Default;
        return new SKTypeface(
            defaultTypeface.Font,
            defaultTypeface.FamilyName,
            style.Weight >= (int)SKFontStyleWeight.SemiBold,
            style.Slant != SKFontStyleSlant.Upright,
            style.Weight,
            style.Width,
            style.Slant);
    }

    private enum GenericFontFamily
    {
        SansSerif,
        Serif,
        Monospace
    }

    private static bool TryGetGenericFontFamily(string? familyName, out GenericFontFamily family)
    {
        switch (familyName?.Trim().ToLowerInvariant())
        {
            case "sans-serif":
            case "system-ui":
            case "ui-sans-serif":
                family = GenericFontFamily.SansSerif;
                return true;
            case "serif":
            case "ui-serif":
                family = GenericFontFamily.Serif;
                return true;
            case "monospace":
            case "ui-monospace":
                family = GenericFontFamily.Monospace;
                return true;
            default:
                family = default;
                return false;
        }
    }

    private static string[] GetGenericFamilyPreferences(GenericFontFamily family)
    {
        if (OperatingSystem.IsMacOS())
        {
            return family switch
            {
                GenericFontFamily.Serif => new[] { "Times", "Times New Roman" },
                GenericFontFamily.Monospace => new[] { "Menlo", "Monaco", "Courier" },
                _ => new[] { "Helvetica", "Arial" }
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return family switch
            {
                GenericFontFamily.Serif => new[] { "Times New Roman", "Georgia" },
                GenericFontFamily.Monospace => new[] { "Consolas", "Courier New" },
                _ => new[] { "Segoe UI", "Arial" }
            };
        }

        return family switch
        {
            GenericFontFamily.Serif => new[] { "Noto Serif", "DejaVu Serif", "Liberation Serif" },
            GenericFontFamily.Monospace => new[] { "Noto Sans Mono", "DejaVu Sans Mono", "Liberation Mono" },
            _ => new[] { "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial" }
        };
    }

    private static FontInfo? FindPreferredFont(IReadOnlyList<FontInfo> fonts, IReadOnlyList<string> preferences)
    {
        for (var preferenceIndex = 0; preferenceIndex < preferences.Count; preferenceIndex++)
        {
            var preference = preferences[preferenceIndex];
            for (var fontIndex = 0; fontIndex < fonts.Count; fontIndex++)
            {
                var font = fonts[fontIndex];
                if (font.FamilyName.Equals(preference, StringComparison.OrdinalIgnoreCase) ||
                    font.Name.Equals(preference, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }
            }
        }

        return null;
    }

    private static SKTypeface CreateSystemTypeface(FontInfo font, SKFontStyle style)
    {
        return new SKTypeface(
            CreateFont(font),
            font.FamilyName,
            style.Weight >= (int)SKFontStyleWeight.SemiBold,
            style.Slant != SKFontStyleSlant.Upright,
            style.Weight,
            style.Width,
            style.Slant);
    }

    public static SKTypeface FromFamilyName(
        string? familyName,
        SKFontStyleWeight weight,
        SKFontStyleWidth width,
        SKFontStyleSlant slant) =>
        FromFamilyName(familyName, (int)weight, (int)width, slant);

    public static SKTypeface FromFamilyName(
        string? familyName,
        int weight,
        int width,
        SKFontStyleSlant slant)
    {
        if (!string.IsNullOrWhiteSpace(familyName))
        {
            return FromFamilyName(familyName, new SKFontStyle(weight, width, slant));
        }

        var fallback = Default;
        return new SKTypeface(
            fallback.Font,
            fallback.FamilyName,
            weight >= (int)SKFontStyleWeight.SemiBold,
            slant != SKFontStyleSlant.Upright,
            weight,
            width,
            slant);
    }

    public static SKTypeface? FromData(SKData data, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        return FromStream(new MemoryStream(data.Bytes, writable: false), index);
    }

    private static bool MatchesStyle(FontInfo font, SKFontStyle style)
    {
        var name = font.Name;
        var wantsBold = style.Weight >= (int)SKFontStyleWeight.SemiBold;
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

    public bool TryGetTableData(uint tag, out byte[] data)
    {
        Span<char> characters = stackalloc char[4]
        {
            (char)((tag >> 24) & 0xff),
            (char)((tag >> 16) & 0xff),
            (char)((tag >> 8) & 0xff),
            (char)(tag & 0xff)
        };

        if (Font.TryGetTable(new string(characters), out var table))
        {
            data = table.ToArray();
            return true;
        }

        data = Array.Empty<byte>();
        return false;
    }

    public SKStreamAsset OpenStream()
    {
        return new SKStreamAsset(Font.FontData.ToArray());
    }

    public SKStreamAsset OpenStream(out int ttcIndex)
    {
        ttcIndex = Font.FaceIndex;
        return OpenStream();
    }

    public void Dispose() { }

    internal static TtfFont CreateFont(FontInfo font)
    {
        var key = (font.FilePath, font.FaceIndex);
        var lazy = s_systemFonts.GetOrAdd(
            key,
            static value => new Lazy<TtfFont>(
                () => new TtfFont(value.Path, value.FaceIndex),
                isThreadSafe: true));
        try
        {
            return lazy.Value;
        }
        catch
        {
            s_systemFonts.TryRemove(key, out _);
            throw;
        }
    }
}

public sealed class SKStreamAsset : SKStream
{
    private readonly byte[] _data;
    private readonly MemoryStream _stream;
    private System.Runtime.InteropServices.GCHandle _pin;

    internal SKStreamAsset(byte[] data)
    {
        _data = data;
        _stream = new MemoryStream(_data, writable: false);
    }

    internal byte[] Data => _data;

    public int Length => checked((int)_stream.Length);

    public int Read(byte[] buffer, int size)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return _stream.Read(buffer, 0, Math.Min(size, buffer.Length));
    }

    public int Read(byte[] buffer, long size)
    {
        return Read(buffer, (int)Math.Min(size, int.MaxValue));
    }

    public int Read(IntPtr buffer, int size)
    {
        if (buffer == IntPtr.Zero || size <= 0)
        {
            return 0;
        }

        var actualSize = Math.Min(size, Length - checked((int)_stream.Position));
        if (actualSize <= 0)
        {
            return 0;
        }

        var temporary = GC.AllocateUninitializedArray<byte>(actualSize);
        var read = _stream.Read(temporary, 0, actualSize);
        System.Runtime.InteropServices.Marshal.Copy(temporary, 0, buffer, read);
        return read;
    }

    public IntPtr GetMemoryBase()
    {
        if (_data.Length == 0)
        {
            return IntPtr.Zero;
        }

        if (!_pin.IsAllocated)
        {
            _pin = System.Runtime.InteropServices.GCHandle.Alloc(
                _data,
                System.Runtime.InteropServices.GCHandleType.Pinned);
        }

        return _pin.AddrOfPinnedObject();
    }

    public override void Dispose()
    {
        if (_pin.IsAllocated)
        {
            _pin.Free();
        }

        _stream.Dispose();
    }
}

public class SKFontManager : IDisposable
{
    public IntPtr Handle { get; } = SKObjectHandle.Create();
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

    public SKTypeface? CreateTypeface(string path, int index = 0) => SKTypeface.FromFile(path, index);

    public SKTypeface? CreateTypeface(Stream stream, int index = 0) => SKTypeface.FromStream(stream, index);

    public SKTypeface? CreateTypeface(SKStreamAsset stream, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return SKTypeface.FromStream(new MemoryStream(stream.Data, writable: false), index);
    }

    public SKTypeface? CreateTypeface(SKData data, int index = 0) => SKTypeface.FromData(data, index);

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
                        if (FontApi.ContainsGlyph(font, (uint)codepoint))
                        {
                            var ttf = SKTypeface.CreateFont(font);
                            bool isBold = style.Weight >= (int)SKFontStyleWeight.SemiBold;
                            bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                            return new SKTypeface(
                                ttf,
                                font.FamilyName,
                                isBold,
                                isItalic,
                                style.Weight,
                                style.Width,
                                style.Slant);
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
                if (FontApi.ContainsGlyph(font, (uint)codepoint))
                {
                    var ttf = SKTypeface.CreateFont(font);
                    bool isBold = style.Weight >= (int)SKFontStyleWeight.SemiBold;
                    bool isItalic = style.Slant != SKFontStyleSlant.Upright;
                    return new SKTypeface(
                        ttf,
                        font.FamilyName,
                        isBold,
                        isItalic,
                        style.Weight,
                        style.Width,
                        style.Slant);
                }
            }
            catch { }
        }

        return null;
    }

    public SKTypeface? MatchCharacter(int codepoint) =>
        MatchCharacter(null, SKFontStyle.Normal, Array.Empty<string>(), codepoint);

    public SKTypeface? MatchCharacter(
        string? familyName,
        SKFontStyleWeight weight,
        SKFontStyleWidth width,
        SKFontStyleSlant slant,
        string[]? bcp47,
        int codepoint) =>
        MatchCharacter(familyName, new SKFontStyle(weight, width, slant), bcp47 ?? Array.Empty<string>(), codepoint);

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
