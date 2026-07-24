using System;
using System.Numerics;
using Microsoft.UI.Xaml.Media.Animation;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>Presents an items host with optional header and footer content.</summary>
public sealed class ItemsPresenter : FrameworkElement
{
    private readonly ContentPresenter _headerPresenter = new();
    private readonly ContentPresenter _footerPresenter = new();

    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null, OnContentChanged);
    public static readonly DependencyProperty HeaderTemplateProperty = Register<DataTemplate?>(nameof(HeaderTemplate), null, OnContentChanged);
    public static readonly DependencyProperty HeaderTransitionsProperty = Register<TransitionCollection?>(nameof(HeaderTransitions), null);
    public static readonly DependencyProperty FooterProperty = Register<object?>(nameof(Footer), null, OnContentChanged);
    public static readonly DependencyProperty FooterTemplateProperty = Register<DataTemplate?>(nameof(FooterTemplate), null, OnContentChanged);
    public static readonly DependencyProperty FooterTransitionsProperty = Register<TransitionCollection?>(nameof(FooterTransitions), null);

    public ItemsPresenter()
    {
        AddChild(_headerPresenter);
        AddChild(_footerPresenter);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public DataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty) as DataTemplate;
        set => SetValue(HeaderTemplateProperty, value);
    }

    public TransitionCollection? HeaderTransitions
    {
        get => GetValue(HeaderTransitionsProperty) as TransitionCollection;
        set => SetValue(HeaderTransitionsProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public DataTemplate? FooterTemplate
    {
        get => GetValue(FooterTemplateProperty) as DataTemplate;
        set => SetValue(FooterTemplateProperty, value);
    }

    public TransitionCollection? FooterTransitions
    {
        get => GetValue(FooterTransitionsProperty) as TransitionCollection;
        set => SetValue(FooterTransitionsProperty, value);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        SynchronizePresenters();
        var inner = new Vector2(
            Math.Max(0f, availableSize.X - Padding.Horizontal),
            Math.Max(0f, availableSize.Y - Padding.Vertical));
        _headerPresenter.Measure(inner);
        _footerPresenter.Measure(inner);
        return new Vector2(
            Math.Max(_headerPresenter.DesiredSize.X, _footerPresenter.DesiredSize.X) + Padding.Horizontal,
            _headerPresenter.DesiredSize.Y + _footerPresenter.DesiredSize.Y + Padding.Vertical);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var x = arrangeRect.X + Padding.Left;
        var y = arrangeRect.Y + Padding.Top;
        var width = Math.Max(0f, arrangeRect.Width - Padding.Horizontal);
        _headerPresenter.Arrange(new Rect(x, y, width, _headerPresenter.DesiredSize.Y));
        y += _headerPresenter.DesiredSize.Y;
        _footerPresenter.Arrange(new Rect(x, y, width, _footerPresenter.DesiredSize.Y));
    }

    private void SynchronizePresenters()
    {
        _headerPresenter.Content = Header;
        _headerPresenter.ContentTemplate = HeaderTemplate;
        _headerPresenter.ContentTransitions = HeaderTransitions;
        _footerPresenter.Content = Footer;
        _footerPresenter.ContentTemplate = FooterTemplate;
        _footerPresenter.ContentTransitions = FooterTransitions;
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(name, typeof(T), typeof(ItemsPresenter),
            new PropertyMetadata(defaultValue, callback)
            {
                AffectsMeasure = true,
                AffectsArrange = true,
                AffectsRender = true
            });

    private static void OnContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((ItemsPresenter)dependencyObject).SynchronizePresenters();
}
