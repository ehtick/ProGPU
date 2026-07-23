using System;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// Displays one selected item at a time.
/// </summary>
public class FlipView : Selector
{
    public static readonly DependencyProperty UseTouchAnimationsForAllNavigationProperty =
        DependencyProperty.Register(
            nameof(UseTouchAnimationsForAllNavigation),
            typeof(bool),
            typeof(FlipView),
            new PropertyMetadata(false));

    public FlipView()
    {
        ItemVisualFactory = static () => new FlipViewItem();
        BindVisualCallback = (visual, item, index) =>
        {
            if (visual is FlipViewItem container)
            {
                container.Content = item;
                var selectedIndex = SelectedIndex < 0 ? 0 : SelectedIndex;
                container.Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            }
        };
    }

    public bool UseTouchAnimationsForAllNavigation
    {
        get => (bool)(GetValue(UseTouchAnimationsForAllNavigationProperty) ?? false);
        set => SetValue(UseTouchAnimationsForAllNavigationProperty, value);
    }

    protected override void OnSelectedIndexChanged(int oldValue, int newValue)
    {
        base.OnSelectedIndexChanged(oldValue, newValue);
        UpdateRealizedSelection();
    }

    private void UpdateRealizedSelection()
    {
        var host = ItemsPanelRoot;
        if (host == null)
            return;

        var selectedIndex = SelectedIndex < 0 && ItemCount > 0 ? 0 : SelectedIndex;
        for (var index = 0; index < host.Children.Count; index++)
        {
            if (host.Children[index] is FrameworkElement element)
                element.Visibility = index == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>
/// Item container created by <see cref="FlipView"/>.
/// </summary>
public class FlipViewItem : SelectorItem
{
}

/// <summary>
/// Root container for an ItemsControl group.
/// </summary>
public class GroupItem : ContentControl
{
}
