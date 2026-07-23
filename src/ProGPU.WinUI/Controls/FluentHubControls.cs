using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Microsoft.UI.Xaml.Markup;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public sealed class HubSectionHeaderClickEventArgs : EventArgs
{
    public HubSectionHeaderClickEventArgs(HubSection section) => Section = section;

    public HubSection Section { get; }
}

/// <summary>
/// Displays a horizontally or vertically panning collection of sections.
/// </summary>
[ContentProperty(Name = nameof(Sections))]
public class Hub : Control
{
    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null);
    public static readonly DependencyProperty OrientationProperty = Register(nameof(Orientation), Orientation.Horizontal);
    public static readonly DependencyProperty DefaultSectionIndexProperty = Register(nameof(DefaultSectionIndex), 0);

    public Hub()
    {
        Sections = new ObservableCollection<HubSection>();
        SectionsInView = new ObservableCollection<HubSection>();
        SectionHeaders = new ObservableCollection<object?>();
        Sections.CollectionChanged += OnSectionsChanged;
    }

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => GetValue(HeaderTemplateProperty) as DataTemplate; set => SetValue(HeaderTemplateProperty, value); }
    public Orientation Orientation { get => (Orientation)(GetValue(OrientationProperty) ?? Orientation.Horizontal); set => SetValue(OrientationProperty, value); }
    public int DefaultSectionIndex { get => (int)(GetValue(DefaultSectionIndexProperty) ?? 0); set => SetValue(DefaultSectionIndexProperty, Math.Max(0, value)); }
    public ObservableCollection<HubSection> Sections { get; }
    public ObservableCollection<HubSection> SectionsInView { get; }
    public ObservableCollection<object?> SectionHeaders { get; }

    public event EventHandler<HubSectionHeaderClickEventArgs>? SectionHeaderClick;
    public event EventHandler<object?>? SectionsInViewChanged;

    public void ScrollToSection(HubSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var index = Sections.IndexOf(section);
        if (index < 0)
            throw new ArgumentException("The section must belong to this Hub.", nameof(section));

        DefaultSectionIndex = index;
        InvalidateArrange();
    }

    internal void RaiseSectionHeaderClick(HubSection section) =>
        SectionHeaderClick?.Invoke(this, new HubSectionHeaderClickEventArgs(section));

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var desired = Vector2.Zero;
        foreach (var section in Sections)
        {
            section.Measure(availableSize);
            if (Orientation == Orientation.Horizontal)
            {
                desired.X += section.DesiredSize.X;
                desired.Y = Math.Max(desired.Y, section.DesiredSize.Y);
            }
            else
            {
                desired.X = Math.Max(desired.X, section.DesiredSize.X);
                desired.Y += section.DesiredSize.Y;
            }
        }

        return desired;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var offset = 0f;
        foreach (var section in Sections)
        {
            if (Orientation == Orientation.Horizontal)
            {
                var width = section.DesiredSize.X;
                section.Arrange(new Rect(arrangeRect.X + offset, arrangeRect.Y, width, arrangeRect.Height));
                offset += width;
            }
            else
            {
                var height = section.DesiredSize.Y;
                section.Arrange(new Rect(arrangeRect.X, arrangeRect.Y + offset, arrangeRect.Width, height));
                offset += height;
            }
        }

        SynchronizeSectionsInView();
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems != null)
        {
            foreach (HubSection section in args.OldItems)
                RemoveChild(section);
        }

        if (args.NewItems != null)
        {
            foreach (HubSection section in args.NewItems)
                AddChild(section);
        }

        SectionHeaders.Clear();
        foreach (var section in Sections)
            SectionHeaders.Add(section.Header);

        InvalidateMeasure();
        Invalidate();
    }

    private void SynchronizeSectionsInView()
    {
        SectionsInView.Clear();
        foreach (var section in Sections)
        {
            if (section.Visibility == Visibility.Visible)
                SectionsInView.Add(section);
        }
        SectionsInViewChanged?.Invoke(this, null);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(Hub),
            new PropertyMetadata(defaultValue) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}

/// <summary>
/// A data-template-backed content section hosted by a <see cref="Hub"/>.
/// </summary>
[ContentProperty(Name = nameof(ContentTemplate))]
public class HubSection : Control
{
    private FrameworkElement? _contentRoot;

    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null, OnSectionPropertyChanged);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null, OnSectionPropertyChanged);
    public static readonly DependencyProperty ContentTemplateProperty = Register<DataTemplate?>(nameof(ContentTemplate), null, OnContentTemplateChanged);
    public static readonly DependencyProperty IsHeaderInteractiveProperty = Register(nameof(IsHeaderInteractive), false, OnSectionPropertyChanged);

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => GetValue(HeaderTemplateProperty) as DataTemplate; set => SetValue(HeaderTemplateProperty, value); }
    public DataTemplate? ContentTemplate { get => GetValue(ContentTemplateProperty) as DataTemplate; set => SetValue(ContentTemplateProperty, value); }
    public bool IsHeaderInteractive { get => (bool)(GetValue(IsHeaderInteractiveProperty) ?? false); set => SetValue(IsHeaderInteractiveProperty, value); }

    public void InvokeHeader()
    {
        if (IsHeaderInteractive && Parent is Hub hub)
            hub.RaiseSectionHeaderClick(this);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _contentRoot?.Measure(availableSize);
        return _contentRoot?.DesiredSize ?? Vector2.Zero;
    }

    protected override void ArrangeOverride(Rect arrangeRect) => _contentRoot?.Arrange(arrangeRect);

    private static void OnContentTemplateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var section = (HubSection)dependencyObject;
        if (section._contentRoot != null)
        {
            XamlTemplateFactory.ReleaseSubtree(section._contentRoot);
            section.RemoveChild(section._contentRoot);
        }

        section._contentRoot = args.NewValue is DataTemplate template
            ? XamlTemplateFactory.Build(template, section)
            : null;

        if (section._contentRoot != null)
        {
            section._contentRoot.DataContext = section.DataContext;
            section.AddChild(section._contentRoot);
        }

        section.InvalidateMeasure();
        section.Invalidate();
    }

    private static void OnSectionPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var section = (HubSection)dependencyObject;
        section.InvalidateMeasure();
        section.Invalidate();
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback callback) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(HubSection),
            new PropertyMetadata(defaultValue, callback)
            {
                AffectsMeasure = true,
                AffectsArrange = true,
                AffectsRender = true
            });
}
