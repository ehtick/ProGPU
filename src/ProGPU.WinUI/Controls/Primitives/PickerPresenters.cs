namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>
/// Presenter used by DatePicker flyouts.
/// </summary>
public class DatePickerFlyoutPresenter : Control
{
    public static readonly DependencyProperty IsDefaultShadowEnabledProperty =
        DependencyProperty.Register(
            nameof(IsDefaultShadowEnabled),
            typeof(bool),
            typeof(DatePickerFlyoutPresenter),
            new PropertyMetadata(true) { AffectsRender = true });

    public bool IsDefaultShadowEnabled
    {
        get => (bool)(GetValue(IsDefaultShadowEnabledProperty) ?? true);
        set => SetValue(IsDefaultShadowEnabledProperty, value);
    }
}

/// <summary>
/// Presenter used by list-picker flyouts.
/// </summary>
public class ListPickerFlyoutPresenter : Control
{
}

/// <summary>
/// Content presenter used by general picker flyouts.
/// </summary>
public class PickerFlyoutPresenter : ContentControl
{
}

/// <summary>
/// Presenter used by TimePicker flyouts.
/// </summary>
public class TimePickerFlyoutPresenter : Control
{
    public static readonly DependencyProperty IsDefaultShadowEnabledProperty =
        DependencyProperty.Register(
            nameof(IsDefaultShadowEnabled),
            typeof(bool),
            typeof(TimePickerFlyoutPresenter),
            new PropertyMetadata(true) { AffectsRender = true });

    public bool IsDefaultShadowEnabled
    {
        get => (bool)(GetValue(IsDefaultShadowEnabledProperty) ?? true);
        set => SetValue(IsDefaultShadowEnabledProperty, value);
    }
}
