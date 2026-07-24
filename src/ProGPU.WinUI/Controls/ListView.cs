using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace Microsoft.UI.Xaml.Controls;

public enum IncrementalLoadingTrigger
{
    None = 0,
    Edge = 1
}

public enum ListViewReorderMode
{
    Disabled = 0,
    Enabled = 1
}

public enum ListViewSelectionMode
{
    None = 0,
    Single = 1,
    Multiple = 2,
    Extended = 3
}

public abstract class ListViewBase : Selector, ISemanticZoomInformation
{
    private readonly List<object> _selectedItems = new();

    public static readonly DependencyProperty SelectionModeProperty = Register(nameof(SelectionMode), ListViewSelectionMode.Single);
    public static readonly DependencyProperty IsSwipeEnabledProperty = Register(nameof(IsSwipeEnabled), true);
    public static readonly DependencyProperty CanDragItemsProperty = Register(nameof(CanDragItems), false);
    public static readonly DependencyProperty CanReorderItemsProperty = Register(nameof(CanReorderItems), false);
    public static readonly DependencyProperty IsItemClickEnabledProperty = Register(nameof(IsItemClickEnabled), false);
    public static readonly DependencyProperty DataFetchSizeProperty = Register(nameof(DataFetchSize), 0d);
    public static readonly DependencyProperty IncrementalLoadingThresholdProperty = Register(nameof(IncrementalLoadingThreshold), 0d);
    public static readonly DependencyProperty IncrementalLoadingTriggerProperty = Register(nameof(IncrementalLoadingTrigger), IncrementalLoadingTrigger.Edge);
    public static readonly DependencyProperty ShowsScrollingPlaceholdersProperty = Register(nameof(ShowsScrollingPlaceholders), true);
    public static readonly DependencyProperty ReorderModeProperty = Register(nameof(ReorderMode), ListViewReorderMode.Disabled);
    public static readonly DependencyProperty IsMultiSelectCheckBoxEnabledProperty = Register(nameof(IsMultiSelectCheckBoxEnabled), true);
    public static readonly DependencyProperty SingleSelectionFollowsFocusProperty = Register(nameof(SingleSelectionFollowsFocus), false);
    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null);
    public static readonly DependencyProperty HeaderTransitionsProperty = Register<TransitionCollection?>(nameof(HeaderTransitions), null);
    public static readonly DependencyProperty FooterProperty = Register<object?>(nameof(Footer), null);
    public static readonly DependencyProperty FooterTemplateProperty = Register<DataTemplate?>(nameof(FooterTemplate), null);
    public static readonly DependencyProperty FooterTransitionsProperty = Register<TransitionCollection?>(nameof(FooterTransitions), null);

    public IList<object> SelectedItems => _selectedItems;
    public ListViewSelectionMode SelectionMode { get => Get<ListViewSelectionMode>(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }
    public bool IsSwipeEnabled { get => Get<bool>(IsSwipeEnabledProperty); set => SetValue(IsSwipeEnabledProperty, value); }
    public bool CanDragItems { get => Get<bool>(CanDragItemsProperty); set => SetValue(CanDragItemsProperty, value); }
    public bool CanReorderItems { get => Get<bool>(CanReorderItemsProperty); set => SetValue(CanReorderItemsProperty, value); }
    public bool IsItemClickEnabled { get => Get<bool>(IsItemClickEnabledProperty); set => SetValue(IsItemClickEnabledProperty, value); }
    public double DataFetchSize { get => Get<double>(DataFetchSizeProperty); set => SetValue(DataFetchSizeProperty, value); }
    public double IncrementalLoadingThreshold { get => Get<double>(IncrementalLoadingThresholdProperty); set => SetValue(IncrementalLoadingThresholdProperty, value); }
    public IncrementalLoadingTrigger IncrementalLoadingTrigger { get => Get<IncrementalLoadingTrigger>(IncrementalLoadingTriggerProperty); set => SetValue(IncrementalLoadingTriggerProperty, value); }
    public bool ShowsScrollingPlaceholders { get => Get<bool>(ShowsScrollingPlaceholdersProperty); set => SetValue(ShowsScrollingPlaceholdersProperty, value); }
    public ListViewReorderMode ReorderMode { get => Get<ListViewReorderMode>(ReorderModeProperty); set => SetValue(ReorderModeProperty, value); }
    public bool IsMultiSelectCheckBoxEnabled { get => Get<bool>(IsMultiSelectCheckBoxEnabledProperty); set => SetValue(IsMultiSelectCheckBoxEnabledProperty, value); }
    public bool SingleSelectionFollowsFocus { get => Get<bool>(SingleSelectionFollowsFocusProperty); set => SetValue(SingleSelectionFollowsFocusProperty, value); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => GetValue(HeaderTemplateProperty) as DataTemplate; set => SetValue(HeaderTemplateProperty, value); }
    public TransitionCollection? HeaderTransitions { get => GetValue(HeaderTransitionsProperty) as TransitionCollection; set => SetValue(HeaderTransitionsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public DataTemplate? FooterTemplate { get => GetValue(FooterTemplateProperty) as DataTemplate; set => SetValue(FooterTemplateProperty, value); }
    public TransitionCollection? FooterTransitions { get => GetValue(FooterTransitionsProperty) as TransitionCollection; set => SetValue(FooterTransitionsProperty, value); }

    public bool IsActiveView { get; set; }
    public bool IsZoomedInView { get; set; }
    public SemanticZoom? SemanticZoomOwner { get; set; }

    public void InitializeViewChange() => Invalidate();
    public void CompleteViewChange() => Invalidate();
    public void StartViewChangeFrom(SemanticZoomLocation source, SemanticZoomLocation destination) { }
    public void StartViewChangeTo(SemanticZoomLocation source, SemanticZoomLocation destination) { }
    public void CompleteViewChangeFrom(SemanticZoomLocation source, SemanticZoomLocation destination) { }
    public void CompleteViewChangeTo(SemanticZoomLocation source, SemanticZoomLocation destination) { }
    public void MakeVisible(SemanticZoomLocation item)
    {
        if (item.Item == null) return;
        var index = Items.IndexOf(item.Item);
        if (index >= 0) SelectedIndex = index;
    }

    private T Get<T>(DependencyProperty property) => (T)GetValue(property)!;
    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(ListViewBase),
            new PropertyMetadata(defaultValue) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}

public class ListView : ListViewBase
{
}

public class GridView : ListViewBase
{
}

public abstract class ListViewBaseHeaderItem : ContentControl
{
}

public class ListViewHeaderItem : ListViewBaseHeaderItem
{
}

public class GridViewHeaderItem : ListViewBaseHeaderItem
{
}

public class ListViewItem : SelectorItem
{
    public ListViewItemTemplateSettings TemplateSettings { get; } = new();
}

public class GridViewItem : SelectorItem
{
    public GridViewItemTemplateSettings TemplateSettings { get; } = new();
}
