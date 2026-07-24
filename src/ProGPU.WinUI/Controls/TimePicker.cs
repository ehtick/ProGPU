using System;
using Microsoft.UI.Xaml.Markup;

namespace Microsoft.UI.Xaml.Controls;

public sealed class TimePickerValueChangedEventArgs : EventArgs
{
    public TimePickerValueChangedEventArgs(TimeSpan oldTime, TimeSpan newTime)
    {
        OldTime = oldTime;
        NewTime = newTime;
    }

    public TimeSpan OldTime { get; }
    public TimeSpan NewTime { get; }
}

public sealed class TimePickerSelectedValueChangedEventArgs : EventArgs
{
    public TimePickerSelectedValueChangedEventArgs(TimeSpan? oldTime, TimeSpan? newTime)
    {
        OldTime = oldTime;
        NewTime = newTime;
    }

    public TimeSpan? OldTime { get; }
    public TimeSpan? NewTime { get; }
}

/// <summary>
/// Selects a time-of-day using a minute-resolution value model.
/// </summary>
[ContentProperty(Name = nameof(Header))]
public class TimePicker : Control
{
    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null);
    public static readonly DependencyProperty ClockIdentifierProperty = Register(nameof(ClockIdentifier), "12HourClock");
    public static readonly DependencyProperty MinuteIncrementProperty = Register(nameof(MinuteIncrement), 1, OnMinuteIncrementChanged);
    public static readonly DependencyProperty TimeProperty = Register(nameof(Time), TimeSpan.Zero, OnTimeChanged);
    public static readonly DependencyProperty SelectedTimeProperty = Register<TimeSpan?>(nameof(SelectedTime), null, OnSelectedTimeChanged);
    public static readonly DependencyProperty LightDismissOverlayModeProperty =
        Register(nameof(LightDismissOverlayMode), LightDismissOverlayMode.Auto);

    private bool _synchronizing;

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => GetValue(HeaderTemplateProperty) as DataTemplate; set => SetValue(HeaderTemplateProperty, value); }
    public string ClockIdentifier { get => GetValue(ClockIdentifierProperty) as string ?? "12HourClock"; set => SetValue(ClockIdentifierProperty, value ?? "12HourClock"); }

    public int MinuteIncrement
    {
        get => (int)(GetValue(MinuteIncrementProperty) ?? 1);
        set
        {
            if (value is < 1 or > 59)
                throw new ArgumentOutOfRangeException(nameof(value), "MinuteIncrement must be between 1 and 59.");
            SetValue(MinuteIncrementProperty, value);
        }
    }

    public TimeSpan Time
    {
        get => (TimeSpan)(GetValue(TimeProperty) ?? TimeSpan.Zero);
        set => SetValue(TimeProperty, Normalize(value));
    }

    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty) as TimeSpan?;
        set => SetValue(SelectedTimeProperty, value.HasValue ? Normalize(value.Value) : null);
    }

    public LightDismissOverlayMode LightDismissOverlayMode
    {
        get => (LightDismissOverlayMode)(GetValue(LightDismissOverlayModeProperty) ?? LightDismissOverlayMode.Auto);
        set => SetValue(LightDismissOverlayModeProperty, value);
    }

    public event EventHandler<TimePickerValueChangedEventArgs>? TimeChanged;
    public event EventHandler<TimePickerSelectedValueChangedEventArgs>? SelectedTimeChanged;

    private static void OnMinuteIncrementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var value = (int)(args.NewValue ?? 1);
        if (value is < 1 or > 59)
            throw new ArgumentOutOfRangeException(nameof(MinuteIncrement), "MinuteIncrement must be between 1 and 59.");
        dependencyObject.SetValue(TimeProperty, RoundToIncrement((TimeSpan)(dependencyObject.GetValue(TimeProperty) ?? TimeSpan.Zero), value));
    }

    private static void OnTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var picker = (TimePicker)dependencyObject;
        var oldTime = (TimeSpan)(args.OldValue ?? TimeSpan.Zero);
        var newTime = RoundToIncrement(Normalize((TimeSpan)(args.NewValue ?? TimeSpan.Zero)), picker.MinuteIncrement);
        if (!Equals(args.NewValue, newTime))
        {
            picker.SetValue(TimeProperty, newTime);
            return;
        }

        if (!picker._synchronizing)
        {
            picker._synchronizing = true;
            try
            {
                picker.SelectedTime = newTime;
            }
            finally
            {
                picker._synchronizing = false;
            }
        }

        picker.TimeChanged?.Invoke(picker, new TimePickerValueChangedEventArgs(oldTime, newTime));
    }

    private static void OnSelectedTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var picker = (TimePicker)dependencyObject;
        var oldTime = args.OldValue as TimeSpan?;
        var newTime = args.NewValue as TimeSpan?;
        if (newTime.HasValue)
        {
            var normalized = RoundToIncrement(Normalize(newTime.Value), picker.MinuteIncrement);
            if (normalized != newTime.Value)
            {
                picker.SetValue(SelectedTimeProperty, normalized);
                return;
            }
        }

        if (!picker._synchronizing && newTime.HasValue)
        {
            picker._synchronizing = true;
            try
            {
                picker.Time = newTime.Value;
            }
            finally
            {
                picker._synchronizing = false;
            }
        }

        picker.SelectedTimeChanged?.Invoke(
            picker,
            new TimePickerSelectedValueChangedEventArgs(oldTime, newTime));
    }

    private static TimeSpan Normalize(TimeSpan value)
    {
        var ticks = value.Ticks % TimeSpan.TicksPerDay;
        if (ticks < 0)
            ticks += TimeSpan.TicksPerDay;
        return TimeSpan.FromTicks(ticks);
    }

    private static TimeSpan RoundToIncrement(TimeSpan value, int increment)
    {
        var totalMinutes = (int)Math.Round(value.TotalMinutes, MidpointRounding.AwayFromZero);
        var roundedMinutes = totalMinutes / increment * increment;
        return TimeSpan.FromMinutes(roundedMinutes % (24 * 60));
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(TimePicker),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsRender = true });
}
