using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Vector;
using ProGPU.Layout;

namespace Microsoft.UI.Xaml;

public enum ElementTheme
{
    Default,
    Light,
    Dark
}

public static class ThemeManager
{
    private static ElementTheme _currentTheme = ElementTheme.Dark;
    public static event Action? ThemeChanged;
    private static readonly Dictionary<Type, Style> NativeDefaultStyles = new();
    private static readonly Dictionary<string, SolidColorBrush> DarkBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SolidColorBrush> LightBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Key, float Thickness, ElementTheme Theme), Pen> PenCache = new();

    public static ElementTheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ThemeChanged?.Invoke();
            }
        }
    }

    private static readonly Dictionary<string, Vector4> DarkPalette = new()
    {
        { "PageBackground", new Vector4(0.08f, 0.08f, 0.12f, 1.0f) }, // Dark Mica: #14141F
        { "CardBackground", new Vector4(0.12f, 0.12f, 0.16f, 1.0f) }, // #1F1F28
        { "ControlBackground", new Vector4(1f, 1f, 1f, 0.05f) }, // White 5%
        { "ControlBackgroundHover", new Vector4(1f, 1f, 1f, 0.09f) }, // White 9%
        { "ControlBackgroundPressed", new Vector4(0f, 0f, 0f, 0.15f) }, // Black 15%
        { "ControlBorder", new Vector4(1f, 1f, 1f, 0.08f) }, // White 8%
        { "ControlBorderHover", new Vector4(1f, 1f, 1f, 0.15f) }, // White 15%
        { "TextPrimary", new Vector4(1f, 1f, 1f, 1.0f) }, // Solid White
        { "TextSecondary", new Vector4(1f, 1f, 1f, 0.6f) }, // Muted White
        { "SystemAccentColor", new Vector4(0.0f, 0.47f, 0.83f, 1.0f) }, // Segoe Blue: #0078D4
        { "SystemAccentColorLight1", new Vector4(0.17f, 0.53f, 0.85f, 1.0f) }, // Hover
        { "SystemAccentColorDark1", new Vector4(0.0f, 0.35f, 0.62f, 1.0f) }, // Pressed
        { "SelectionHighlight", new Vector4(0.0f, 0.47f, 0.83f, 0.25f) }, // Translucent Segoe Blue
        { "HeaderBackground", new Vector4(0.05f, 0.05f, 0.07f, 1.0f) }, // Deep Dark
        { "ScrollbarThumb", new Vector4(1f, 1f, 1f, 0.25f) }, // White 25%
        { "ScrollbarThumbHover", new Vector4(1f, 1f, 1f, 0.45f) }, // White 45%
        { "ButtonAmbientShadow", new Vector4(0f, 0f, 0f, 0.04f) },
        { "ButtonPenumbraShadow", new Vector4(0f, 0f, 0f, 0.08f) },
        { "NavigationViewItemBackgroundSelected", new Vector4(1f, 1f, 1f, 0.07f) },
        { "NavigationViewItemBackgroundPointerOver", new Vector4(1f, 1f, 1f, 0.05f) },
        { "TabViewItemCloseHover", new Vector4(1.0f, 0.33f, 0.33f, 1.0f) },
        { "TextOnAccent", new Vector4(1f, 1f, 1f, 1.0f) }
    };

    private static readonly Dictionary<string, Vector4> LightPalette = new()
    {
        { "PageBackground", new Vector4(0.96f, 0.96f, 0.98f, 1.0f) }, // Light Acrylic: #F5F5F7
        { "CardBackground", new Vector4(1.0f, 1.0f, 1.0f, 1.0f) }, // Solid White
        { "ControlBackground", new Vector4(0f, 0f, 0f, 0.04f) }, // Black 4%
        { "ControlBackgroundHover", new Vector4(0f, 0f, 0f, 0.07f) }, // Black 7%
        { "ControlBackgroundPressed", new Vector4(0f, 0f, 0f, 0.12f) }, // Black 12%
        { "ControlBorder", new Vector4(0f, 0f, 0f, 0.09f) }, // Black 9%
        { "ControlBorderHover", new Vector4(0f, 0f, 0f, 0.18f) }, // Black 18%
        { "TextPrimary", new Vector4(0.08f, 0.08f, 0.12f, 1.0f) }, // Solid Dark
        { "TextSecondary", new Vector4(0.08f, 0.08f, 0.12f, 0.6f) }, // Muted Dark
        { "SystemAccentColor", new Vector4(0.0f, 0.47f, 0.83f, 1.0f) }, // Segoe Blue: #0078D4
        { "SystemAccentColorLight1", new Vector4(0.17f, 0.53f, 0.85f, 1.0f) }, // Hover
        { "SystemAccentColorDark1", new Vector4(0.0f, 0.35f, 0.62f, 1.0f) }, // Pressed
        { "SelectionHighlight", new Vector4(0.0f, 0.47f, 0.83f, 0.25f) }, // Translucent Segoe Blue
        { "HeaderBackground", new Vector4(0.92f, 0.92f, 0.94f, 1.0f) }, // Lighter header
        { "ScrollbarThumb", new Vector4(0f, 0f, 0f, 0.18f) }, // Black 18%
        { "ScrollbarThumbHover", new Vector4(0f, 0f, 0f, 0.35f) }, // Black 35%
        { "ButtonAmbientShadow", new Vector4(0f, 0f, 0f, 0.04f) },
        { "ButtonPenumbraShadow", new Vector4(0f, 0f, 0f, 0.08f) },
        { "NavigationViewItemBackgroundSelected", new Vector4(0f, 0f, 0f, 0.08f) },
        { "NavigationViewItemBackgroundPointerOver", new Vector4(0f, 0f, 0f, 0.05f) },
        { "TabViewItemCloseHover", new Vector4(1.0f, 0.33f, 0.33f, 1.0f) },
        { "TextOnAccent", new Vector4(1f, 1f, 1f, 1.0f) }
    };

    private static readonly Dictionary<string, string> ResourceAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ButtonBackground", "ControlBackground" },
        { "ButtonBackgroundPointerOver", "ControlBackgroundHover" },
        { "ButtonBackgroundPressed", "ControlBackgroundPressed" },
        { "ButtonBackgroundFocused", "ControlBackground" },
        { "ButtonBackgroundDisabled", "ControlBackground" },
        { "ButtonForeground", "TextPrimary" },
        { "ButtonForegroundPointerOver", "TextPrimary" },
        { "ButtonForegroundPressed", "TextPrimary" },
        { "ButtonForegroundDisabled", "TextSecondary" },
        { "ButtonBorderBrush", "ControlBorder" },
        { "ButtonBorderBrushPointerOver", "ControlBorderHover" },
        { "ButtonBorderBrushPressed", "ControlBorder" },
        { "ButtonBorderBrushFocused", "ControlBorderHover" },
        { "ButtonBorderBrushDisabled", "ControlBorder" },

        { "AccentButtonBackground", "SystemAccentColor" },
        { "AccentButtonBackgroundPointerOver", "SystemAccentColorLight1" },
        { "AccentButtonBackgroundPressed", "SystemAccentColorDark1" },
        { "AccentButtonBackgroundFocused", "SystemAccentColor" },
        { "AccentButtonBackgroundDisabled", "ControlBackground" },
        { "AccentButtonForeground", "TextOnAccent" },
        { "AccentButtonForegroundPointerOver", "TextOnAccent" },
        { "AccentButtonForegroundPressed", "TextOnAccent" },
        { "AccentButtonForegroundDisabled", "TextSecondary" },
        { "AccentButtonBorderBrush", "SystemAccentColor" },
        { "AccentButtonBorderBrushPointerOver", "SystemAccentColorLight1" },
        { "AccentButtonBorderBrushPressed", "SystemAccentColorDark1" },
        { "AccentButtonBorderBrushFocused", "SystemAccentColor" },
        { "AccentButtonBorderBrushDisabled", "ControlBorder" },

        { "RepeatButtonBackground", "ControlBackground" },
        { "RepeatButtonBackgroundFocused", "ControlBackground" },
        { "RepeatButtonForeground", "TextPrimary" },
        { "RepeatButtonBorderBrush", "ControlBorder" },
        { "RepeatButtonBorderBrushFocused", "ControlBorderHover" },
        { "HyperlinkButtonBackground", "ControlBackground" },
        { "HyperlinkButtonBackgroundFocused", "ControlBackground" },
        { "HyperlinkButtonForeground", "SystemAccentColor" },
        { "HyperlinkButtonBorderBrush", "SystemAccentColor" },
        { "HyperlinkButtonBorderBrushFocused", "SystemAccentColor" },

        { "CheckBoxBackgroundUnchecked", "ControlBackground" },
        { "CheckBoxBackgroundUncheckedPointerOver", "ControlBackgroundHover" },
        { "CheckBoxBackgroundUncheckedPressed", "ControlBackgroundPressed" },
        { "CheckBoxForegroundUnchecked", "TextPrimary" },
        { "CheckBoxBorderBrushUnchecked", "ControlBorder" },
        { "CheckBoxBorderBrushUncheckedPointerOver", "ControlBorderHover" },
        { "CheckBoxCheckBackgroundFillChecked", "SystemAccentColor" },
        { "CheckBoxCheckBackgroundFillCheckedPointerOver", "SystemAccentColorLight1" },
        { "CheckBoxCheckBackgroundFillCheckedPressed", "SystemAccentColorDark1" },
        { "CheckBoxCheckGlyphForegroundChecked", "TextOnAccent" },

        { "CheckBoxBackground", "ControlBackground" },
        { "CheckBoxBackgroundPointerOver", "ControlBackgroundHover" },
        { "CheckBoxBackgroundPressed", "ControlBackgroundPressed" },
        { "CheckBoxBackgroundFocused", "ControlBackground" },
        { "CheckBoxBackgroundDisabled", "ControlBackground" },
        { "CheckBoxForeground", "TextPrimary" },
        { "CheckBoxForegroundPointerOver", "TextPrimary" },
        { "CheckBoxForegroundPressed", "TextPrimary" },
        { "CheckBoxForegroundDisabled", "TextSecondary" },
        { "CheckBoxBorderBrush", "ControlBorder" },
        { "CheckBoxBorderBrushPointerOver", "ControlBorderHover" },
        { "CheckBoxBorderBrushPressed", "ControlBorder" },
        { "CheckBoxBorderBrushFocused", "ControlBorderHover" },
        { "CheckBoxBorderBrushDisabled", "ControlBorder" },

        { "RadioButtonBackground", "ControlBackground" },
        { "RadioButtonBackgroundPointerOver", "ControlBackgroundHover" },
        { "RadioButtonBackgroundPressed", "ControlBackgroundPressed" },
        { "RadioButtonBackgroundFocused", "ControlBackground" },
        { "RadioButtonBackgroundDisabled", "ControlBackground" },
        { "RadioButtonForeground", "TextPrimary" },
        { "RadioButtonForegroundPointerOver", "TextPrimary" },
        { "RadioButtonForegroundPressed", "TextPrimary" },
        { "RadioButtonForegroundDisabled", "TextSecondary" },
        { "RadioButtonBorderBrush", "ControlBorder" },
        { "RadioButtonBorderBrushPointerOver", "ControlBorderHover" },
        { "RadioButtonBorderBrushPressed", "ControlBorder" },
        { "RadioButtonBorderBrushFocused", "ControlBorderHover" },
        { "RadioButtonBorderBrushDisabled", "ControlBorder" },
        { "RadioButtonCheckBackgroundFillChecked", "SystemAccentColor" },
        { "RadioButtonCheckBackgroundFillCheckedPointerOver", "SystemAccentColorLight1" },
        { "RadioButtonCheckBackgroundFillCheckedPressed", "SystemAccentColorDark1" },
        { "RadioButtonCheckGlyphForegroundChecked", "TextOnAccent" },

        { "ComboBoxBackground", "ControlBackground" },
        { "ComboBoxBackgroundPointerOver", "ControlBackgroundHover" },
        { "ComboBoxBackgroundPressed", "ControlBackgroundPressed" },
        { "ComboBoxBackgroundDisabled", "ControlBackground" },
        { "ComboBoxForeground", "TextPrimary" },
        { "ComboBoxForegroundPointerOver", "TextPrimary" },
        { "ComboBoxForegroundPressed", "TextPrimary" },
        { "ComboBoxForegroundDisabled", "TextSecondary" },
        { "ComboBoxBorderBrush", "ControlBorder" },
        { "ComboBoxBorderBrushPointerOver", "ControlBorderHover" },
        { "ComboBoxBorderBrushPressed", "ControlBorder" },
        { "ComboBoxBorderBrushDisabled", "ControlBorder" },
        { "ComboBoxItemBackgroundSelected", "SelectionHighlight" },
        { "ComboBoxItemBackgroundPointerOver", "ControlBackgroundHover" },
        { "ComboBoxItemForeground", "TextPrimary" },

        { "TextControlBackground", "ControlBackground" },
        { "TextControlBackgroundPointerOver", "ControlBackgroundHover" },
        { "TextControlBackgroundFocused", "CardBackground" },
        { "TextControlForeground", "TextPrimary" },
        { "TextControlForegroundPointerOver", "TextPrimary" },
        { "TextControlBorderBrush", "ControlBorder" },
        { "TextControlBorderBrushPointerOver", "ControlBorderHover" },
        { "TextControlBorderBrushFocused", "SystemAccentColor" },
        { "TextControlPlaceholderForeground", "TextSecondary" },

        { "TextBoxBackground", "TextControlBackground" },
        { "TextBoxBackgroundPointerOver", "TextControlBackgroundPointerOver" },
        { "TextBoxBackgroundPressed", "TextControlBackground" },
        { "TextBoxBackgroundFocused", "TextControlBackgroundFocused" },
        { "TextBoxBackgroundDisabled", "TextControlBackground" },
        { "TextBoxForeground", "TextControlForeground" },
        { "TextBoxForegroundPointerOver", "TextControlForegroundPointerOver" },
        { "TextBoxForegroundFocused", "TextControlForeground" },
        { "TextBoxForegroundDisabled", "TextControlPlaceholderForeground" },
        { "TextBoxBorderBrush", "TextControlBorderBrush" },
        { "TextBoxBorderBrushPointerOver", "TextControlBorderBrushPointerOver" },
        { "TextBoxBorderBrushFocused", "TextControlBorderBrushFocused" },
        { "TextBoxBorderBrushDisabled", "TextControlBorderBrush" },

        { "PasswordBoxBackground", "TextControlBackground" },
        { "PasswordBoxBackgroundPointerOver", "TextControlBackgroundPointerOver" },
        { "PasswordBoxBackgroundPressed", "TextControlBackground" },
        { "PasswordBoxBackgroundFocused", "TextControlBackgroundFocused" },
        { "PasswordBoxBackgroundDisabled", "TextControlBackground" },
        { "PasswordBoxForeground", "TextControlForeground" },
        { "PasswordBoxForegroundPointerOver", "TextControlForegroundPointerOver" },
        { "PasswordBoxForegroundFocused", "TextControlForeground" },
        { "PasswordBoxForegroundDisabled", "TextControlPlaceholderForeground" },
        { "PasswordBoxBorderBrush", "TextControlBorderBrush" },
        { "PasswordBoxBorderBrushPointerOver", "TextControlBorderBrushPointerOver" },
        { "PasswordBoxBorderBrushFocused", "TextControlBorderBrushFocused" },
        { "PasswordBoxBorderBrushDisabled", "TextControlBorderBrush" },

        { "SliderTrackFill", "ControlBorder" },
        { "SliderTrackValueFill", "SystemAccentColor" },
        { "SliderThumbBackground", "TextPrimary" },
        { "SliderThumbBorderBrush", "ControlBorder" },
        { "SliderTrackFillPointerOver", "ControlBorderHover" },
        { "SliderTrackValueFillPointerOver", "SystemAccentColorLight1" },
        { "SliderTrackValueFillPressed", "SystemAccentColorDark1" },
        { "SliderTrackFillDisabled", "ControlBackground" },
        { "SliderTrackValueFillDisabled", "ControlBackground" },

        { "ToggleSwitchContentForeground", "TextPrimary" },
        { "ToggleSwitchHeaderForeground", "TextPrimary" },
        { "ToggleSwitchContainerBackground", "ControlBackground" },
        { "ToggleSwitchContainerBackgroundPointerOver", "ControlBackgroundHover" },
        { "ToggleSwitchFillOn", "SystemAccentColor" },
        { "ToggleSwitchFillOnPointerOver", "SystemAccentColorLight1" },
        { "ToggleSwitchFillOnPressed", "SystemAccentColorDark1" },
        { "ToggleSwitchKnobFillOff", "TextSecondary" },
        { "ToggleSwitchKnobFillOn", "TextPrimary" },

        { "GridSplitterBackground", "ControlBorder" },
        { "GridSplitterBackgroundPointerOver", "ControlBorderHover" },
        { "GridSplitterBackgroundPressed", "ControlBorderHover" },
        { "GridSplitterBackgroundFocused", "ControlBorder" },
        { "GridSplitterBackgroundDisabled", "ControlBorder" },
        { "GridSplitterForeground", "TextPrimary" },
        { "GridSplitterBorderBrush", "ControlBorder" },

        { "ProgressBarBackground", "ControlBorder" },
        { "ProgressBarForeground", "SystemAccentColor" },
        { "ProgressRingForeground", "SystemAccentColor" },
        { "ComboBoxBackgroundFocused", "ControlBackground" },
        { "ComboBoxBorderBrushFocused", "SystemAccentColor" },
        { "DatePickerBackground", "TextControlBackground" },
        { "DatePickerBackgroundPointerOver", "TextControlBackgroundPointerOver" },
        { "DatePickerBackgroundPressed", "TextControlBackground" },
        { "DatePickerBackgroundFocused", "TextControlBackgroundFocused" },
        { "DatePickerBackgroundDisabled", "TextControlBackground" },
        { "DatePickerForeground", "TextControlForeground" },
        { "DatePickerForegroundPointerOver", "TextControlForegroundPointerOver" },
        { "DatePickerForegroundPressed", "TextControlForeground" },
        { "DatePickerForegroundDisabled", "TextControlPlaceholderForeground" },
        { "DatePickerBorderBrush", "TextControlBorderBrush" },
        { "DatePickerBorderBrushPointerOver", "TextControlBorderBrushPointerOver" },
        { "DatePickerBorderBrushFocused", "TextControlBorderBrushFocused" },
        { "DatePickerBorderBrushDisabled", "TextControlBorderBrush" },
        { "ToolTipBackground", "CardBackground" },
        { "ToolTipForeground", "TextPrimary" },
        { "ToolTipBorderBrush", "ControlBorder" },
        { "ContentDialogBackground", "CardBackground" },
        { "ContentDialogForeground", "TextPrimary" },
        { "SystemControlBackgroundAccentBrush", "SystemAccentColor" },
        { "SystemControlForegroundBaseHighBrush", "TextPrimary" },
        { "SystemControlForegroundBaseMediumBrush", "TextSecondary" },
        { "SystemControlBackgroundBaseLowBrush", "ControlBackground" },
        { "SystemControlHighlightAccentBrush", "SystemAccentColor" }
    };

    private static string ResolveAlias(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        int depth = 0;
        while (ResourceAliases.TryGetValue(key, out var alias) && depth < 20)
        {
            key = alias;
            depth++;
        }
        return key;
    }

    private static object? ResolveValue(object? value)
    {
        if (value is StaticResourceRef r)
        {
            return GetResource(r.ResourceKey);
        }
        return value;
    }

    public static object? GetResource(string key) => GetResource(key, CurrentTheme);

    public static object? GetResource(string key, ElementTheme theme)
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (key.Equals("AccentButtonStyle", StringComparison.OrdinalIgnoreCase))
        {
            var accentStyle = new Style(typeof(Button));
            AddControlChrome(accentStyle, "SystemAccentColor", "TextOnAccent", "SystemAccentColor", new Thickness(1f), 6f, new Thickness(12f, 6f, 12f, 6f));
            return accentStyle;
        }

        key = ResolveAlias(key);

        var actualTheme = theme == ElementTheme.Default ? CurrentTheme : theme;
        var dict = (actualTheme == ElementTheme.Light) ? LightPalette : DarkPalette;
        if (dict.TryGetValue(key, out var colorVal))
        {
            var cache = (actualTheme == ElementTheme.Light) ? LightBrushCache : DarkBrushCache;
            if (cache.TryGetValue(key, out var cachedBrush))
            {
                return cachedBrush;
            }
            var newBrush = new SolidColorBrush(colorVal);
            cache[key] = newBrush;
            return newBrush;
        }

        return null;
    }

    public static Brush GetBrush(string key) => GetBrush(key, CurrentTheme);

    public static Brush GetBrush(string key, ElementTheme theme)
    {
        var actualTheme = theme == ElementTheme.Default ? CurrentTheme : theme;
        var cache = (actualTheme == ElementTheme.Light) ? LightBrushCache : DarkBrushCache;

        key = ResolveAlias(key);

        if (cache.TryGetValue(key, out var cachedBrush))
        {
            return cachedBrush;
        }

        var colorFallback = GetColor(key, actualTheme);
        var newBrush = new SolidColorBrush(colorFallback);
        cache[key] = newBrush;
        return newBrush;
    }

    public static Pen GetPen(string key, float thickness = 1.0f) => GetPen(key, thickness, CurrentTheme);

    public static Pen GetPen(string key, float thickness, ElementTheme theme)
    {
        var actualTheme = theme == ElementTheme.Default ? CurrentTheme : theme;
        key = ResolveAlias(key);

        var cacheKey = (key, thickness, actualTheme);
        if (PenCache.TryGetValue(cacheKey, out var cachedPen))
        {
            return cachedPen;
        }

        var brush = GetBrush(key, actualTheme);
        var newPen = new Pen(brush, thickness);
        PenCache[cacheKey] = newPen;
        return newPen;
    }

    public static Vector4 GetColor(string key) => GetColor(key, CurrentTheme);

    public static Vector4 GetColor(string key, ElementTheme theme)
    {
        key = ResolveAlias(key);

        var actualTheme = theme == ElementTheme.Default ? CurrentTheme : theme;
        var dict = (actualTheme == ElementTheme.Light) ? LightPalette : DarkPalette;
        if (dict.TryGetValue(key, out var valHex))
        {
            return valHex;
        }
        return new Vector4(1f, 1f, 1f, 1f); // Default White
    }


    public static Style? GetDefaultStyle(Type controlType)
    {
        if (NativeDefaultStyles.TryGetValue(controlType, out var cached))
        {
            return cached;
        }

        var style = GetGeneratedDefaultStyle(controlType);
        if (style != null)
        {
            NativeDefaultStyles[controlType] = style;
            return style;
        }

        style = CreateNativeDefaultStyle(controlType);
        if (style != null)
        {
            NativeDefaultStyles[controlType] = style;
            return style;
        }

        return null;
    }

    private static Style? GetGeneratedDefaultStyle(Type controlType)
    {
        return null;
    }

    private static Style? CreateNativeDefaultStyle(Type controlType)
    {
        if (!typeof(Control).IsAssignableFrom(controlType))
        {
            return null;
        }

        var style = new Style(controlType);
        AddControlChrome(style, "ControlBackground", "TextPrimary", "ControlBorder", new Thickness(1f), 4f, new Thickness(8f, 4f));

        if (typeof(HyperlinkButton).IsAssignableFrom(controlType))
        {
            style.Setters.Add(new Setter(nameof(Control.Background), TransparentBrush()));
            style.Setters.Add(new Setter(nameof(Control.Foreground), new ThemeResource("SystemAccentColor")));
            style.Setters.Add(new Setter(nameof(Control.BorderBrush), TransparentBrush()));
            style.Setters.Add(new Setter(nameof(Control.BorderThickness), new Thickness(0f)));
            style.Setters.Add(new Setter(nameof(Control.Padding), new Thickness(4f, 2f)));
            return style;
        }

        if (typeof(Button).IsAssignableFrom(controlType) || typeof(RepeatButton).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "ButtonBackground", "ButtonForeground", "ButtonBorderBrush", new Thickness(1f), 6f, new Thickness(12f, 6f, 12f, 6f));
            return style;
        }

        if (typeof(CheckBox).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "CheckBoxBackground", "CheckBoxForeground", "CheckBoxBorderBrush", new Thickness(1f), 4f, new Thickness(8f, 4f, 8f, 4f));
            return style;
        }

        if (typeof(RadioButton).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "RadioButtonBackground", "RadioButtonForeground", "RadioButtonBorderBrush", new Thickness(1f), 9f, new Thickness(8f, 4f, 8f, 4f));
            return style;
        }

        if (typeof(Slider).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "SliderTrackFill", "SliderTrackValueFill", "SliderThumbBorderBrush", new Thickness(1f), 8f, new Thickness(0f));
            return style;
        }

        if (typeof(ToggleSwitch).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "ToggleSwitchContainerBackground", "ToggleSwitchContentForeground", "ControlBorder", new Thickness(1f), 10f, new Thickness(6f, 4f, 6f, 4f));
            return style;
        }

        if (typeof(ProgressBar).IsAssignableFrom(controlType))
        {
            style.Setters.Add(new Setter(nameof(Control.Background), new ThemeResource("ProgressBarBackground")));
            style.Setters.Add(new Setter(nameof(Control.BorderBrush), new ThemeResource("ProgressBarForeground")));
            style.Setters.Add(new Setter(nameof(Control.BorderThickness), new Thickness(0f)));
            style.Setters.Add(new Setter(nameof(Control.CornerRadius), 2f));
            return style;
        }

        if (typeof(ProgressRing).IsAssignableFrom(controlType))
        {
            style.Setters.Add(new Setter(nameof(Control.Foreground), new ThemeResource("ProgressRingForeground")));
            style.Setters.Add(new Setter(nameof(Control.BorderBrush), new ThemeResource("ProgressRingForeground")));
            style.Setters.Add(new Setter(nameof(Control.BorderThickness), new Thickness(0f)));
            return style;
        }

        if (typeof(ComboBox).IsAssignableFrom(controlType) || typeof(DatePicker).IsAssignableFrom(controlType) || typeof(TextBox).IsAssignableFrom(controlType) || typeof(RichEditBox).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "TextBoxBackground", "TextBoxForeground", "TextBoxBorderBrush", new Thickness(1f), 4f, new Thickness(10f, 6f));
            return style;
        }

        if (typeof(PasswordBox).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "PasswordBoxBackground", "PasswordBoxForeground", "PasswordBoxBorderBrush", new Thickness(1f), 4f, new Thickness(10f, 6f, 36f, 6f));
            return style;
        }

        if (typeof(RatingControl).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "Transparent", "TextPrimary", "Transparent", new Thickness(0f), 0f, new Thickness(4f));
            return style;
        }

        if (typeof(ComboBoxItem).IsAssignableFrom(controlType) || typeof(TreeViewItem).IsAssignableFrom(controlType) || typeof(NavigationViewItem).IsAssignableFrom(controlType) || typeof(TabViewItem).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "ControlBackground", "TextPrimary", "ControlBorder", new Thickness(0f), 4f, new Thickness(8f, 4f));
            return style;
        }

        if (typeof(ContentDialog).IsAssignableFrom(controlType) || typeof(ToolTip).IsAssignableFrom(controlType) || typeof(DataGrid).IsAssignableFrom(controlType) || typeof(TreeView).IsAssignableFrom(controlType) || typeof(CalendarView).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "CardBackground", "TextPrimary", "ControlBorder", new Thickness(1f), 6f, new Thickness(12f));
            return style;
        }

        if (typeof(ScrollViewer).IsAssignableFrom(controlType) || typeof(ThemeToggleIconControl).IsAssignableFrom(controlType))
        {
            AddControlChrome(style, "ControlBackground", "TextPrimary", "ControlBorder", new Thickness(1f), 4f, new Thickness(6f));
            return style;
        }

        return style;
    }

    private static void AddControlChrome(Style style, string backgroundKey, string foregroundKey, string borderKey, Thickness borderThickness, float cornerRadius, Thickness padding)
    {
        style.Setters.Add(new Setter(nameof(Control.Background), new ThemeResource(backgroundKey)));
        style.Setters.Add(new Setter(nameof(Control.Foreground), new ThemeResource(foregroundKey)));
        style.Setters.Add(new Setter(nameof(Control.BorderBrush), new ThemeResource(borderKey)));
        style.Setters.Add(new Setter(nameof(Control.BorderThickness), borderThickness));
        style.Setters.Add(new Setter(nameof(Control.CornerRadius), cornerRadius));
        style.Setters.Add(new Setter(nameof(Control.Padding), padding));
    }

    private static Brush TransparentBrush()
    {
        return new SolidColorBrush(new Vector4(0f, 0f, 0f, 0f));
    }
}

public class StaticResourceRef
{
    public string ResourceKey { get; }
    public StaticResourceRef(string resourceKey)
    {
        ResourceKey = resourceKey;
    }
}
