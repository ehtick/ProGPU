using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// Presents command-bar elements in a lightweight flyout.
/// </summary>
[ContentProperty(Name = nameof(PrimaryCommands))]
public class CommandBarFlyout : FlyoutBase
{
    public static readonly DependencyProperty AlwaysExpandedProperty = DependencyProperty.Register(
        nameof(AlwaysExpanded),
        typeof(bool),
        typeof(CommandBarFlyout),
        new PropertyMetadata(false));

    public CommandBarFlyout()
    {
        PrimaryCommands = new ObservableCollection<ICommandBarElement>();
        SecondaryCommands = new ObservableCollection<ICommandBarElement>();
    }

    public ObservableCollection<ICommandBarElement> PrimaryCommands { get; }

    public ObservableCollection<ICommandBarElement> SecondaryCommands { get; }

    public bool AlwaysExpanded
    {
        get => (bool)(GetValue(AlwaysExpandedProperty) ?? false);
        set => SetValue(AlwaysExpandedProperty, value);
    }

    protected override Control CreatePresenter()
    {
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Background = new ThemeResourceBrush("CommandBarFlyoutPresenterBackground"),
            Spacing = 2f
        };

        if (PrimaryCommands.Count > 0)
        {
            var primaryPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2f
            };
            AddCommands(primaryPanel, PrimaryCommands);
            root.Children.Add(primaryPanel);
        }

        if (SecondaryCommands.Count > 0 && (AlwaysExpanded || PrimaryCommands.Count == 0))
        {
            var secondaryPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2f
            };
            AddCommands(secondaryPanel, SecondaryCommands);
            root.Children.Add(secondaryPanel);
        }

        return new ContentControl
        {
            Content = root,
            Padding = new Thickness(4f),
            Background = new ThemeResourceBrush("CommandBarFlyoutPresenterBackground"),
            BorderBrush = new ThemeResourceBrush("CommandBarFlyoutPresenterBorderBrush"),
            BorderThickness = new Thickness(1f)
        };
    }

    private static void AddCommands(
        StackPanel panel,
        ObservableCollection<ICommandBarElement> commands)
    {
        foreach (var command in commands)
        {
            if (command is FrameworkElement element)
                panel.Children.Add(element);
        }
    }
}

/// <summary>
/// Supplies the standard editing commands for text-control context flyouts.
/// </summary>
public class TextCommandBarFlyout : CommandBarFlyout
{
}
