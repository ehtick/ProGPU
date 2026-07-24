using Windows.UI.Text;

namespace Microsoft.UI.Text;

/// <summary>Provides the standard OpenType font-weight values used by WinUI.</summary>
public static class FontWeights
{
    public static FontWeight Thin => Create(100);
    public static FontWeight ExtraLight => Create(200);
    public static FontWeight Light => Create(300);
    public static FontWeight SemiLight => Create(350);
    public static FontWeight Normal => Create(400);
    public static FontWeight Medium => Create(500);
    public static FontWeight SemiBold => Create(600);
    public static FontWeight Bold => Create(700);
    public static FontWeight ExtraBold => Create(800);
    public static FontWeight Black => Create(900);
    public static FontWeight ExtraBlack => Create(950);

    internal static bool TryParse(string text, out FontWeight value)
    {
        if (string.Equals(text, nameof(Thin), System.StringComparison.OrdinalIgnoreCase))
            value = Thin;
        else if (string.Equals(text, nameof(ExtraLight), System.StringComparison.OrdinalIgnoreCase))
            value = ExtraLight;
        else if (string.Equals(text, nameof(Light), System.StringComparison.OrdinalIgnoreCase))
            value = Light;
        else if (string.Equals(text, nameof(SemiLight), System.StringComparison.OrdinalIgnoreCase))
            value = SemiLight;
        else if (string.Equals(text, nameof(Normal), System.StringComparison.OrdinalIgnoreCase))
            value = Normal;
        else if (string.Equals(text, nameof(Medium), System.StringComparison.OrdinalIgnoreCase))
            value = Medium;
        else if (string.Equals(text, nameof(SemiBold), System.StringComparison.OrdinalIgnoreCase))
            value = SemiBold;
        else if (string.Equals(text, nameof(Bold), System.StringComparison.OrdinalIgnoreCase))
            value = Bold;
        else if (string.Equals(text, nameof(ExtraBold), System.StringComparison.OrdinalIgnoreCase))
            value = ExtraBold;
        else if (string.Equals(text, nameof(Black), System.StringComparison.OrdinalIgnoreCase))
            value = Black;
        else if (string.Equals(text, nameof(ExtraBlack), System.StringComparison.OrdinalIgnoreCase))
            value = ExtraBlack;
        else if (ushort.TryParse(
                     text,
                     System.Globalization.NumberStyles.None,
                     System.Globalization.CultureInfo.InvariantCulture,
                     out var numeric))
            value = Create(numeric);
        else
        {
            value = default;
            return false;
        }

        return true;
    }

    private static FontWeight Create(ushort value) => new() { Weight = value };
}
