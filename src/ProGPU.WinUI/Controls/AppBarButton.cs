using Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Xaml.Controls;

public enum CommandBarLabelPosition
{
    Default = 0,
    Collapsed = 1
}

public interface ICommandBarElement
{
    bool IsCompact { get; set; }
    bool IsInOverflow { get; }
    int DynamicOverflowOrder { get; set; }
}

/// <summary>A command button with a label and an icon.</summary>
public class AppBarButton : Button, ICommandBarElement
{
    public AppBarButtonTemplateSettings TemplateSettings { get; } = new();

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconElement), typeof(AppBarButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(AppBarButton), new PropertyMetadata(false));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(AppBarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelPositionProperty = DependencyProperty.Register(
        nameof(LabelPosition), typeof(CommandBarLabelPosition), typeof(AppBarButton),
        new PropertyMetadata(CommandBarLabelPosition.Default));

    public static readonly DependencyProperty IsInOverflowProperty = DependencyProperty.Register(
        nameof(IsInOverflow), typeof(bool), typeof(AppBarButton), new PropertyMetadata(false));

    public static readonly DependencyProperty DynamicOverflowOrderProperty = DependencyProperty.Register(
        nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarButton), new PropertyMetadata(0));

    public IconElement? Icon
    {
        get => GetValue(IconProperty) as IconElement;
        set => SetValue(IconProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)(GetValue(IsCompactProperty) ?? false);
        set => SetValue(IsCompactProperty, value);
    }

    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    public CommandBarLabelPosition LabelPosition
    {
        get => (CommandBarLabelPosition)(GetValue(LabelPositionProperty) ?? CommandBarLabelPosition.Default);
        set => SetValue(LabelPositionProperty, value);
    }

    public bool IsInOverflow => (bool)(GetValue(IsInOverflowProperty) ?? false);

    public int DynamicOverflowOrder
    {
        get => (int)(GetValue(DynamicOverflowOrderProperty) ?? 0);
        set => SetValue(DynamicOverflowOrderProperty, value);
    }
}

/// <summary>A toggleable app-bar command with a label and an icon.</summary>
public class AppBarToggleButton : ToggleButton, ICommandBarElement
{
    public AppBarToggleButtonTemplateSettings TemplateSettings { get; } = new();

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconElement), typeof(AppBarToggleButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(AppBarToggleButton), new PropertyMetadata(false));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(AppBarToggleButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsInOverflowProperty = DependencyProperty.Register(
        nameof(IsInOverflow), typeof(bool), typeof(AppBarToggleButton), new PropertyMetadata(false));

    public static readonly DependencyProperty DynamicOverflowOrderProperty = DependencyProperty.Register(
        nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarToggleButton), new PropertyMetadata(0));

    public IconElement? Icon
    {
        get => GetValue(IconProperty) as IconElement;
        set => SetValue(IconProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)(GetValue(IsCompactProperty) ?? false);
        set => SetValue(IsCompactProperty, value);
    }

    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    public bool IsInOverflow => (bool)(GetValue(IsInOverflowProperty) ?? false);

    public int DynamicOverflowOrder
    {
        get => (int)(GetValue(DynamicOverflowOrderProperty) ?? 0);
        set => SetValue(DynamicOverflowOrderProperty, value);
    }
}

/// <summary>Separates groups of app-bar commands.</summary>
public sealed class AppBarSeparator : Control, ICommandBarElement
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(AppBarSeparator), new PropertyMetadata(false));

    public static readonly DependencyProperty IsInOverflowProperty = DependencyProperty.Register(
        nameof(IsInOverflow), typeof(bool), typeof(AppBarSeparator), new PropertyMetadata(false));

    public static readonly DependencyProperty DynamicOverflowOrderProperty = DependencyProperty.Register(
        nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarSeparator), new PropertyMetadata(0));

    public bool IsCompact { get => (bool)(GetValue(IsCompactProperty) ?? false); set => SetValue(IsCompactProperty, value); }
    public bool IsInOverflow => (bool)(GetValue(IsInOverflowProperty) ?? false);
    public int DynamicOverflowOrder { get => (int)(GetValue(DynamicOverflowOrderProperty) ?? 0); set => SetValue(DynamicOverflowOrderProperty, value); }
}
