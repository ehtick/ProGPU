using Thickness = Microsoft.UI.Xaml.Thickness;
using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using StackPanel = Microsoft.UI.Xaml.Controls.StackPanel;
using Grid = Microsoft.UI.Xaml.Controls.Grid;

namespace ProGPU.Samples;

public static class FontGlyphBrowserPage
{
    private sealed class GlyphBrowserItem : Border, IRetainedVirtualizedItemFragment
    {
        private static readonly DependencyProperty SelectedBorderBrushProperty = DependencyProperty.Register(
            "SelectedBorderBrush",
            typeof(Brush),
            typeof(GlyphBrowserItem),
            new PropertyMetadata(null));
        private static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.Register(
            "HoverBorderBrush",
            typeof(Brush),
            typeof(GlyphBrowserItem),
            new PropertyMetadata(null));
        private static readonly DependencyProperty HoverBackgroundBrushProperty = DependencyProperty.Register(
            "HoverBackgroundBrush",
            typeof(Brush),
            typeof(GlyphBrowserItem),
            new PropertyMetadata(null));
        private static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
            "TextBrush",
            typeof(Brush),
            typeof(GlyphBrowserItem),
            new PropertyMetadata(null));
        private static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            "AccentBrush",
            typeof(Brush),
            typeof(GlyphBrowserItem),
            new PropertyMetadata(null));
        private readonly GpuPictureRecorder _recorder = new();
        private readonly ushort[] _iconGlyph = new ushort[1];
        private readonly Vector2[] _iconPosition = new Vector2[1];
        private ushort[] _indexGlyphs = [];
        private Vector2[] _indexPositions = [];
        private ushort[] _hexGlyphs = [];
        private Vector2[] _hexPositions = [];
        private TtfFont? _font;
        private Rect _retainedBounds;
        private bool _fragmentDirty = true;
        private bool _hasGlyphCommand;
        private int _boundGlyphIndex = -1;
        private bool _isSelected;
        private bool _isHovered;
        private Pen? _defaultBorderPen;
        private Pen? _hoverBorderPen;
        private Pen? _selectedBorderPen;

        public GlyphBrowserItem()
        {
            CornerRadius = 6f;
            Padding = new Thickness(6);
            Background = new ThemeResourceBrush("PageBackground");
            BorderBrush = new ThemeResourceBrush("ControlBorder");
            SetValue(SelectedBorderBrushProperty, new ThemeResourceBrush("SystemAccentColor"));
            SetValue(HoverBorderBrushProperty, new ThemeResourceBrush("ControlBorderHover"));
            SetValue(HoverBackgroundBrushProperty, new ThemeResourceBrush("ControlBackgroundHover"));
            SetValue(TextBrushProperty, new ThemeResourceBrush("TextPrimary"));
            SetValue(AccentBrushProperty, new ThemeResourceBrush("SystemAccentColor"));
            BorderThickness = new Thickness(1f);
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            WidthConstraint = 84f;
            HeightConstraint = 92f;
            RetainedFragment = new SceneFragmentHandle(CreateEmptyPicture());
            RebuildThemePens();

            PointerPressed += OnItemClick;
            PointerEntered += OnItemHover;
            PointerExited += OnItemLeave;
        }

        public void Bind(TtfFont? font, int index, bool isSelected)
        {
            if (!ReferenceEquals(_font, font))
            {
                _font = font;
                _fragmentDirty = true;
            }

            if (_boundGlyphIndex != index)
            {
                _boundGlyphIndex = index;
                Tag = index;
                _fragmentDirty = true;
            }

            SetSelected(isSelected);
        }

        public void SetHovered(bool isHovered)
        {
            if (_isHovered == isHovered)
            {
                return;
            }

            _isHovered = isHovered;
            UpdateChrome();
            if (UpdateRetainedFragment(_retainedBounds))
            {
                InvalidateRetainedTransform();
            }
        }

        public int BoundGlyphIndex => Tag is int index ? index : -1;

        public bool EmitsGlyphCommand()
        {
            return _hasGlyphCommand;
        }

        private void SetSelected(bool isSelected)
        {
            if (_isSelected == isSelected)
            {
                return;
            }

            _isSelected = isSelected;
            UpdateChrome();
        }

        private void UpdateChrome()
        {
            _fragmentDirty = true;
        }

        public SceneFragmentHandle RetainedFragment { get; }

        public bool UpdateRetainedFragment(Rect bounds)
        {
            if (!_fragmentDirty && _retainedBounds == bounds)
            {
                return false;
            }

            _retainedBounds = bounds;
            _fragmentDirty = false;
            var font = _font;
            var labelFont = PopupService.DefaultFont ?? font;
            var context = _recorder.BeginRecording(bounds);
            var card = new Rect(bounds.X + 4f, bounds.Y + 4f, 84f, 92f);
            Brush textBrush = GetValue(TextBrushProperty) as Brush
                ?? Background
                ?? throw new InvalidOperationException("The glyph item text brush resource is unavailable.");
            Brush accentBrush = GetValue(AccentBrushProperty) as Brush ?? textBrush;
            Brush background = _isSelected || _isHovered
                ? GetValue(HoverBackgroundBrushProperty) as Brush ?? Background ?? textBrush
                : Background ?? textBrush;
            Pen borderPen = _isSelected
                ? _selectedBorderPen!
                : _isHovered ? _hoverBorderPen! : _defaultBorderPen!;
            context.DrawRoundedRectangle(
                background,
                borderPen,
                card,
                6f);

            _hasGlyphCommand = false;
            if (font != null && _boundGlyphIndex >= 0)
            {
                ushort glyph = (ushort)_boundGlyphIndex;
                _iconGlyph[0] = glyph;
                float iconSize = 36f;
                float iconWidth = 40f;
                float iconX = card.X + (card.Width - iconWidth) * 0.5f;
                float unitsPerEm = font.UnitsPerEm > 0 ? font.UnitsPerEm : 2048f;
                float advance = font.GetAdvanceWidth(glyph, iconSize);
                float baseline = card.Y + 6f + font.Ascender * (iconSize / unitsPerEm);
                context.DrawGlyphRun(
                    _iconGlyph,
                    _iconPosition,
                    font,
                    iconSize,
                    textBrush,
                    new Vector2(iconX + (iconWidth - advance) * 0.5f, baseline),
                    preferGlyphAtlas: true,
                    useLogicalGlyphAtlasResolution: false);
                _hasGlyphCommand = true;
            }

            if (labelFont != null && _boundGlyphIndex >= 0)
            {
                const float labelSize = 9f;
                float firstWidth = BuildIndexGlyphRun(labelFont, labelSize, _boundGlyphIndex);
                float secondWidth = BuildHexGlyphRun(labelFont, labelSize, _boundGlyphIndex);
                float labelUnits = labelFont.UnitsPerEm > 0 ? labelFont.UnitsPerEm : 2048f;
                float labelAscent = labelFont.Ascender * (labelSize / labelUnits);
                context.DrawGlyphRun(
                    _indexGlyphs,
                    _indexPositions,
                    labelFont,
                    labelSize,
                    textBrush,
                    new Vector2(card.X + (card.Width - firstWidth) * 0.5f, card.Y + 57f + labelAscent),
                    preferGlyphAtlas: true);
                context.DrawGlyphRun(
                    _hexGlyphs,
                    _hexPositions,
                    labelFont,
                    labelSize,
                    accentBrush,
                    new Vector2(card.X + (card.Width - secondWidth) * 0.5f, card.Y + 70f + labelAscent),
                    isBold: true,
                    preferGlyphAtlas: true);
            }

            RetainedFragment.ReplacePicture(_recorder.EndRecording());
            return true;
        }

        public override void OnRender(DrawingContext context)
        {
            // The owning virtualizing panel records RetainedFragment in stable slot order.
        }

        protected override void OnThemeChanged()
        {
            _fragmentDirty = true;
            RebuildThemePens();
            if (_retainedBounds.Width > 0f && _retainedBounds.Height > 0f)
            {
                UpdateRetainedFragment(_retainedBounds);
            }
            base.OnThemeChanged();
        }

        private void RebuildThemePens()
        {
            Brush fallback = GetValue(TextBrushProperty) as Brush
                ?? Background
                ?? throw new InvalidOperationException("The glyph item theme resources are unavailable.");
            _defaultBorderPen = new Pen(BorderBrush ?? fallback, 1f);
            _hoverBorderPen = new Pen(GetValue(HoverBorderBrushProperty) as Brush ?? fallback, 1f);
            _selectedBorderPen = new Pen(GetValue(SelectedBorderBrushProperty) as Brush ?? fallback, 1.5f);
        }

        private float BuildIndexGlyphRun(TtfFont font, float size, int value)
        {
            int digits = CountDecimalDigits(value);
            int length = 5 + digits;
            EnsureGlyphRunCapacity(ref _indexGlyphs, ref _indexPositions, length);
            int cursor = 0;
            cursor = AppendGlyph(font, size, _indexGlyphs, _indexPositions, cursor, 'I');
            cursor = AppendGlyph(font, size, _indexGlyphs, _indexPositions, cursor, 'd');
            cursor = AppendGlyph(font, size, _indexGlyphs, _indexPositions, cursor, 'x');
            cursor = AppendGlyph(font, size, _indexGlyphs, _indexPositions, cursor, ':');
            cursor = AppendGlyph(font, size, _indexGlyphs, _indexPositions, cursor, ' ');
            int divisor = Pow10(digits - 1);
            while (divisor > 0)
            {
                cursor = AppendGlyph(
                    font,
                    size,
                    _indexGlyphs,
                    _indexPositions,
                    cursor,
                    (char)('0' + value / divisor % 10));
                divisor /= 10;
            }
            return GetGlyphRunWidth(font, size, _indexGlyphs, _indexPositions);
        }

        private float BuildHexGlyphRun(TtfFont font, float size, int value)
        {
            int digits = Math.Max(3, CountHexDigits(value));
            int length = 2 + digits;
            EnsureGlyphRunCapacity(ref _hexGlyphs, ref _hexPositions, length);
            int cursor = 0;
            cursor = AppendGlyph(font, size, _hexGlyphs, _hexPositions, cursor, '0');
            cursor = AppendGlyph(font, size, _hexGlyphs, _hexPositions, cursor, 'x');
            for (int shift = (digits - 1) * 4; shift >= 0; shift -= 4)
            {
                int digit = value >> shift & 0xF;
                cursor = AppendGlyph(
                    font,
                    size,
                    _hexGlyphs,
                    _hexPositions,
                    cursor,
                    (char)(digit < 10 ? '0' + digit : 'A' + digit - 10));
            }
            return GetGlyphRunWidth(font, size, _hexGlyphs, _hexPositions);
        }

        private static int AppendGlyph(
            TtfFont font,
            float size,
            ushort[] glyphs,
            Vector2[] positions,
            int cursor,
            char character)
        {
            ushort glyph = font.GetGlyphIndex(character);
            glyphs[cursor] = glyph;
            positions[cursor] = cursor == 0
                ? Vector2.Zero
                : new Vector2(
                    positions[cursor - 1].X + font.GetAdvanceWidth(glyphs[cursor - 1], size),
                    0f);
            return cursor + 1;
        }

        private static float GetGlyphRunWidth(
            TtfFont font,
            float size,
            ushort[] glyphs,
            Vector2[] positions) => glyphs.Length == 0
                ? 0f
                : positions[^1].X + font.GetAdvanceWidth(glyphs[^1], size);

        private static void EnsureGlyphRunCapacity(
            ref ushort[] glyphs,
            ref Vector2[] positions,
            int length)
        {
            if (glyphs.Length == length)
            {
                return;
            }
            glyphs = new ushort[length];
            positions = new Vector2[length];
        }

        private static int CountDecimalDigits(int value) => value switch
        {
            >= 10000 => 5,
            >= 1000 => 4,
            >= 100 => 3,
            >= 10 => 2,
            _ => 1
        };

        private static int CountHexDigits(int value) => value switch
        {
            >= 0x10000 => 5,
            >= 0x1000 => 4,
            >= 0x100 => 3,
            >= 0x10 => 2,
            _ => 1
        };

        private static int Pow10(int exponent) => exponent switch
        {
            4 => 10000,
            3 => 1000,
            2 => 100,
            1 => 10,
            _ => 1
        };

        private static GpuPicture CreateEmptyPicture()
        {
            var recorder = new GpuPictureRecorder();
            recorder.BeginRecording(new Rect(0f, 0f, 1f, 1f));
            return recorder.EndRecording();
        }
    }

    private sealed class GlyphIndexList : IList
    {
        public GlyphIndexList(int count)
        {
            Count = count;
        }

        public int Count { get; }
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public object? this[int index]
        {
            get => index >= 0 && index < Count ? (ushort)index : throw new ArgumentOutOfRangeException(nameof(index));
            set => throw new NotSupportedException();
        }

        public bool Contains(object? value) => value is ushort glyph && glyph < Count;
        public int IndexOf(object? value) => value is ushort glyph && glyph < Count ? glyph : -1;
        public void CopyTo(Array array, int index)
        {
            for (var glyph = 0; glyph < Count; glyph++)
            {
                array.SetValue((ushort)glyph, index + glyph);
            }
        }

        public IEnumerator GetEnumerator()
        {
            for (var glyph = 0; glyph < Count; glyph++)
            {
                yield return (ushort)glyph;
            }
        }

        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    private static TtfFont? _selectedFont;
    private static List<FontInfo> _systemFonts = new();
    private static ushort _selectedGlyphIndex = 0;

    // UI references for live metrics updates
    private static RichTextBlock? _unitsPerEmText;
    private static RichTextBlock? _totalGlyphsText;
    private static RichTextBlock? _ascenderText;
    private static RichTextBlock? _descenderText;
    private static RichTextBlock? _lineGapText;

    // UI references for glyph details inspector
    private static RichTextBlock? _detailIndexText;
    private static RichTextBlock? _detailHexText;
    private static Border? _detailColorsBorder;
    private static RichTextBlock? _detailColorsText;
    private static RichTextBlock? _detailWidthText;

    private static FontIcon? _largeGlyphPreview;
    private static ItemsControl? _itemsControl;
    private static UniformVirtualizingGridPanel? _virtualGrid;
    private static TextBox? _pathInput;
    private static RichTextBlock? _pathStatus;
    private static float _benchmarkScrollDirection = 1f;

    internal static void AdvanceBenchmarkScroll(float step)
    {
        if (_virtualGrid == null)
        {
            return;
        }

        float maxOffset = Math.Max(0f, _virtualGrid.TotalVirtualHeight - _virtualGrid.ViewportHeight);
        if (maxOffset <= 0f)
        {
            return;
        }

        float nextOffset = _virtualGrid.ScrollOffset + _benchmarkScrollDirection * step;
        if (nextOffset >= maxOffset)
        {
            nextOffset = maxOffset;
            _benchmarkScrollDirection = -1f;
        }
        else if (nextOffset <= 0f)
        {
            nextOffset = 0f;
            _benchmarkScrollDirection = 1f;
        }

        _virtualGrid.ScrollOffset = nextOffset;
    }

    internal static bool TryGetBenchmarkGlyphState(
        out int realizedItems,
        out int glyphCommandItems,
        out int minimumGlyphIndex,
        out int maximumGlyphIndex)
    {
        realizedItems = 0;
        glyphCommandItems = 0;
        minimumGlyphIndex = int.MaxValue;
        maximumGlyphIndex = -1;
        if (_virtualGrid == null)
        {
            return false;
        }

        foreach (var child in _virtualGrid.Children)
        {
            if (child is not GlyphBrowserItem item || item.BoundGlyphIndex < 0)
            {
                continue;
            }

            realizedItems++;
            minimumGlyphIndex = Math.Min(minimumGlyphIndex, item.BoundGlyphIndex);
            maximumGlyphIndex = Math.Max(maximumGlyphIndex, item.BoundGlyphIndex);
            if (item.EmitsGlyphCommand())
            {
                glyphCommandItems++;
            }
        }

        return realizedItems > 0;
    }

    public static FrameworkElement Create()
    {
        _benchmarkScrollDirection = 1f;
        // 1. Initial State Font Load
        _selectedFont = AppState._font ?? PopupService.DefaultFont;
        _systemFonts = new List<FontInfo>();

        // 2. Main Page Layout Root
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        mainGrid.ColumnDefinitions.Add(new GridLength(1.4f, GridUnitType.Star)); // Left Pane: Controls + Grid
        mainGrid.ColumnDefinitions.Add(new GridLength(20f, GridUnitType.Absolute)); // Spacer
        mainGrid.ColumnDefinitions.Add(new GridLength(0.8f, GridUnitType.Star)); // Right Pane: Large Preview

        // Left Container
        var leftStack = new Grid
        {
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Auto)); // Row 0: Title
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Auto)); // Row 1: Description
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Auto)); // Row 2: Controls
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Auto)); // Row 3: Path status
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Auto)); // Row 4: Metadata (Metrics Dashboard)
        leftStack.RowDefinitions.Add(new GridLength(1f, GridUnitType.Star)); // Row 5: Glyph grid!
        mainGrid.AddChild(leftStack);
        Grid.SetColumn(leftStack, 0);

        // Right Container Card
        var previewCard = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 12f,
            Padding = new Thickness(24),
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        mainGrid.AddChild(previewCard);
        Grid.SetColumn(previewCard, 2);

        var previewStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        previewCard.Child = previewStack;

        // Build Right Preview Details
        var previewTitle = new RichTextBlock { Margin = new Thickness(0, 0, 0, 16) };
        previewTitle.Inlines.Add(new Bold(new Run("Glyph High-DPI Outline Preview") { FontSize = 16f, Foreground = new ThemeResourceBrush("SystemAccentColor") }));
        previewStack.AddChild(previewTitle);

        // Vector Designer/Typographic grid backdrop workspace
        var previewBox = new TypographicPreviewBox
        {
            Background = new ThemeResourceBrush("PageBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Height = 240f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        
        _largeGlyphPreview = new FontIcon
        {
            Font = _selectedFont,
            GlyphIndex = _selectedGlyphIndex,
            FontSize = 160f,
            UseVectorGlyphRendering = true,
            WidthConstraint = 200f,
            HeightConstraint = 200f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        previewBox.Child = _largeGlyphPreview;
        previewStack.AddChild(previewBox);

        // structured details card
        var detailsCard = new Border
        {
            Background = new ThemeResourceBrush("PageBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 0)
        };

        var detailsStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        detailsCard.Child = detailsStack;
        previewStack.AddChild(detailsCard);

        Grid createDetailRow(string labelText, FrameworkElement valueElement)
        {
            var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, HeightConstraint = 28f, Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
            row.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));

            var lbl = new RichTextBlock { VerticalAlignment = VerticalAlignment.Center };
            lbl.Inlines.Add(new Bold(new Run(labelText) { FontSize = 11.5f, Foreground = new ThemeResourceBrush("TextSecondary") }));
            row.AddChild(lbl);
            Grid.SetColumn(lbl, 0);

            valueElement.HorizontalAlignment = HorizontalAlignment.Right;
            valueElement.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(valueElement);
            Grid.SetColumn(valueElement, 1);

            return row;
        }

        _detailIndexText = new RichTextBlock();
        detailsStack.AddChild(createDetailRow("Glyph Index", _detailIndexText));

        _detailHexText = new RichTextBlock();
        detailsStack.AddChild(createDetailRow("Glyph ID (hex)", _detailHexText));

        _detailColorsText = new RichTextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _detailColorsBorder = new Border
        {
            CornerRadius = 4f,
            Padding = new Thickness(8, 2, 8, 2),
            Child = _detailColorsText
        };
        detailsStack.AddChild(createDetailRow("Has Color Layers", _detailColorsBorder));

        _detailWidthText = new RichTextBlock();
        detailsStack.AddChild(createDetailRow("Advance Width (em 100)", _detailWidthText));

        // Build Left Pane: Page title
        var title = new RichTextBlock { Margin = new Thickness(0, 0, 0, 6) };
        title.Inlines.Add(new Bold(new Run("TrueType Font Glyph Inspector") { FontSize = 24f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        leftStack.AddChild(title);
        Grid.SetRow(title, 0);

        var desc = new RichTextBlock { Margin = new Thickness(0, 0, 0, 20) };
        desc.Inlines.Add(new Run("High-performance vector typography inspector. Browse millions of raw glyph contours smoothly via multi-column viewport virtualization backed by our zero-allocation GPGPU compute renderer.") { FontSize = 13f, Foreground = new ThemeResourceBrush("TextSecondary") });
        leftStack.AddChild(desc);
        Grid.SetRow(desc, 1);

        // Font Selectors (Combobox + Load file)
        var controlsRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 16) };
        controlsRow.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));       // ComboBox
        controlsRow.ColumnDefinitions.Add(new GridLength(12f, GridUnitType.Absolute));  // Spacer
        controlsRow.ColumnDefinitions.Add(new GridLength(1.2f, GridUnitType.Star));     // Path Selector + Button

        // ComboBox Selector
        var fontSelectorStack = new StackPanel { Orientation = Orientation.Vertical };
        var selectorLabel = new RichTextBlock { Margin = new Thickness(0, 0, 0, 4) };
        selectorLabel.Inlines.Add(new Bold(new Run("SYSTEM FONTS") { FontSize = 11f, Foreground = new ThemeResourceBrush("TextSecondary") }));
        fontSelectorStack.AddChild(selectorLabel);

        var fontCombo = new ComboBox
        {
            PlaceholderText = "Select system font...",
            WidthConstraint = 260f,
            HeightConstraint = 32f
        };
        
        var fontItemsLoaded = false;
        fontCombo.DropDownOpening += (s, e) =>
        {
            if (fontItemsLoaded)
            {
                return;
            }

            fontItemsLoaded = true;
            try
            {
                _systemFonts = FontApi.GetSystemFonts();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FontGlyphBrowserPage] System font scan error: {ex.Message}");
                _systemFonts = new List<FontInfo>();
            }
            foreach (var fontInfo in _systemFonts)
            {
                fontCombo.Items.Add(new ComboBoxItem { Text = fontInfo.Name, Tag = fontInfo });
            }
        };

        fontCombo.SelectionChanged += (s, e) =>
        {
            if (fontCombo.SelectedItem?.Tag is FontInfo info)
            {
                LoadFontFile(info.FilePath);
            }
        };
        fontSelectorStack.AddChild(fontCombo);
        controlsRow.AddChild(fontSelectorStack);
        Grid.SetColumn(fontSelectorStack, 0);

        // Path Selector
        var pathStack = new StackPanel { Orientation = Orientation.Vertical };
        var pathLabel = new RichTextBlock { Margin = new Thickness(0, 0, 0, 4) };
        pathLabel.Inlines.Add(new Bold(new Run("LOAD CUSTOM TTF / TTC FILE PATH") { FontSize = 11f, Foreground = new ThemeResourceBrush("TextSecondary") }));
        pathStack.AddChild(pathLabel);

        var pathGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        pathGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
        pathGrid.ColumnDefinitions.Add(new GridLength(8f, GridUnitType.Absolute));
        pathGrid.ColumnDefinitions.Add(new GridLength(80f, GridUnitType.Absolute));

        _pathInput = new TextBox
        {
            PlaceholderText = "Enter absolute TTF path...",
            HeightConstraint = 32f,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        pathGrid.AddChild(_pathInput);
        Grid.SetColumn(_pathInput, 0);

        var loadBtn = new Button
        {
            HeightConstraint = 32f,
            CornerRadius = 4f,
            Background = new ThemeResourceBrush("SystemAccentColor")
        };
        var btnRun = new Run("Load") { FontSize = 12f, Foreground = new ThemeResourceBrush("TextOnAccent") };
        loadBtn.Content = new RichTextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Inlines = { new Bold(btnRun) } };
        loadBtn.Click += (s, e) =>
        {
            string p = _pathInput.Text ?? string.Empty;
            if (File.Exists(p))
            {
                LoadFontFile(p);
            }
            else
            {
                _pathStatus!.Inlines.Clear();
                _pathStatus.Inlines.Add(new Run("File not found.") { Foreground = new SolidColorBrush(new Vector4(1f, 0.3f, 0.3f, 1f)) });
                _pathStatus.Invalidate();
            }
        };
        pathGrid.AddChild(loadBtn);
        Grid.SetColumn(loadBtn, 2);
        pathStack.AddChild(pathGrid);
        controlsRow.AddChild(pathStack);
        Grid.SetColumn(pathStack, 2);
        leftStack.AddChild(controlsRow);
        Grid.SetRow(controlsRow, 2);

        // Path status readout
        _pathStatus = new RichTextBlock { Margin = new Thickness(0, 0, 0, 12), FontSize = 11f };
        leftStack.AddChild(_pathStatus);
        Grid.SetRow(_pathStatus, 3);

        // Font metadata readout card (Dashboard row of metric tiles)
        var metaCard = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var metaGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        metaGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
        metaGrid.ColumnDefinitions.Add(new GridLength(8f, GridUnitType.Absolute));
        metaGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
        metaGrid.ColumnDefinitions.Add(new GridLength(8f, GridUnitType.Absolute));
        metaGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
        metaGrid.ColumnDefinitions.Add(new GridLength(8f, GridUnitType.Absolute));
        metaGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));
        metaGrid.ColumnDefinitions.Add(new GridLength(8f, GridUnitType.Absolute));
        metaGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));

        Border createMetricCard(string labelText, out RichTextBlock valueBlock, bool isAccent = false)
        {
            var card = new Border
            {
                Background = new ThemeResourceBrush("ControlBackground"),
                BorderBrush = new ThemeResourceBrush("ControlBorder"),
                BorderThickness = new Thickness(1f),
                CornerRadius = 6f,
                Padding = new Thickness(10, 8, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var labelBlock = new RichTextBlock { Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            labelBlock.Inlines.Add(new Bold(new Run(labelText.ToUpper()) { FontSize = 8.5f, Foreground = new ThemeResourceBrush("TextSecondary") }));
            valueBlock = new RichTextBlock { HorizontalAlignment = HorizontalAlignment.Center };
            var valueRun = new Run("0") { FontSize = 16f };
            if (isAccent)
            {
                valueRun.Foreground = new ThemeResourceBrush("SystemAccentColor");
                valueBlock.Inlines.Add(new Bold(valueRun));
            }
            else
            {
                valueRun.Foreground = new ThemeResourceBrush("TextPrimary");
                valueBlock.Inlines.Add(new Bold(valueRun));
            }
            stack.AddChild(labelBlock);
            stack.AddChild(valueBlock);
            card.Child = stack;
            return card;
        }

        var unitsCard = createMetricCard("Units Per Em", out _unitsPerEmText);
        metaGrid.AddChild(unitsCard);
        Grid.SetColumn(unitsCard, 0);

        var glyphsCard = createMetricCard("Total Glyphs", out _totalGlyphsText, isAccent: true);
        metaGrid.AddChild(glyphsCard);
        Grid.SetColumn(glyphsCard, 2);

        var ascenderCard = createMetricCard("Ascender", out _ascenderText);
        metaGrid.AddChild(ascenderCard);
        Grid.SetColumn(ascenderCard, 4);

        var descenderCard = createMetricCard("Descender", out _descenderText);
        metaGrid.AddChild(descenderCard);
        Grid.SetColumn(descenderCard, 6);

        var lineGapCard = createMetricCard("Line Gap", out _lineGapText);
        metaGrid.AddChild(lineGapCard);
        Grid.SetColumn(lineGapCard, 8);

        metaCard.Child = metaGrid;
        leftStack.AddChild(metaCard);
        Grid.SetRow(metaCard, 4);

        // 3. Setup the ItemsControl with UniformVirtualizingGridPanel ItemsPanel
        var gridBorder = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _itemsControl = new ItemsControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _virtualGrid = new UniformVirtualizingGridPanel
        {
            ItemWidth = 92f,
            ItemHeight = 100f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _itemsControl.ItemsPanel = _virtualGrid;

        // Wire virtualized recycling delegates on ItemsControl
        _itemsControl.ItemTemplate = static () => new GlyphBrowserItem();

        _itemsControl.BindVisualCallback = (vis, itemObj, idx) =>
        {
            ((GlyphBrowserItem)vis).Bind(_selectedFont, idx, idx == _selectedGlyphIndex);
        };

        gridBorder.Child = _itemsControl;
        leftStack.AddChild(gridBorder);
        Grid.SetRow(gridBorder, 5);

        // Update labels
        UpdateSelectedFontDetails();
        UpdateSelectedGlyph(_selectedGlyphIndex);

        return mainGrid;
    }

    private static void OnItemHover(object? sender, PointerRoutedEventArgs e)
    {
        (sender as GlyphBrowserItem)?.SetHovered(true);
    }

    private static void OnItemLeave(object? sender, PointerRoutedEventArgs e)
    {
        (sender as GlyphBrowserItem)?.SetHovered(false);
    }

    private static void OnItemClick(object? sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is int idx)
        {
            UpdateSelectedGlyph((ushort)idx);
            
            // Re-render only active border items to refresh selected outline highlights
            _itemsControl?.RefreshVisibleItems();
        }
    }

    private static void LoadFontFile(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            var loaded = new TtfFont(path);
            _selectedFont = loaded;
            _selectedGlyphIndex = 0;

            if (_pathInput != null)
            {
                _pathInput.Text = path;
                _pathInput.Invalidate();
            }

            if (_pathStatus != null)
            {
                _pathStatus.Inlines.Clear();
                _pathStatus.Inlines.Add(new Run("Successfully loaded font: ") { Foreground = new ThemeResourceBrush("SystemGreenAccent") });
                _pathStatus.Inlines.Add(new Bold(new Run(Path.GetFileName(path))));
                _pathStatus.Invalidate();
            }

            UpdateSelectedFontDetails();
            UpdateSelectedGlyph(0);

            if (_virtualGrid != null)
            {
                _virtualGrid.ScrollOffset = 0f;
            }
        }
        catch (Exception ex)
        {
            if (_pathStatus != null)
            {
                _pathStatus.Inlines.Clear();
                _pathStatus.Inlines.Add(new Run($"Error parsing TTF: {ex.Message}") { Foreground = new SolidColorBrush(new Vector4(1f, 0.3f, 0.3f, 1f)) });
                _pathStatus.Invalidate();
            }
        }
    }

    private static void UpdateSelectedFontDetails()
    {
        if (_selectedFont == null) return;

        if (_unitsPerEmText != null)
        {
            _unitsPerEmText.Inlines.Clear();
            _unitsPerEmText.Inlines.Add(new Bold(new Run(_selectedFont.UnitsPerEm.ToString()) { FontSize = 16f, Foreground = new ThemeResourceBrush("TextPrimary") }));
            _unitsPerEmText.Invalidate();
        }

        if (_totalGlyphsText != null)
        {
            _totalGlyphsText.Inlines.Clear();
            _totalGlyphsText.Inlines.Add(new Bold(new Run(_selectedFont.NumGlyphs.ToString()) { FontSize = 16f, Foreground = new ThemeResourceBrush("SystemAccentColor") }));
            _totalGlyphsText.Invalidate();
        }

        if (_ascenderText != null)
        {
            _ascenderText.Inlines.Clear();
            _ascenderText.Inlines.Add(new Bold(new Run(_selectedFont.Ascender.ToString()) { FontSize = 16f, Foreground = new ThemeResourceBrush("TextPrimary") }));
            _ascenderText.Invalidate();
        }

        if (_descenderText != null)
        {
            _descenderText.Inlines.Clear();
            _descenderText.Inlines.Add(new Bold(new Run(_selectedFont.Descender.ToString()) { FontSize = 16f, Foreground = new ThemeResourceBrush("TextPrimary") }));
            _descenderText.Invalidate();
        }

        if (_lineGapText != null)
        {
            _lineGapText.Inlines.Clear();
            _lineGapText.Inlines.Add(new Bold(new Run(_selectedFont.LineGap.ToString()) { FontSize = 16f, Foreground = new ThemeResourceBrush("TextPrimary") }));
            _lineGapText.Invalidate();
        }

        if (_itemsControl != null)
        {
            _itemsControl.ItemsSource = new GlyphIndexList(_selectedFont.NumGlyphs);
        }
    }

    private static void UpdateSelectedGlyph(ushort index)
    {
        _selectedGlyphIndex = index;

        if (_largeGlyphPreview != null)
        {
            _largeGlyphPreview.Font = _selectedFont;
            _largeGlyphPreview.GlyphIndex = _selectedGlyphIndex;
            _largeGlyphPreview.Invalidate();
        }

        if (_selectedFont != null)
        {
            if (_detailIndexText != null)
            {
                _detailIndexText.Inlines.Clear();
                _detailIndexText.Inlines.Add(new Bold(new Run($"#{index}") { FontSize = 12f, Foreground = new ThemeResourceBrush("TextPrimary") }));
                _detailIndexText.Invalidate();
            }

            if (_detailHexText != null)
            {
                _detailHexText.Inlines.Clear();
                _detailHexText.Inlines.Add(new Bold(new Run($"0x{index:X3}") { FontSize = 12f, Foreground = new ThemeResourceBrush("SystemAccentColor") }));
                _detailHexText.Invalidate();
            }

            if (_detailColorsBorder != null && _detailColorsText != null)
            {
                bool hasColors = _selectedFont.HasColorLayers(index);
                _detailColorsText.Inlines.Clear();
                if (hasColors)
                {
                    _detailColorsBorder.Background = new SolidColorBrush(new Vector4(0.188f, 0.82f, 0.345f, 0.15f));
                    _detailColorsBorder.BorderBrush = new SolidColorBrush(new Vector4(0.188f, 0.82f, 0.345f, 0.3f));
                    _detailColorsBorder.BorderThickness = new Thickness(0.5f);
                    _detailColorsText.Inlines.Add(new Bold(new Run("YES") { FontSize = 10f, Foreground = new ThemeResourceBrush("SystemGreenAccent") }));
                }
                else
                {
                    _detailColorsBorder.Background = new ThemeResourceBrush("ControlBackground");
                    _detailColorsBorder.BorderBrush = new ThemeResourceBrush("ControlBorder");
                    _detailColorsBorder.BorderThickness = new Thickness(0.5f);
                    _detailColorsText.Inlines.Add(new Bold(new Run("NO") { FontSize = 10f, Foreground = new ThemeResourceBrush("TextSecondary") }));
                }
                _detailColorsBorder.Invalidate();
                _detailColorsText.Invalidate();
            }

            if (_detailWidthText != null)
            {
                float advance = _selectedFont.GetAdvanceWidth(index, 100f);
                _detailWidthText.Inlines.Clear();
                _detailWidthText.Inlines.Add(new Bold(new Run($"{advance:F2}px") { FontSize = 12f, Foreground = new ThemeResourceBrush("TextPrimary") }));
                _detailWidthText.Invalidate();
            }
        }
    }
}

