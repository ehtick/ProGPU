using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using Microsoft.UI.Xaml.Media.Animation;

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = "Content")]
public class ContentControl : Control
{
    private FrameworkElement? _contentTemplateRoot;
    private FrameworkElement? _presentedContent;

    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(
            nameof(ContentTemplate),
            typeof(DataTemplate),
            typeof(ContentControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(ContentTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(ContentControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public static readonly DependencyProperty ContentTransitionsProperty =
        DependencyProperty.Register(
            nameof(ContentTransitions),
            typeof(TransitionCollection),
            typeof(ContentControl),
            new PropertyMetadata(null) { AffectsRender = true });

    public DataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty) as DataTemplate;
        set => SetValue(ContentTemplateProperty, value);
    }

    public DataTemplateSelector? ContentTemplateSelector
    {
        get => GetValue(ContentTemplateSelectorProperty) as DataTemplateSelector;
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    public TransitionCollection? ContentTransitions
    {
        get => GetValue(ContentTransitionsProperty) as TransitionCollection;
        set => SetValue(ContentTransitionsProperty, value);
    }

    public FrameworkElement? ContentTemplateRoot => _contentTemplateRoot;

    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(
            "Content",
            typeof(object),
            typeof(ContentControl),
            new PropertyMetadata(null, OnContentChanged));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ContentControl)d;
        control.OnContentChanged(e.OldValue, e.NewValue);
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ContentControl)d;
        control.OnContentChanged(control.Content, control.Content);
    }

    protected virtual void OnContentChanged(object? oldValue, object? newValue)
    {
        XamlTemplateFactory.ReleaseSubtree(_presentedContent);
        if (_presentedContent != null)
            RemoveChild(_presentedContent);

        _presentedContent = null;
        _contentTemplateRoot = null;

        var selectedTemplate = ContentTemplateSelector?.SelectTemplate(newValue, this) ?? ContentTemplate;
        if (selectedTemplate != null)
        {
            _contentTemplateRoot = XamlTemplateFactory.Build(selectedTemplate, newValue);
            _presentedContent = _contentTemplateRoot;
            if (_presentedContent != null)
            {
                _presentedContent.DataContext = newValue;
                AddChild(_presentedContent);
            }
        }
        else if (newValue != null)
        {
            if (newValue is FrameworkElement newFe)
            {
                _presentedContent = newFe;
            }
            else
            {
                // Auto-wrap non-FrameworkElement content in a RichTextBlock
                var tb = new RichTextBlock { TextWrapping = TextWrapping.NoWrap };
                tb.Inlines.Add(new Run { Text = newValue.ToString() ?? string.Empty });
                _presentedContent = tb;
            }

            AddChild(_presentedContent);
        }
        
        Invalidate();
        InvalidateMeasure();
    }

    protected FrameworkElement? ContentVisual
    {
        get
        {
            return _presentedContent;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (HasTemplate)
        {
            return base.MeasureOverride(availableSize);
        }

        var inset = BorderThickness + Padding;
        var insetSize = new Vector2(inset.Horizontal, inset.Vertical);
        var contentAvailable = availableSize;
        if (!float.IsInfinity(contentAvailable.X)) contentAvailable.X = Math.Max(0f, contentAvailable.X - insetSize.X);
        if (!float.IsInfinity(contentAvailable.Y)) contentAvailable.Y = Math.Max(0f, contentAvailable.Y - insetSize.Y);

        var contentFe = ContentVisual;
        if (contentFe != null)
        {
            contentFe.Measure(contentAvailable);
            return contentFe.DesiredSize + insetSize;
        }

        return insetSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (HasTemplate)
        {
            base.ArrangeOverride(arrangeRect);
            return;
        }

        var inset = BorderThickness + Padding;
        var childRect = new Rect(
            arrangeRect.X + inset.Left,
            arrangeRect.Y + inset.Top,
            Math.Max(0f, arrangeRect.Width - inset.Horizontal),
            Math.Max(0f, arrangeRect.Height - inset.Vertical));

        var contentFe = ContentVisual;
        if (contentFe != null)
        {
            contentFe.Arrange(childRect);
        }
    }
}
