using System;
using System.Collections;
using System.Numerics;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = nameof(Items))]
public class ItemsControl : Control
{
    private IList? _indexedItemsSource;
    private Panel? _itemsHost;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ItemsControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(ItemsControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ItemTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(ItemsControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ItemsPanelProperty = DependencyProperty.Register(
        nameof(ItemsPanel), typeof(ItemsPanelTemplate), typeof(ItemsControl),
        new PropertyMetadata(null, OnItemsPanelTemplateChanged) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(ItemsControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ItemContainerStyleProperty = DependencyProperty.Register(
        nameof(ItemContainerStyle), typeof(Style), typeof(ItemsControl), new PropertyMetadata(null));

    public static readonly DependencyProperty ItemContainerTransitionsProperty = DependencyProperty.Register(
        nameof(ItemContainerTransitions), typeof(TransitionCollection), typeof(ItemsControl),
        new PropertyMetadata(null) { AffectsRender = true });

    public ItemsControl()
    {
        Items = new ItemCollection(this);
        ItemsHost = new StackPanel();
        ItemContainerTransitions = new TransitionCollection();

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null) Style = defaultStyle;
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty) as IEnumerable;
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty) as DataTemplate;
        set => SetValue(ItemTemplateProperty, value);
    }

    public DataTemplateSelector? ItemTemplateSelector
    {
        get => GetValue(ItemTemplateSelectorProperty) as DataTemplateSelector;
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    public ItemsPanelTemplate? ItemsPanel
    {
        get => GetValue(ItemsPanelProperty) as ItemsPanelTemplate;
        set => SetValue(ItemsPanelProperty, value);
    }

    public string DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty) as string ?? string.Empty;
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public Style? ItemContainerStyle
    {
        get => GetValue(ItemContainerStyleProperty) as Style;
        set => SetValue(ItemContainerStyleProperty, value);
    }

    public TransitionCollection ItemContainerTransitions
    {
        get => (TransitionCollection)GetValue(ItemContainerTransitionsProperty)!;
        set => SetValue(ItemContainerTransitionsProperty, value);
    }

    public ItemCollection Items { get; }
    public Panel? ItemsPanelRoot => _itemsHost;

    /// <summary>
    /// ProGPU's realized panel extension. WinUI's public <see cref="ItemsPanel"/> remains
    /// an <see cref="ItemsPanelTemplate"/>; this member supplies a live optimized host.
    /// </summary>
    public Panel? ItemsHost
    {
        get => _itemsHost;
        set
        {
            if (ReferenceEquals(_itemsHost, value)) return;
            DetachItemsHost(_itemsHost);
            _itemsHost = value;
            AttachItemsHost(value);
            RefreshItems();
        }
    }

    /// <summary>ProGPU's allocation-bounded item-container factory.</summary>
    public Func<Visual>? ItemVisualFactory { get; set; }

    /// <summary>ProGPU's in-place realized-container binding callback.</summary>
    public Action<Visual, object, int>? BindVisualCallback { get; set; }

    public int ItemCount => _indexedItemsSource?.Count ?? Items.Count;

    public object? GetItemAt(int index)
    {
        if ((uint)index >= (uint)ItemCount) return null;
        return _indexedItemsSource != null ? _indexedItemsSource[index] : Items[index];
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AttachItemsHost(_itemsHost);
    }

    internal void OnItemsCollectionChanged()
    {
        if (_indexedItemsSource != null) return;
        RefreshItems();
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (ItemsControl)dependencyObject;
        control._indexedItemsSource = args.NewValue as IList;
        control.Items.ReplaceWith(control._indexedItemsSource == null ? args.NewValue as IEnumerable : null);
        control.RefreshItems();
    }

    private static void OnItemsPanelTemplateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (ItemsControl)dependencyObject;
        if (args.NewValue is not ItemsPanelTemplate template) return;
        if (Microsoft.UI.Xaml.Markup.XamlTemplateFactory.Build(template, control) is Panel panel)
            control.ItemsHost = panel;
    }

    private void AttachItemsHost(Panel? panel)
    {
        if (panel == null) return;
        if (HasTemplate && GetTemplateChild("ScrollViewer") is ScrollViewer scrollViewer)
        {
            if (!ReferenceEquals(scrollViewer.Content, panel)) scrollViewer.Content = panel;
        }
        else if (!ReferenceEquals(panel.Parent, this))
        {
            AddChild(panel);
        }
    }

    private void DetachItemsHost(Panel? panel)
    {
        if (panel == null) return;
        if (HasTemplate && GetTemplateChild("ScrollViewer") is ScrollViewer scrollViewer)
        {
            if (ReferenceEquals(scrollViewer.Content, panel)) scrollViewer.Content = null;
        }
        else if (ReferenceEquals(panel.Parent, this))
        {
            RemoveChild(panel);
        }
        Microsoft.UI.Xaml.Markup.XamlTemplateFactory.ReleaseSubtree(panel);
    }

    public void RefreshItems()
    {
        var panel = _itemsHost;
        if (panel == null) return;

        if (panel is VirtualizingPanel virtualizingPanel)
        {
            virtualizingPanel.ForceRebind();
        }
        else
        {
            panel.Children.Clear();
            if (ItemVisualFactory != null)
            {
                for (var index = 0; index < ItemCount; index++)
                {
                    var container = ItemVisualFactory();
                    BindVisualCallback?.Invoke(container, GetItemAt(index)!, index);
                    panel.Children.Add(container);
                }
            }
        }
        InvalidateMeasure();
        Invalidate();
    }

    public void RefreshVisibleItems()
    {
        if (_itemsHost is VirtualizingPanel virtualizingPanel) virtualizingPanel.RebindVisibleItems();
        else RefreshItems();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (HasTemplate) return base.MeasureOverride(availableSize);
        if (_itemsHost == null) return Vector2.Zero;
        _itemsHost.Measure(availableSize);
        return _itemsHost.DesiredSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            return;
        }
        _itemsHost?.Arrange(arrangeRect);
    }
}
