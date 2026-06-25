using System;
using System.Collections.Generic;
using System.IO;

namespace ProGPU.Text;

public class FontInfo
{
    public string Name { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int FaceIndex { get; set; }

    public override string ToString()
    {
        return $"{Name} ({FamilyName})";
    }
}

public static class FontApi
{
    private static readonly object s_cachedSystemFontsLock = new();
    private static List<FontInfo>? s_cachedSystemFonts;

    public static List<FontInfo> GetSystemFonts()
    {
        var list = new List<FontInfo>();

        foreach (var dir in GetSystemFontDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".ttc" || ext == ".otf")
                    {
                        list.AddRange(ParseFontInfos(file));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                continue;
            }
        }

        // Deduplicate and sort
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueList = new List<FontInfo>();
        foreach (var font in list)
        {
            var key = $"{font.Name}|{font.FilePath}|{font.FaceIndex}";
            if (!seen.Contains(key))
            {
                seen.Add(key);
                uniqueList.Add(font);
            }
        }

        uniqueList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return uniqueList;
    }

    public static FontInfo? FindSystemFont(params string[] familyOrFullNames)
    {
        if (familyOrFullNames.Length == 0)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in familyOrFullNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        foreach (var font in GetCachedSystemFonts())
        {
            if (names.Contains(font.FamilyName) || names.Contains(font.Name))
            {
                return font;
            }
        }

        return null;
    }

    private static IReadOnlyList<FontInfo> GetCachedSystemFonts()
    {
        lock (s_cachedSystemFontsLock)
        {
            s_cachedSystemFonts ??= GetSystemFonts();
            return s_cachedSystemFonts;
        }
    }

    public static FontInfo? ParseFontInfo(string file)
    {
        var infos = ParseFontInfos(file);
        return infos.Count > 0 ? infos[0] : null;
    }

    public static List<FontInfo> ParseFontInfos(string file)
    {
        try
        {
            var infos = new List<FontInfo>();
            if (!SfntFontFace.TryLoadFaces(file, out IReadOnlyList<SfntFontFace> faces))
            {
                return new List<FontInfo> { CreateFallbackInfo(file, 0) };
            }

            foreach (SfntFontFace face in faces)
            {
                string familyName = TryGetFirstName(face, SfntNameIds.PreferredFamilyName) ??
                                    TryGetFirstName(face, SfntNameIds.FamilyName) ??
                                    Path.GetFileNameWithoutExtension(file);
                string fullName = TryGetFirstName(face, SfntNameIds.FullName) ?? familyName;

                infos.Add(new FontInfo
                {
                    Name = fullName,
                    FamilyName = familyName,
                    FilePath = file,
                    FaceIndex = face.FaceIndex
                });
            }

            return infos;
        }
        catch
        {
            return new List<FontInfo> { CreateFallbackInfo(file, 0) };
        }
    }

    public static IEnumerable<string> GetSystemFontDirectories()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/System/Library/Fonts/Supplemental";
            yield return "/Library/Fonts";
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Fonts");
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "fonts");
        }
    }

    private static string? TryGetFirstName(SfntFontFace face, ushort nameId)
    {
        return face.TryGetName(nameId, out string value) ? value : null;
    }

    private static FontInfo CreateFallbackInfo(string file, int faceIndex)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return new FontInfo
        {
            Name = name,
            FamilyName = name,
            FilePath = file,
            FaceIndex = faceIndex
        };
    }
}
