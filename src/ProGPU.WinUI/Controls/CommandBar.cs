using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;

namespace Microsoft.UI.Xaml.Controls;

public enum CommandBarDefaultLabelPosition
{
    Bottom = 0,
    Right = 1,
    Collapsed = 2
}

public enum CommandBarOverflowButtonVisibility
{
    Auto = 0,
    Visible = 1,
    Collapsed = 2
}

[ContentProperty(Name = nameof(PrimaryCommands))]
public class CommandBar : AppBar
{
    public static readonly DependencyProperty CommandBarOverflowPresenterStyleProperty = DependencyProperty.Register(
        nameof(CommandBarOverflowPresenterStyle), typeof(Style), typeof(CommandBar), new PropertyMetadata(null));

    public static readonly DependencyProperty DefaultLabelPositionProperty = DependencyProperty.Register(
        nameof(DefaultLabelPosition), typeof(CommandBarDefaultLabelPosition), typeof(CommandBar),
        new PropertyMetadata(CommandBarDefaultLabelPosition.Bottom));

    public static readonly DependencyProperty OverflowButtonVisibilityProperty = DependencyProperty.Register(
        nameof(OverflowButtonVisibility), typeof(CommandBarOverflowButtonVisibility), typeof(CommandBar),
        new PropertyMetadata(CommandBarOverflowButtonVisibility.Auto));

    public static readonly DependencyProperty IsDynamicOverflowEnabledProperty = DependencyProperty.Register(
        nameof(IsDynamicOverflowEnabled), typeof(bool), typeof(CommandBar), new PropertyMetadata(true));

    public CommandBar()
    {
        PrimaryCommands = new ObservableCollection<ICommandBarElement>();
        SecondaryCommands = new ObservableCollection<ICommandBarElement>();
    }

    public ObservableCollection<ICommandBarElement> PrimaryCommands { get; }
    public ObservableCollection<ICommandBarElement> SecondaryCommands { get; }
    public CommandBarTemplateSettings CommandBarTemplateSettings { get; } = new();

    public Style? CommandBarOverflowPresenterStyle
    {
        get => GetValue(CommandBarOverflowPresenterStyleProperty) as Style;
        set => SetValue(CommandBarOverflowPresenterStyleProperty, value);
    }

    public CommandBarDefaultLabelPosition DefaultLabelPosition
    {
        get => (CommandBarDefaultLabelPosition)(GetValue(DefaultLabelPositionProperty) ?? CommandBarDefaultLabelPosition.Bottom);
        set => SetValue(DefaultLabelPositionProperty, value);
    }

    public CommandBarOverflowButtonVisibility OverflowButtonVisibility
    {
        get => (CommandBarOverflowButtonVisibility)(GetValue(OverflowButtonVisibilityProperty) ?? CommandBarOverflowButtonVisibility.Auto);
        set => SetValue(OverflowButtonVisibilityProperty, value);
    }

    public bool IsDynamicOverflowEnabled
    {
        get => (bool)(GetValue(IsDynamicOverflowEnabledProperty) ?? true);
        set => SetValue(IsDynamicOverflowEnabledProperty, value);
    }
}

public class CommandBarOverflowPresenter : ItemsControl
{
}
