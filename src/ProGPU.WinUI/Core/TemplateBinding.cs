using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Microsoft.UI.Xaml.Controls;

public class TemplateBinding
{
    private readonly WeakReference<DependencyObject> _targetRef;
    private readonly DependencyProperty _targetProperty;
    private readonly WeakReference<DependencyObject> _sourceRef;
    private readonly DependencyProperty _sourceProperty;
    private readonly long _token;

    public TemplateBinding(DependencyObject target, DependencyProperty targetProperty, DependencyObject source, DependencyProperty sourceProperty)
    {
        _targetRef = new WeakReference<DependencyObject>(target);
        _targetProperty = targetProperty;
        _sourceRef = new WeakReference<DependencyObject>(source);
        _sourceProperty = sourceProperty;

        // Apply initial value immediately
        UpdateTarget(source.GetValue(sourceProperty));

        // Hook up standard DependencyProperty callback
        _token = source.RegisterPropertyChangedCallback(sourceProperty, OnSourcePropertyChanged);
    }

    public static TemplateBinding Bind(DependencyObject target, DependencyProperty targetProperty, DependencyObject source, DependencyProperty sourceProperty)
    {
        return new TemplateBinding(target, targetProperty, source, sourceProperty);
    }

    private void OnSourcePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateTarget(e.NewValue);
    }

    private void UpdateTarget(object? newValue)
    {
        if (_targetRef.TryGetTarget(out var target))
        {
            var converted = ConvertValue(newValue, _targetProperty.PropertyType);
            target.SetValue(_targetProperty, converted);
        }
        else
        {
            if (_sourceRef.TryGetTarget(out var source))
            {
                source.UnregisterPropertyChangedCallback(_sourceProperty, _token);
            }
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;
        var valType = value.GetType();
        if (targetType.IsAssignableFrom(valType)) return value;

        try
        {
            if (targetType == typeof(float)) return Convert.ToSingle(value);
            if (targetType == typeof(double)) return Convert.ToDouble(value);
            if (targetType == typeof(int)) return Convert.ToInt32(value);
            if (targetType == typeof(Thickness) && value is float fVal) return new Thickness(fVal);
            if (targetType == typeof(Thickness) && value is double dVal) return new Thickness((float)dVal);
            if (targetType == typeof(Thickness) && value is Microsoft.UI.Xaml.Thickness t) return (Thickness)t;
            if (targetType == typeof(Microsoft.UI.Xaml.Thickness) && value is Thickness pt) return (Microsoft.UI.Xaml.Thickness)pt;
            
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }
}