/// <summary>
/// A premium typographic designer workspace box that draws a dynamic vector blueprint grid backdrop and baseline coordinate crosshair axes.
/// </summary>
public class TypographicPreviewBox : Border
{
    private readonly ThemeResourceBrush _pageBackgroundBrush = new("PageBackground");
    private readonly Pen _gridPen = new(new ThemeResourceBrush("ControlBorder"), 0.5f);
    private readonly Pen _axisPen = new(new ThemeResourceBrush("SystemAccentColor"), 1f);

    public override void OnRender(DrawingContext context)
    {
        // 1. Draw page background
        var bg = Background ?? _pageBackgroundBrush;
        context.DrawRectangle(bg, null, new Rect(Vector2.Zero, Size));

        // 2. Draw blueprint grid lines (thin, translucent lines)
        float step = 20f;
        for (float y = step; y < Size.Y; y += step)
        {
            context.DrawLine(_gridPen, new Vector2(0, y), new Vector2(Size.X, y));
        }
        for (float x = step; x < Size.X; x += step)
        {
            context.DrawLine(_gridPen, new Vector2(x, 0), new Vector2(x, Size.Y));
        }

        // Draw bold center baseline/midline axes (using theme accent color)
        float centerX = Size.X / 2f;
        float centerY = Size.Y / 2f;
        context.DrawLine(_axisPen, new Vector2(0, centerY), new Vector2(Size.X, centerY));
        context.DrawLine(_axisPen, new Vector2(centerX, 0), new Vector2(centerX, Size.Y));

        // 3. Draw child visual elements (large FontIcon glyph)
        base.OnRender(context);
    }
}
