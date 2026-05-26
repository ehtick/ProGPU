using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml;

public class RoutedEventArgs : EventArgs
{
    public bool Handled { get; set; }
    public object? OriginalSource { get; set; }
}

public class KeyRoutedEventArgs : RoutedEventArgs
{
    public Key Key { get; set; }
}

public class CharacterReceivedRoutedEventArgs : RoutedEventArgs
{
    public char Character { get; set; }
}

public class PointerRoutedEventArgs : RoutedEventArgs
{
    public Vector2 Position { get; set; }       // Position relative to the element
    public Vector2 ScreenPosition { get; set; } // Position relative to the screen
    public bool IsLeftButtonPressed { get; set; }
    public float WheelDelta { get; set; }
}

public partial class FrameworkElement
{
    

    public static readonly Microsoft.UI.Xaml.DependencyProperty FontProperty =
        Microsoft.UI.Xaml.DependencyProperty.Register(
            "Font",
            typeof(ProGPU.Text.TtfFont),
            typeof(FrameworkElement),
            new Microsoft.UI.Xaml.PropertyMetadata(null, null, isInheritable: true));

    public ProGPU.Text.TtfFont? Font
    {
        get => GetValue(FontProperty) as ProGPU.Text.TtfFont;
        set => SetValue(FontProperty, value);
    }

