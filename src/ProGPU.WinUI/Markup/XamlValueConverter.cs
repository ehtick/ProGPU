using System;
using System.Globalization;
using System.Numerics;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Markup;

/// <summary>
/// Applies the runtime value conversions required when a XAML value crosses an
/// untyped resource, setter, visual-state, or template-binding boundary.
/// </summary>
internal static class XamlValueConverter
{
    public static object? ConvertTo(Type targetType, object? value)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        if (value is null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        var conversionType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (conversionType.IsInstanceOfType(value))
            return value;

        if (conversionType.IsEnum && value is string enumText)
            return Enum.Parse(conversionType, enumText, ignoreCase: true);

        if (conversionType == typeof(bool) && value is string booleanText)
            return bool.Parse(booleanText);

        if (conversionType == typeof(Thickness))
        {
            if (value is float singleThickness)
                return new Thickness(singleThickness);
            if (value is double doubleThickness)
                return new Thickness((float)doubleThickness);
            if (value is int integerThickness)
                return new Thickness(integerThickness);
            if (value is string thicknessText)
                return Thickness.Parse(thicknessText);
        }

        if (conversionType == typeof(CornerRadius))
        {
            if (value is float singleRadius)
                return new CornerRadius(singleRadius);
            if (value is double doubleRadius)
                return new CornerRadius(doubleRadius);
            if (value is int integerRadius)
                return new CornerRadius(integerRadius);
            if (value is string radiusText)
                return ParseCornerRadius(radiusText);
        }

        if (conversionType == typeof(Windows.UI.Text.FontWeight) &&
            value is string fontWeightText &&
            Microsoft.UI.Text.FontWeights.TryParse(fontWeightText, out var fontWeight))
        {
            return fontWeight;
        }

        if (conversionType == typeof(Brush) && value is string brushText)
            return ParseBrush(brushText);

        if (conversionType == typeof(Vector4) && value is string colorText)
            return ParseColor(colorText);

        if (conversionType == typeof(float))
            return System.Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (conversionType == typeof(double))
            return System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (conversionType == typeof(int))
            return System.Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return System.Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
    }

    private static CornerRadius ParseCornerRadius(string text)
    {
        var parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => new CornerRadius(ParseDouble(parts[0])),
            4 => new CornerRadius(
                ParseDouble(parts[0]),
                ParseDouble(parts[1]),
                ParseDouble(parts[2]),
                ParseDouble(parts[3])),
            _ => throw new FormatException($"'{text}' is not a valid CornerRadius value.")
        };
    }

    private static Brush ParseBrush(string text)
    {
        if (text.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(new Vector4(0f, 0f, 0f, 0f));

        return new SolidColorBrush(ParseColor(text));
    }

    private static Vector4 ParseColor(string text)
    {
        if (!text.StartsWith("#", StringComparison.Ordinal))
            throw new FormatException($"'{text}' is not a supported color value.");

        var hex = text.Substring(1);
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8)
            throw new FormatException($"'{text}' is not a supported color value.");

        var argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector4(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f,
            ((argb >> 24) & 0xFF) / 255f);
    }

    private static double ParseDouble(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
