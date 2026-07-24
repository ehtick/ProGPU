using System;

namespace Microsoft.UI.Xaml.Media;

/// <summary>Represents a WinUI font-family source and fallback list.</summary>
public class FontFamily : IEquatable<FontFamily>
{
    public FontFamily(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            throw new ArgumentException("A font family source is required.", nameof(familyName));
        }

        Source = familyName;
    }

    public string Source { get; }

    public static FontFamily XamlAutoFontFamily { get; } = new("XamlAutoFontFamily");

    public bool Equals(FontFamily? other) => other is not null &&
        string.Equals(Source, other.Source, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as FontFamily);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Source);
    public override string ToString() => Source;
}
