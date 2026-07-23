using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.Text;

namespace Microsoft.UI.Xaml.Controls;

[ContentProperty(Name = nameof(Content))]
public class ContentPresenter : FrameworkElement
{
    private RichTextBlock? _generatedText;
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(
            nameof(ContentTemplate),
            typeof(DataTemplate),
            typeof(ContentPresenter),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public DataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty) as DataTemplate;
        set => SetValue(ContentTemplateProperty, value);
    }

    public static readonly DependencyProperty ContentTransitionsProperty =
        DependencyProperty.Register(
            nameof(ContentTransitions),
            typeof(TransitionCollection),
            typeof(ContentPresenter),
            new PropertyMetadata(null) { AffectsRender = true });

    public TransitionCollection? ContentTransitions
    {
        get => GetValue(ContentTransitionsProperty) as TransitionCollection;
        set => SetValue(ContentTransitionsProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground),
            typeof(Brush),
            typeof(ContentPresenter),
            new PropertyMetadata(null, OnTypographyChanged) { AffectsRender = true });

    public Brush? Foreground
    {
        get => GetValue(ForegroundProperty) as Brush;
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(
            nameof(FontFamily),
            typeof(FontFamily),
            typeof(ContentPresenter),
            new PropertyMetadata(FontFamily.XamlAutoFontFamily, OnTypographyChanged) { AffectsMeasure = true, AffectsRender = true });

    public FontFamily FontFamily
    {
        get => (FontFamily)(GetValue(FontFamilyProperty) ?? FontFamily.XamlAutoFontFamily);
        set => SetValue(FontFamilyProperty, value);
    }

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(
            nameof(FontWeight),
            typeof(FontWeight),
            typeof(ContentPresenter),
            new PropertyMetadata(Microsoft.UI.Text.FontWeights.Normal, OnTypographyChanged) { AffectsMeasure = true, AffectsRender = true });

    public FontWeight FontWeight
    {
        get => (FontWeight)(GetValue(FontWeightProperty) ?? Microsoft.UI.Text.FontWeights.Normal);
        set => SetValue(FontWeightProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(ContentPresenter),
        new PropertyMetadata(14d, OnTypographyChanged) { AffectsMeasure = true, AffectsRender = true });

    public double FontSize
    {
        get => (double)(GetValue(FontSizeProperty) ?? 14d);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly DependencyProperty MaxLinesProperty =
        DependencyProperty.Register(
            nameof(MaxLines),
            typeof(int),
            typeof(ContentPresenter),
            new PropertyMetadata(0, OnGeneratedTextLayoutChanged)
            {
                AffectsMeasure = true,
                AffectsArrange = true,
                AffectsRender = true
            });

    public int MaxLines
    {
        get => (int)(GetValue(MaxLinesProperty) ?? 0);
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxLines cannot be negative.");
            }
            SetValue(MaxLinesProperty, value);
        }
    }

    public static readonly DependencyProperty OpticalMarginAlignmentProperty =
        DependencyProperty.Register(
            nameof(OpticalMarginAlignment),
            typeof(OpticalMarginAlignment),
            typeof(ContentPresenter),
            new PropertyMetadata(OpticalMarginAlignment.None, OnGeneratedTextLayoutChanged)
            {
                AffectsMeasure = true,
                AffectsRender = true
            });

    public OpticalMarginAlignment OpticalMarginAlignment
    {
        get => (OpticalMarginAlignment)(GetValue(OpticalMarginAlignmentProperty) ?? OpticalMarginAlignment.None);
        set => SetValue(OpticalMarginAlignmentProperty, value);
    }

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(
            "Background",
            typeof(Brush),
            typeof(ContentPresenter),
            new PropertyMetadata(null) { AffectsRender = true });

    public Brush? Background
    {
        get => GetValue(BackgroundProperty) as Brush;
        set => SetValue(BackgroundProperty, value);
    }

    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(
            "BorderBrush",
            typeof(Brush),
            typeof(ContentPresenter),
            new PropertyMetadata(null) { AffectsRender = true });

    public Brush? BorderBrush
    {
        get => GetValue(BorderBrushProperty) as Brush;
        set => SetValue(BorderBrushProperty, value);
    }

    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(
            "BorderThickness",
            typeof(Thickness),
            typeof(ContentPresenter),
            new PropertyMetadata(default(Thickness)) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public Thickness BorderThickness
    {
        get => (Thickness)(GetValue(BorderThicknessProperty) ?? default(Thickness));
        set => SetValue(BorderThicknessProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            "CornerRadius",
            typeof(CornerRadius),
            typeof(ContentPresenter),
            new PropertyMetadata(default(CornerRadius)) { AffectsRender = true });

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)(GetValue(CornerRadiusProperty) ?? default(CornerRadius));
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty BackgroundSizingProperty = DependencyProperty.Register(
        nameof(BackgroundSizing),
        typeof(BackgroundSizing),
        typeof(ContentPresenter),
        new PropertyMetadata(BackgroundSizing.InnerBorderEdge) { AffectsRender = true });

    public BackgroundSizing BackgroundSizing
    {
        get => (BackgroundSizing)(GetValue(BackgroundSizingProperty) ?? BackgroundSizing.InnerBorderEdge);
        set => SetValue(BackgroundSizingProperty, value);
    }

    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(
            "Content",
            typeof(object),
            typeof(ContentPresenter),
            new PropertyMetadata(null, OnContentChanged));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cp = (ContentPresenter)d;
        cp.OnContentChanged(e.OldValue, e.NewValue);
    }

    protected virtual void OnContentChanged(object? oldValue, object? newValue)
    {
        if (oldValue is FrameworkElement oldFe)
        {
            RemoveChild(oldFe);
        }
        else if (_generatedText != null)
        {
            RemoveChild(_generatedText);
            _generatedText = null;
        }

        if (newValue != null)
        {
            if (newValue is FrameworkElement newFe)
            {
                AddChild(newFe);
            }
            else
            {
                // Auto-wrap non-FrameworkElement content in a RichTextBlock
                _generatedText = new RichTextBlock
                {
                    TextWrapping = TextWrapping,
                    FontFamily = FontFamily,
                    FontWeight = FontWeight,
                    FontSize = (float)FontSize,
                    Foreground = Foreground,
                    MaxLines = MaxLines,
                    OpticalMarginAlignment = OpticalMarginAlignment
                };
                _generatedText.Inlines.Add(new Run { Text = newValue.ToString() ?? string.Empty });
                AddChild(_generatedText);
            }
        }
    }

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(TextWrapping),
            typeof(ContentPresenter),
            new PropertyMetadata(TextWrapping.NoWrap, OnTextWrappingChanged) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)(GetValue(TextWrappingProperty) ?? TextWrapping.NoWrap);
        set => SetValue(TextWrappingProperty, value);
    }

    private static void OnTextWrappingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (ContentPresenter)d;
        if (presenter._generatedText != null)
        {
            presenter._generatedText.TextWrapping = (TextWrapping)(e.NewValue ?? TextWrapping.NoWrap);
        }
    }

    private static void OnTypographyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ContentPresenter)dependencyObject;
        if (presenter._generatedText == null) return;
        presenter._generatedText.FontFamily = presenter.FontFamily;
        presenter._generatedText.FontWeight = presenter.FontWeight;
        presenter._generatedText.FontSize = (float)presenter.FontSize;
        presenter._generatedText.Foreground = presenter.Foreground;
    }

    private static void OnGeneratedTextLayoutChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        var presenter = (ContentPresenter)dependencyObject;
        if (presenter._generatedText == null)
        {
            return;
        }

        presenter._generatedText.MaxLines = presenter.MaxLines;
        presenter._generatedText.OpticalMarginAlignment = presenter.OpticalMarginAlignment;
    }

    public static readonly DependencyProperty HorizontalContentAlignmentProperty =
        DependencyProperty.Register(
            "HorizontalContentAlignment",
            typeof(HorizontalAlignment),
            typeof(ContentPresenter),
            new PropertyMetadata(HorizontalAlignment.Stretch) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public HorizontalAlignment HorizontalContentAlignment
    {
        get => (HorizontalAlignment)(GetValue(HorizontalContentAlignmentProperty) ?? HorizontalAlignment.Stretch);
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    public static readonly DependencyProperty VerticalContentAlignmentProperty =
        DependencyProperty.Register(
            "VerticalContentAlignment",
            typeof(VerticalAlignment),
            typeof(ContentPresenter),
            new PropertyMetadata(VerticalAlignment.Stretch) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public VerticalAlignment VerticalContentAlignment
    {
        get => (VerticalAlignment)(GetValue(VerticalContentAlignmentProperty) ?? VerticalAlignment.Stretch);
        set => SetValue(VerticalContentAlignmentProperty, value);
    }

    protected FrameworkElement? ContentVisual
    {
        get
        {
            if (Content is FrameworkElement fe) return fe;
            foreach (var child in Children)
            {
                if (child is FrameworkElement childFe) return childFe;
            }
            return null;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float borderH = BorderThickness.Horizontal;
        float borderV = BorderThickness.Vertical;
        float paddingH = Padding.Horizontal;
        float paddingV = Padding.Vertical;

        Vector2 inset = new Vector2(borderH + paddingH, borderV + paddingV);
        Vector2 contentAvail = new Vector2(
            Math.Max(0f, availableSize.X - inset.X),
            Math.Max(0f, availableSize.Y - inset.Y)
        );

        Vector2 contentDesired = Vector2.Zero;
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            contentVisual.Measure(contentAvail);
            contentDesired = contentVisual.DesiredSize;
        }

        // Return desired size with ONLY BorderThickness. LayoutNode automatically adds Padding!
        return contentDesired + new Vector2(borderH, borderV);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var contentVisual = ContentVisual;
        if (contentVisual != null)
        {
            // Only apply BorderThickness insets. LayoutNode already applied Padding to arrangeRect!
            float leftInset = BorderThickness.Left;
            float topInset = BorderThickness.Top;
            float rightInset = BorderThickness.Right;
            float bottomInset = BorderThickness.Bottom;

            float innerW = Math.Max(0f, arrangeRect.Width - (leftInset + rightInset));
            float innerH = Math.Max(0f, arrangeRect.Height - (topInset + bottomInset));

            var horizAlign = HorizontalContentAlignment;
            var vertAlign = VerticalContentAlignment;

            float childW = contentVisual.DesiredSize.X;
            float childH = contentVisual.DesiredSize.Y;

            if (horizAlign == HorizontalAlignment.Stretch)
            {
                childW = innerW;
            }
            else
            {
                childW = Math.Min(innerW, childW);
            }

            if (vertAlign == VerticalAlignment.Stretch)
            {
                childH = innerH;
            }
            else
            {
                childH = Math.Min(innerH, childH);
            }

            float childX = arrangeRect.X + leftInset;
            if (horizAlign == HorizontalAlignment.Center)
            {
                childX += (innerW - childW) / 2f;
            }
            else if (horizAlign == HorizontalAlignment.Right)
            {
                childX += (innerW - childW);
            }

            float childY = arrangeRect.Y + topInset;
            if (vertAlign == VerticalAlignment.Center)
            {
                childY += (innerH - childH) / 2f;
            }
            else if (vertAlign == VerticalAlignment.Bottom)
            {
                childY += (innerH - childH);
            }

            contentVisual.Arrange(new Rect(childX, childY, childW, childH));
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (Background != null || (BorderBrush != null && BorderThickness.Left > 0))
        {
            var pen = BorderBrush != null && BorderThickness.Left > 0 ? new Pen(BorderBrush, BorderThickness.Left) : null;
            context.DrawRoundedRectangle(Background, pen, new Rect(Vector2.Zero, Size), (float)CornerRadius.TopLeft);
        }
        base.OnRender(context);
    }
}
