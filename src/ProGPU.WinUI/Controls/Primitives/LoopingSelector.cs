using System;
using System.Collections.ObjectModel;

namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>
/// Displays a selected item with optional circular previous/next navigation.
/// </summary>
public class LoopingSelector : Control
{
    public static readonly DependencyProperty ShouldLoopProperty = Register(nameof(ShouldLoop), true);
    public static readonly DependencyProperty ItemsProperty = Register<ObservableCollection<object>?>(nameof(Items), null, OnItemsChanged);
    public static readonly DependencyProperty SelectedIndexProperty = Register(nameof(SelectedIndex), -1, OnSelectedIndexChanged);
    public static readonly DependencyProperty SelectedItemProperty = Register<object?>(nameof(SelectedItem), null, OnSelectedItemChanged);
    public static readonly DependencyProperty ItemWidthProperty = Register(nameof(ItemWidth), 0);
    public static readonly DependencyProperty ItemHeightProperty = Register(nameof(ItemHeight), 0);
    public static readonly DependencyProperty ItemTemplateProperty = Register<DataTemplate?>(nameof(ItemTemplate), null);

    private bool _synchronizingSelection;

    public LoopingSelector() => Items = new ObservableCollection<object>();

    public bool ShouldLoop { get => (bool)(GetValue(ShouldLoopProperty) ?? true); set => SetValue(ShouldLoopProperty, value); }
    public ObservableCollection<object> Items { get => (ObservableCollection<object>?)GetValue(ItemsProperty) ?? []; set => SetValue(ItemsProperty, value ?? []); }
    public int SelectedIndex { get => (int)(GetValue(SelectedIndexProperty) ?? -1); set => SetValue(SelectedIndexProperty, CoerceIndex(value)); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int ItemWidth { get => (int)(GetValue(ItemWidthProperty) ?? 0); set => SetValue(ItemWidthProperty, Math.Max(0, value)); }
    public int ItemHeight { get => (int)(GetValue(ItemHeightProperty) ?? 0); set => SetValue(ItemHeightProperty, Math.Max(0, value)); }
    public DataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty) as DataTemplate; set => SetValue(ItemTemplateProperty, value); }

    public event EventHandler? SelectionChanged;

    public void MoveNext() => SelectedIndex = Move(SelectedIndex, 1);

    public void MovePrevious() => SelectedIndex = Move(SelectedIndex, -1);

    private int Move(int index, int delta)
    {
        if (Items.Count == 0)
            return -1;
        var candidate = index < 0 ? 0 : index + delta;
        if (ShouldLoop)
            return (candidate % Items.Count + Items.Count) % Items.Count;
        return Math.Clamp(candidate, 0, Items.Count - 1);
    }

    private int CoerceIndex(int value)
    {
        if (Items.Count == 0)
            return -1;
        return Math.Clamp(value, -1, Items.Count - 1);
    }

    private static void OnItemsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var selector = (LoopingSelector)dependencyObject;
        if (selector.SelectedIndex >= selector.Items.Count)
            selector.SelectedIndex = selector.Items.Count - 1;
        selector.InvalidateMeasure();
        selector.Invalidate();
    }

    private static void OnSelectedIndexChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var selector = (LoopingSelector)dependencyObject;
        if (selector._synchronizingSelection)
            return;

        var index = selector.CoerceIndex((int)(args.NewValue ?? -1));
        if (index != (int)(args.NewValue ?? -1))
        {
            selector.SetValue(SelectedIndexProperty, index);
            return;
        }

        selector._synchronizingSelection = true;
        try
        {
            selector.SelectedItem = index >= 0 ? selector.Items[index] : null;
        }
        finally
        {
            selector._synchronizingSelection = false;
        }
        selector.SelectionChanged?.Invoke(selector, EventArgs.Empty);
        selector.Invalidate();
    }

    private static void OnSelectedItemChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var selector = (LoopingSelector)dependencyObject;
        if (selector._synchronizingSelection)
            return;

        selector._synchronizingSelection = true;
        try
        {
            selector.SelectedIndex = args.NewValue == null ? -1 : selector.Items.IndexOf(args.NewValue);
        }
        finally
        {
            selector._synchronizingSelection = false;
        }
        selector.SelectionChanged?.Invoke(selector, EventArgs.Empty);
        selector.Invalidate();
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(LoopingSelector),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsRender = true });
}

/// <summary>
/// Content container realized by <see cref="LoopingSelector"/>.
/// </summary>
public class LoopingSelectorItem : ContentControl
{
}
