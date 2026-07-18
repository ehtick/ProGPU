using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ProGPU.Backend;
using ProGPU.Text;
using ProGPU.Vector;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls;

public class TextVisual : FrameworkElement, ITextLayoutProvider, IViewportDeferredWarmable
{
    private const int MaximumPendingDeferredWarmups = 16;
    private const int MaximumConcurrentDeferredWarmups = 2;
    private static readonly SemaphoreSlim s_deferredWarmupConcurrency =
        new(MaximumConcurrentDeferredWarmups, MaximumConcurrentDeferredWarmups);
    private static int s_deferredWarmupsInFlight;
    private string _text = string.Empty;
    private float _fontSize = 14f;
    private TextAlignment _alignment = TextAlignment.Left;
    private TextLayout? _layout;
    private TextShapingOptions _textShapingOptions = TextShapingOptions.Default;
    private bool _deferLayoutUntilRender;
    private int _layoutRevision;
    private int _deferredWarmupState;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                InvalidateLayoutCache();
                Invalidate();
            }
        }
    }

    protected override void OnPropertyChanged(Microsoft.UI.Xaml.DependencyProperty dp, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(dp, oldValue, newValue);
        if (dp == FontProperty)
        {
            InvalidateLayoutCache();
            Invalidate();
        }
    }

    public float FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize != value)
            {
                _fontSize = value;
                InvalidateLayoutCache();
                Invalidate();
            }
        }
    }

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            "Brush",
            typeof(Brush),
            typeof(TextVisual),
            new PropertyMetadata(null, (d, e) => ((TextVisual)d).Invalidate()));

    public Brush? Brush
    {
        get => GetValue(BrushProperty) as Brush;
        set => SetValue(BrushProperty, value);
    }

    public TextAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (_alignment != value)
            {
                _alignment = value;
                InvalidateLayoutCache();
                Invalidate();
            }
        }
    }

    public TextShapingOptions TextShapingOptions
    {
        get => _textShapingOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_textShapingOptions.Equals(value))
            {
                _textShapingOptions = value;
                InvalidateLayoutCache();
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Defers shaping until the visual first enters a compiled viewport. This is intended
    /// for fixed-height retained specimens whose parent supplies a finite width.
    /// </summary>
    public bool DeferLayoutUntilRender
    {
        get => _deferLayoutUntilRender;
        set
        {
            if (_deferLayoutUntilRender != value)
            {
                _deferLayoutUntilRender = value;
                InvalidateLayoutCache();
                InvalidateMeasure();
                Invalidate();
            }
        }
    }

    private TtfFont? ResolveFont()
    {
        return Font ?? PopupService.DefaultFont;
    }

    public override Rect? LocalRenderBounds
    {
        get
        {
            float padding = MathF.Max(FontSize * 2f, Size.Y);
            return new Rect(-padding, -padding, Size.X + padding * 2f, Size.Y + padding * 2f);
        }
    }

    public override bool IsViewportDeferred => DeferLayoutUntilRender;

    /// <summary>Reports whether CPU shaping has produced a reusable positioned layout.</summary>
    public bool HasPreparedLayout => Volatile.Read(ref _layout) != null;

    public TextLayout? GetOrUpdateLayout(GlyphAtlas atlas)
    {
        var resolvedFont = ResolveFont();
        if (resolvedFont == null) return null;

        float maxWidth = Size.X;
        if (_layout == null || !HasCompatibleLayoutWidth(_layout, maxWidth))
        {
            _layout = new TextLayout(Text, resolvedFont, FontSize, maxWidth, Alignment, atlas, TextShapingOptions);
        }
        else if (!_layout.HasTextures)
        {
            _layout.GenerateLayout(atlas);
        }
        return _layout;
    }

    /// <summary>
    /// Shapes a deferred retained layout without allocating atlas textures. Returns false
    /// until layout has supplied a finite width.
    /// </summary>
    public bool WarmDeferredLayout()
    {
        if (_layout != null)
        {
            return true;
        }

        var resolvedFont = ResolveFont();
        float maxWidth = Size.X;
        if (string.IsNullOrEmpty(Text) || resolvedFont == null)
        {
            return true;
        }
        if (!float.IsFinite(maxWidth) || maxWidth <= 0f)
        {
            return false;
        }

        _layout = new TextLayout(Text, resolvedFont, FontSize, maxWidth, Alignment, null, TextShapingOptions);
        return true;
    }

    /// <summary>
    /// Queues bounded CPU-only shaping for the next viewport ring. The immutable layout is
    /// published through the UI dispatcher; atlas generation and drawing remain on the render
    /// thread. Returns false when already prepared, already queued, invalid, or globally throttled.
    /// </summary>
    public bool QueueViewportWarmup()
    {
        if (!DeferLayoutUntilRender || Volatile.Read(ref _layout) != null)
        {
            return false;
        }

        TtfFont? font = ResolveFont();
        string text = Text;
        float maxWidth = Size.X;
        if (string.IsNullOrEmpty(text) || font == null ||
            !float.IsFinite(maxWidth) || maxWidth <= 0f)
        {
            return false;
        }
        if (Interlocked.CompareExchange(ref _deferredWarmupState, 1, 0) != 0)
        {
            return false;
        }
        if (Interlocked.Increment(ref s_deferredWarmupsInFlight) > MaximumPendingDeferredWarmups)
        {
            Interlocked.Decrement(ref s_deferredWarmupsInFlight);
            Volatile.Write(ref _deferredWarmupState, 0);
            return false;
        }

        int revision = Volatile.Read(ref _layoutRevision);
        float fontSize = FontSize;
        TextAlignment alignment = Alignment;
        TextShapingOptions shapingOptions = TextShapingOptions;
        _ = Task.Run(async () =>
        {
            TextLayout? prepared = null;
            await s_deferredWarmupConcurrency.WaitAsync().ConfigureAwait(false);
            try
            {
                prepared = new TextLayout(
                    text,
                    font,
                    fontSize,
                    maxWidth,
                    alignment,
                    atlas: null,
                    shapingOptions: shapingOptions);
            }
            catch
            {
                // The foreground layout path remains authoritative and will surface the same
                // failure with its normal diagnostics if preparation cannot be completed.
            }
            finally
            {
                s_deferredWarmupConcurrency.Release();
                Interlocked.Decrement(ref s_deferredWarmupsInFlight);
            }

            void Publish()
            {
                if (prepared != null &&
                    revision == Volatile.Read(ref _layoutRevision) &&
                    Volatile.Read(ref _layout) == null)
                {
                    Volatile.Write(ref _layout, prepared);
                }
                Volatile.Write(ref _deferredWarmupState, 0);
            }

            var dispatcher = Microsoft.UI.Xaml.Input.InputSystem.DispatcherQueue;
            if (dispatcher != null)
            {
                try
                {
                    dispatcher(Publish);
                }
                catch
                {
                    Volatile.Write(ref _deferredWarmupState, 0);
                }
            }
            else
            {
                Publish();
            }
        });
        return true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var resolvedFont = ResolveFont();
        if (string.IsNullOrEmpty(Text) || resolvedFont == null)
            return Vector2.Zero;

        float maxWidth = WidthConstraint ?? availableSize.X;
        if (DeferLayoutUntilRender &&
            HeightConstraint.HasValue &&
            float.IsFinite(maxWidth) &&
            maxWidth >= 0f)
        {
            return new Vector2(maxWidth, HeightConstraint.Value);
        }

        if (_layout == null || !HasCompatibleLayoutWidth(_layout, maxWidth))
        {
            _layout = new TextLayout(Text, resolvedFont, FontSize, maxWidth, Alignment, null, TextShapingOptions);
        }
        return _layout.MeasuredSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
        float maxWidth = arrangeRect.Width;
        if (_layout != null && !HasCompatibleLayoutWidth(_layout, maxWidth))
        {
            InvalidateLayoutCache();
        }
    }

    private void InvalidateLayoutCache()
    {
        Interlocked.Increment(ref _layoutRevision);
        Volatile.Write(ref _layout, null);
    }

    private bool HasCompatibleLayoutWidth(TextLayout layout, float requestedWidth)
    {
        float existingWidth = layout.MaxWidth;
        if (existingWidth.Equals(requestedWidth)) return true;
        return Alignment == TextAlignment.Left &&
               float.IsPositiveInfinity(existingWidth) &&
               requestedWidth >= layout.ContentSize.X;
    }

    public override void OnRender(DrawingContext context)
    {
        var resolvedFont = ResolveFont();
        if (string.IsNullOrEmpty(Text) || resolvedFont == null) return;
        
        var resolvedBrush = Brush ?? ThemeManager.GetBrush("TextPrimary");

        // Add single drawing run command; compositor will dynamically compile coordinates
        context.Commands.Add(new RenderCommand
        {
            Type = RenderCommandType.DrawText,
            Text = Text,
            Font = resolvedFont,
            FontSize = FontSize,
            Brush = resolvedBrush,
            Position = Vector2.Zero,
            Rect = new Rect(Vector2.Zero, Size),
            TextShapingOptions = TextShapingOptions
        });
    }
}
