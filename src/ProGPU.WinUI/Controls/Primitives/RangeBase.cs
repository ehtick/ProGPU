using System;

namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>
/// Base class for controls whose value is constrained to a numeric range.
/// </summary>
public class RangeBase : Control
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(RangeBase),
        new PropertyMetadata(0d, OnMinimumChanged) { AffectsRender = true });

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(RangeBase),
        new PropertyMetadata(1d, OnMaximumChanged) { AffectsRender = true });

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(RangeBase),
        new PropertyMetadata(0d, OnValuePropertyChanged) { AffectsRender = true });

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange),
        typeof(double),
        typeof(RangeBase),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange),
        typeof(double),
        typeof(RangeBase),
        new PropertyMetadata(10d));

    public double Minimum
    {
        get => GetDouble(MinimumProperty);
        set
        {
            EnsureFinite(value, nameof(value));
            SetValue(MinimumProperty, value);
        }
    }

    public double Maximum
    {
        get => GetDouble(MaximumProperty);
        set
        {
            EnsureFinite(value, nameof(value));
            SetValue(MaximumProperty, value);
        }
    }

    public double Value
    {
        get => GetDouble(ValueProperty);
        set
        {
            EnsureFinite(value, nameof(value));
            SetValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
        }
    }

    public double SmallChange
    {
        get => GetDouble(SmallChangeProperty);
        set
        {
            EnsureNonNegativeFinite(value, nameof(value));
            SetValue(SmallChangeProperty, value);
        }
    }

    public double LargeChange
    {
        get => GetDouble(LargeChangeProperty);
        set
        {
            EnsureNonNegativeFinite(value, nameof(value));
            SetValue(LargeChangeProperty, value);
        }
    }

    public event EventHandler<RoutedPropertyChangedEventArgs<double>>? ValueChanged;

    protected virtual void OnValueChanged(double oldValue, double newValue)
    {
        Invalidate();
        ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(oldValue, newValue)
        {
            OriginalSource = this
        });
    }

    private static void OnMinimumChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var range = (RangeBase)dependencyObject;
        var minimum = AsDouble(args.NewValue);
        EnsureFinite(minimum, nameof(Minimum));

        if (range.Maximum < minimum)
            range.Maximum = minimum;

        range.CoerceValue();
    }

    private static void OnMaximumChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var range = (RangeBase)dependencyObject;
        var maximum = AsDouble(args.NewValue);
        EnsureFinite(maximum, nameof(Maximum));

        if (range.Minimum > maximum)
            range.Minimum = maximum;

        range.CoerceValue();
    }

    private static void OnValuePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var range = (RangeBase)dependencyObject;
        var oldValue = AsDouble(args.OldValue);
        var newValue = AsDouble(args.NewValue);
        EnsureFinite(newValue, nameof(Value));

        var coerced = Math.Clamp(newValue, range.Minimum, range.Maximum);
        if (coerced != newValue)
        {
            range.SetValue(ValueProperty, coerced);
            return;
        }

        if (oldValue != newValue)
            range.OnValueChanged(oldValue, newValue);
    }

    private void CoerceValue()
    {
        var value = Value;
        var coerced = Math.Clamp(value, Minimum, Maximum);
        if (value != coerced)
            SetValue(ValueProperty, coerced);
        else
            Invalidate();
    }

    private double GetDouble(DependencyProperty property) => AsDouble(GetValue(property));

    private static double AsDouble(object? value) => value is double result ? result : 0d;

    private static void EnsureFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Range values must be finite.");
    }

    private static void EnsureNonNegativeFinite(double value, string parameterName)
    {
        EnsureFinite(value, parameterName);
        if (value < 0d)
            throw new ArgumentOutOfRangeException(parameterName, "Range increments cannot be negative.");
    }
}