    protected override void RaisePropertyChanged(string propertyName)
    {
        base.RaisePropertyChanged(propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (propertyName != null)
        {
            RaisePropertyChanged(propertyName);
        }
    }

    public IList<KeyboardAccelerator> KeyboardAccelerators { get; } = new List<KeyboardAccelerator>();

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    private object? _tag;
    public object? Tag
    {
        get => _tag;
        set { if (_tag != value) { _tag = value; OnPropertyChanged(); } }
    }

    private bool _isHitTestVisible = true;
    public bool IsHitTestVisible
    {
        get => _isHitTestVisible;
        set { if (_isHitTestVisible != value) { _isHitTestVisible = value; OnPropertyChanged(); } }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    private object? _toolTip;
    public object? ToolTip
    {
        get => _toolTip;
        set { if (_toolTip != value) { _toolTip = value; OnPropertyChanged(); } }
    }

    private Style? _style;
    public Style? Style
    {
        get => _style;
        set
        {
            if (_style != value)
            {
                _style = value;
                ApplyStyle();
                OnPropertyChanged();
            }
        }
    }

    private object? ConvertValue(Type targetType, object? value)
    {
        if (value == null) return null;

        var valType = value.GetType();
        if (targetType.IsAssignableFrom(valType))
        {
            return value;
        }

        // 1. Enum conversion
        if (targetType.IsEnum && value is string strEnum)
        {
            return Enum.Parse(targetType, strEnum, true);
        }

        // 2. Boolean conversion
        if (targetType == typeof(bool) && value is string strBool)
        {
            return bool.Parse(strBool);
        }

        // 3. Thickness conversion
        if (targetType == typeof(Thickness))
        {
            if (value is float fVal) return new Thickness(fVal);
            if (value is double dVal) return new Thickness((float)dVal);
            if (value is int iVal) return new Thickness(iVal);
            if (value is string strThick)
            {
                var parts = strThick.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1 && float.TryParse(parts[0], out float uniform))
                    return new Thickness(uniform);
                if (parts.Length == 2 && float.TryParse(parts[0], out float h) && float.TryParse(parts[1], out float v))
                    return new Thickness(h, v);
                if (parts.Length == 4 && float.TryParse(parts[0], out float l) && float.TryParse(parts[1], out float t) && float.TryParse(parts[2], out float r) && float.TryParse(parts[3], out float b))
                    return new Thickness(l, t, r, b);
            }
        }

        // 4. CornerRadius conversion (which is defined as a float in ProGPU)
        if (targetType == typeof(float) && value is string strFloat)
        {
            if (float.TryParse(strFloat, out float parsedFloat))
            {
                return parsedFloat;
            }
        }

        // 5. Brush conversion
        if (targetType == typeof(ProGPU.Vector.Brush) && value is string strBrush)
        {
            if (strBrush.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return new ProGPU.Vector.SolidColorBrush(new Vector4(0f, 0f, 0f, 0f));
            }
            if (strBrush.StartsWith("#"))
            {
                var hex = strBrush.Substring(1);
                if (hex.Length == 6) hex = "FF" + hex;
                if (hex.Length == 8)
                {
                    uint rgba = Convert.ToUInt32(hex, 16);
                    float a = ((rgba >> 24) & 0xFF) / 255.0f;
                    float r = ((rgba >> 16) & 0xFF) / 255.0f;
                    float g = ((rgba >> 8) & 0xFF) / 255.0f;
                    float b = (rgba & 0xFF) / 255.0f;
                    return new ProGPU.Vector.SolidColorBrush(new Vector4(r, g, b, a));
                }
            }
        }

        // 6. Vector4 color conversion
        if (targetType == typeof(Vector4) && value is string strColor)
        {
            if (strColor.StartsWith("#"))
            {
                var hex = strColor.Substring(1);
                if (hex.Length == 6) hex = "FF" + hex;
                if (hex.Length == 8)
                {
                    uint rgba = Convert.ToUInt32(hex, 16);
                    float a = ((rgba >> 24) & 0xFF) / 255.0f;
                    float r = ((rgba >> 16) & 0xFF) / 255.0f;
                    float g = ((rgba >> 8) & 0xFF) / 255.0f;
                    float b = (rgba & 0xFF) / 255.0f;
                    return new Vector4(r, g, b, a);
                }
            }
        }

        // 7. Numeric standard conversions
        if (targetType == typeof(float)) return Convert.ToSingle(value);
        if (targetType == typeof(double)) return Convert.ToDouble(value);
        if (targetType == typeof(int)) return Convert.ToInt32(value);

        return Convert.ChangeType(value, targetType);
    }

    private void ApplyStyle()
    {
        ClearStyleValues();
        if (_style == null) return;
        if (!_style.TargetType.IsAssignableFrom(GetType())) return;

        foreach (var setter in _style.Setters)
        {
            if (string.IsNullOrEmpty(setter.Property)) continue;

            var dp = DependencyProperty.Lookup(GetType(), setter.Property);
            if (dp != null)
            {
                var val = setter.Value;
                if (val is StaticResourceRef staticRef)
                {
                    val = new ThemeResource(staticRef.ResourceKey);
                }

                if (val is ThemeResource themeResource)
                {
                    SetStyleValue(dp, themeResource);
                }
                else
                {
                    var converted = ConvertValue(dp.PropertyType, val);
                    SetStyleValue(dp, converted);
                }
            }
            else
            {
                var prop = GetType().GetProperty(setter.Property, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        var val = setter.Value;
                        if (val is StaticResourceRef staticRef)
                        {
                            val = ThemeManager.GetResource(staticRef.ResourceKey);
                        }
                        var convertedValue = ConvertValue(prop.PropertyType, val);
                        prop.SetValue(this, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error applying style property '{setter.Property}' on {GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
    }

    public float Width
    {
        get => WidthConstraint ?? float.NaN;
        set { if (Width != value) { WidthConstraint = float.IsNaN(value) ? null : value; OnPropertyChanged(); } }
    }

    public float Height
    {
        get => HeightConstraint ?? float.NaN;
        set { if (Height != value) { HeightConstraint = float.IsNaN(value) ? null : value; OnPropertyChanged(); } }
    }

    // Routed Events
    public event EventHandler<PointerRoutedEventArgs>? PointerPressed;
    public event EventHandler<PointerRoutedEventArgs>? PointerReleased;
    public event EventHandler<PointerRoutedEventArgs>? PointerMoved;
    public event EventHandler<PointerRoutedEventArgs>? PointerEntered;
    public event EventHandler<PointerRoutedEventArgs>? PointerExited;
    public event EventHandler<PointerRoutedEventArgs>? PointerWheelChanged;

    public event EventHandler<KeyRoutedEventArgs>? KeyDown;
    public event EventHandler<KeyRoutedEventArgs>? KeyUp;
    public event EventHandler<CharacterReceivedRoutedEventArgs>? CharacterReceived;

    // Helper methods to trigger routed events
    public virtual void OnPointerPressed(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerPressed?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnPointerPressed(e);
        }
    }

    public virtual void OnPointerReleased(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerReleased?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnPointerReleased(e);
        }
    }

    public virtual void OnPointerMoved(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerMoved?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnPointerMoved(e);
        }
    }

    public virtual void OnPointerEntered(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerEntered?.Invoke(this, e);
    }

    public virtual void OnPointerExited(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerExited?.Invoke(this, e);
    }

    public virtual void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        PointerWheelChanged?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnPointerWheelChanged(e);
        }
    }

    public virtual void OnKeyDown(KeyRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        KeyDown?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnKeyDown(e);
        }
    }

    public virtual void OnKeyUp(KeyRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        KeyUp?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnKeyUp(e);
        }
    }

    public virtual void OnCharacterReceived(CharacterReceivedRoutedEventArgs e)
    {
        e.OriginalSource ??= this;
        CharacterReceived?.Invoke(this, e);
        if (!e.Handled && Parent is FrameworkElement parentFe)
        {
            parentFe.OnCharacterReceived(e);
        }
    }
}

