using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public sealed class CalendarDatePickerDateChangedEventArgs : EventArgs
{
    public CalendarDatePickerDateChangedEventArgs(DateTimeOffset? oldDate, DateTimeOffset? newDate)
    {
        OldDate = oldDate;
        NewDate = newDate;
    }

    public DateTimeOffset? OldDate { get; }
    public DateTimeOffset? NewDate { get; }
}

/// <summary>
/// Drop-down single-date picker backed by a CalendarView.
/// </summary>
public class CalendarDatePicker : Control
{
    private static readonly DateTimeOffset DefaultMinDate = DateTimeOffset.Now.AddYears(-100);
    private static readonly DateTimeOffset DefaultMaxDate = DateTimeOffset.Now.AddYears(100);
    private readonly CalendarView _calendar;
    private bool _synchronizing;

    public static readonly DependencyProperty DateProperty = Register<DateTimeOffset?>(nameof(Date), null, OnDateChanged);
    public static readonly DependencyProperty IsCalendarOpenProperty = Register(nameof(IsCalendarOpen), false, OnIsCalendarOpenChanged);
    public static readonly DependencyProperty DateFormatProperty = Register(nameof(DateFormat), "shortdate");
    public static readonly DependencyProperty PlaceholderTextProperty = Register(nameof(PlaceholderText), "Select a date");
    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null);
    public static readonly DependencyProperty CalendarViewStyleProperty = Register<Style?>(nameof(CalendarViewStyle), null, OnCalendarContractChanged);
    public static readonly DependencyProperty LightDismissOverlayModeProperty = Register(nameof(LightDismissOverlayMode), LightDismissOverlayMode.Auto);
    public static readonly DependencyProperty DescriptionProperty = Register<object?>(nameof(Description), null);
    public static readonly DependencyProperty MinDateProperty = Register(nameof(MinDate), DefaultMinDate, OnCalendarContractChanged);
    public static readonly DependencyProperty MaxDateProperty = Register(nameof(MaxDate), DefaultMaxDate, OnCalendarContractChanged);
    public static readonly DependencyProperty IsTodayHighlightedProperty = Register(nameof(IsTodayHighlighted), true, OnCalendarContractChanged);
    public static readonly DependencyProperty DisplayModeProperty = Register(nameof(DisplayMode), CalendarViewDisplayMode.Month, OnCalendarContractChanged);
    public static readonly DependencyProperty FirstDayOfWeekProperty = Register(nameof(FirstDayOfWeek), DayOfWeek.Sunday, OnCalendarContractChanged);
    public static readonly DependencyProperty DayOfWeekFormatProperty = Register(nameof(DayOfWeekFormat), "{dayofweek.abbreviated(2)}", OnCalendarContractChanged);
    public static readonly DependencyProperty CalendarIdentifierProperty = Register(nameof(CalendarIdentifier), "GregorianCalendar", OnCalendarContractChanged);
    public static readonly DependencyProperty IsOutOfScopeEnabledProperty = Register(nameof(IsOutOfScopeEnabled), true, OnCalendarContractChanged);
    public static readonly DependencyProperty IsGroupLabelVisibleProperty = Register(nameof(IsGroupLabelVisible), false, OnCalendarContractChanged);

    public CalendarDatePicker()
    {
        _calendar = new CalendarView();
        _calendar.SelectedDatesChanged += OnCalendarSelectedDateChanged;
        SynchronizeCalendar();
    }

    public DateTimeOffset? Date { get => GetValue(DateProperty) as DateTimeOffset?; set => SetValue(DateProperty, CoerceDate(value)); }
    public bool IsCalendarOpen { get => (bool)(GetValue(IsCalendarOpenProperty) ?? false); set => SetValue(IsCalendarOpenProperty, value); }
    public string DateFormat { get => GetValue(DateFormatProperty) as string ?? "shortdate"; set => SetValue(DateFormatProperty, value ?? string.Empty); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty) as string ?? "Select a date"; set => SetValue(PlaceholderTextProperty, value ?? string.Empty); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => GetValue(HeaderTemplateProperty) as DataTemplate; set => SetValue(HeaderTemplateProperty, value); }
    public Style? CalendarViewStyle { get => GetValue(CalendarViewStyleProperty) as Style; set => SetValue(CalendarViewStyleProperty, value); }
    public LightDismissOverlayMode LightDismissOverlayMode { get => (LightDismissOverlayMode)(GetValue(LightDismissOverlayModeProperty) ?? LightDismissOverlayMode.Auto); set => SetValue(LightDismissOverlayModeProperty, value); }
    public object? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public DateTimeOffset MinDate { get => (DateTimeOffset)(GetValue(MinDateProperty) ?? DefaultMinDate); set { if (value > MaxDate) throw new ArgumentOutOfRangeException(nameof(value)); SetValue(MinDateProperty, value); } }
    public DateTimeOffset MaxDate { get => (DateTimeOffset)(GetValue(MaxDateProperty) ?? DefaultMaxDate); set { if (value < MinDate) throw new ArgumentOutOfRangeException(nameof(value)); SetValue(MaxDateProperty, value); } }
    public bool IsTodayHighlighted { get => (bool)(GetValue(IsTodayHighlightedProperty) ?? true); set => SetValue(IsTodayHighlightedProperty, value); }
    public CalendarViewDisplayMode DisplayMode { get => (CalendarViewDisplayMode)(GetValue(DisplayModeProperty) ?? CalendarViewDisplayMode.Month); set => SetValue(DisplayModeProperty, value); }
    public DayOfWeek FirstDayOfWeek { get => (DayOfWeek)(GetValue(FirstDayOfWeekProperty) ?? DayOfWeek.Sunday); set => SetValue(FirstDayOfWeekProperty, value); }
    public string DayOfWeekFormat { get => GetValue(DayOfWeekFormatProperty) as string ?? string.Empty; set => SetValue(DayOfWeekFormatProperty, value ?? string.Empty); }
    public string CalendarIdentifier { get => GetValue(CalendarIdentifierProperty) as string ?? "GregorianCalendar"; set => SetValue(CalendarIdentifierProperty, value ?? "GregorianCalendar"); }
    public bool IsOutOfScopeEnabled { get => (bool)(GetValue(IsOutOfScopeEnabledProperty) ?? true); set => SetValue(IsOutOfScopeEnabledProperty, value); }
    public bool IsGroupLabelVisible { get => (bool)(GetValue(IsGroupLabelVisibleProperty) ?? false); set => SetValue(IsGroupLabelVisibleProperty, value); }

    public event EventHandler<CalendarDatePickerDateChangedEventArgs>? DateChanged;
    public event EventHandler<object?>? Opened;
    public event EventHandler<object?>? Closed;

    public void SetDisplayDate(DateTimeOffset date) => _calendar.DisplayDate = CoerceDate(date)!.Value.LocalDateTime;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (!IsCalendarOpen)
            return new Vector2(160f, 32f);
        _calendar.Measure(availableSize);
        return _calendar.DesiredSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (IsCalendarOpen)
            _calendar.Arrange(arrangeRect);
    }

    private DateTimeOffset? CoerceDate(DateTimeOffset? value) =>
        value.HasValue ? (value.Value < MinDate ? MinDate : value.Value > MaxDate ? MaxDate : value.Value) : null;

    private void SynchronizeCalendar()
    {
        _calendar.MinDate = MinDate;
        _calendar.MaxDate = MaxDate;
        _calendar.IsTodayHighlighted = IsTodayHighlighted;
        _calendar.DisplayMode = DisplayMode;
        _calendar.FirstDayOfWeek = FirstDayOfWeek;
        _calendar.DayOfWeekFormat = DayOfWeekFormat;
        _calendar.CalendarIdentifier = CalendarIdentifier;
        _calendar.IsOutOfScopeEnabled = IsOutOfScopeEnabled;
        _calendar.IsGroupLabelVisible = IsGroupLabelVisible;
        _calendar.Style = CalendarViewStyle;
        _calendar.SelectedDate = Date?.LocalDateTime;
    }

    private void OnCalendarSelectedDateChanged(object? sender, EventArgs args)
    {
        if (_synchronizing)
            return;
        Date = _calendar.SelectedDate.HasValue
            ? new DateTimeOffset(_calendar.SelectedDate.Value)
            : null;
    }

    private static void OnDateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var picker = (CalendarDatePicker)dependencyObject;
        var oldDate = args.OldValue as DateTimeOffset?;
        var newDate = picker.CoerceDate(args.NewValue as DateTimeOffset?);
        if (!Equals(args.NewValue, newDate))
        {
            picker.SetValue(DateProperty, newDate);
            return;
        }

        picker._synchronizing = true;
        try
        {
            picker._calendar.SelectedDate = newDate?.LocalDateTime;
        }
        finally
        {
            picker._synchronizing = false;
        }
        picker.DateChanged?.Invoke(picker, new CalendarDatePickerDateChangedEventArgs(oldDate, newDate));
    }

    private static void OnIsCalendarOpenChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var picker = (CalendarDatePicker)dependencyObject;
        var isOpen = (bool)(args.NewValue ?? false);
        if (isOpen)
        {
            picker.SynchronizeCalendar();
            if (!ReferenceEquals(picker._calendar.Parent, picker))
                picker.AddChild(picker._calendar);
            picker.Opened?.Invoke(picker, null);
        }
        else
        {
            if (ReferenceEquals(picker._calendar.Parent, picker))
                picker.RemoveChild(picker._calendar);
            picker.Closed?.Invoke(picker, null);
        }
        picker.InvalidateMeasure();
        picker.Invalidate();
    }

    private static void OnCalendarContractChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var picker = (CalendarDatePicker)dependencyObject;
        picker.SynchronizeCalendar();
        var coerced = picker.CoerceDate(picker.Date);
        if (coerced != picker.Date)
            picker.Date = coerced;
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(CalendarDatePicker),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}
