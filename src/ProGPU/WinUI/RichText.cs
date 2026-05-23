using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using ProGPU.Text;

namespace ProGPU.WinUI;

public abstract class Block
{
    public float MarginBottom { get; set; } = 12f;
}

public abstract class TextElement : Block
{
    public Brush? Foreground { get; set; }
    public float? FontSize { get; set; }
}

public abstract class Inline : TextElement
{
}

public class Run : Inline
{
    public string Text { get; set; } = string.Empty;

    public Run() { }
    public Run(string text) { Text = text; }
}

public class Span : Inline
{
    public List<Inline> Inlines { get; } = new();

    public Span() { }
    public Span(params Inline[] inlines)
    {
        Inlines.AddRange(inlines);
    }
}

public class Bold : Span
{
    public Bold() { }
    public Bold(params Inline[] inlines) : base(inlines) { }
}

public class Italic : Span
{
    public Italic() { }
    public Italic(params Inline[] inlines) : base(inlines) { }
}

public class Underline : Span
{
    public Underline() { }
    public Underline(params Inline[] inlines) : base(inlines) { }
}

public class Hyperlink : Span
{
    public string Uri { get; set; } = string.Empty;

    public event EventHandler<RoutedEventArgs>? Click;

    public Hyperlink() { }
    public Hyperlink(params Inline[] inlines) : base(inlines) { }

    public void RaiseClick()
    {
        Click?.Invoke(this, new RoutedEventArgs { OriginalSource = this });
    }
}

public class InlineUIContainer : Inline
{
    public FrameworkElement? Child { get; set; }

    public InlineUIContainer() { }
    public InlineUIContainer(FrameworkElement child)
    {
        Child = child;
    }
}

public class ListBlock : Inline
{
    public List<ListItem> Items { get; } = new();
    public bool IsOrdered { get; set; } // false: bullet, true: numbered
    public float Indentation { get; set; } = 24f;
}

public class ListItem : Span
{
    public ListItem() { }
    public ListItem(params Inline[] inlines) : base(inlines) { }
}

public class Table : Inline
{
    public List<TableRow> Rows { get; } = new();
    public float CellPadding { get; set; } = 8f;
    public float BorderThickness { get; set; } = 1f;
    public Brush? BorderBrush { get; set; }
    public List<float>? ColumnWidths { get; set; }

    public Table() { }
    public Table(params TableRow[] rows)
    {
        Rows.AddRange(rows);
    }
}

public class TableRow
{
    public List<TableCell> Cells { get; } = new();

    public TableRow() { }
    public TableRow(params TableCell[] cells)
    {
        Cells.AddRange(cells);
    }
}

public class TableCell : Span
{
    public Brush? Background { get; set; }

    public TableCell() { }
    public TableCell(params Inline[] inlines) : base(inlines) { }
    public TableCell(string text) : base(new Run(text)) { }
}

public class TableVisualDecoration
{
    public Rect Rect;
    public Brush? Background;
    public float BorderThickness;
    public Brush? BorderBrush;
    public bool IsTop;
    public bool IsLeft;
}

public struct RichChar
{
    public char Character;
    public Brush Foreground;
    public float FontSize;
    public bool IsBold;
    public bool IsItalic;
    public bool IsUnderline;
    public Inline? SourceInline;
    public FrameworkElement? EmbeddedElement;
    public float LeftIndent;    // Bullet list indents
    public float BulletOffset;  // Bullet negative gutter shift
}

public class PositionedRichChar
{
    public RichChar Info;
    public Vector2 Position;
}

public class RichTextBlock : FrameworkElement
{
    private TtfFont? _font;
    private float _fontSize = 14f;
    private TextAlignment _textAlignment = TextAlignment.Left;
    private readonly List<PositionedRichChar> _positionedChars = new();
    private readonly List<TableVisualDecoration> _tableDecorations = new();

    public List<Inline> Inlines { get; } = new();

    public int SelectionStart { get; set; } = -1;
    public int SelectionLength { get; set; } = 0;

    public TtfFont? Font
    {
        get => _font;
        set { if (_font != value) { _font = value; Invalidate(); } }
    }

    public float FontSize
    {
        get => _fontSize;
        set { if (_fontSize != value) { _fontSize = value; Invalidate(); } }
    }

    private Brush? _foreground;
    public Brush? Foreground
    {
        get => _foreground;
        set { if (_foreground != value) { _foreground = value; Invalidate(); } }
    }

    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set { if (_textAlignment != value) { _textAlignment = value; Invalidate(); } }
    }

    public List<PositionedRichChar> PositionedChars => _positionedChars;

    private Hyperlink? _hoveredHyperlink = null;

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsEnabled) return;

        var localPos = InputSystem.GetLocalPosition(this, e.Position);
        Hyperlink? foundLink = null;

        foreach (var pc in _positionedChars)
        {
            if (pc.Info.SourceInline is Hyperlink hl && Font != null)
            {
                ushort gIdx = Font.GetGlyphIndex(pc.Info.Character);
                float advance = Font.GetAdvanceWidth(gIdx, pc.Info.FontSize);
                Rect charRect = new Rect(pc.Position.X, pc.Position.Y, advance, pc.Info.FontSize);
                if (charRect.Contains(localPos))
                {
                    foundLink = hl;
                    break;
                }
            }
        }

        if (_hoveredHyperlink != foundLink)
        {
            _hoveredHyperlink = foundLink;
            Invalidate();
        }
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsEnabled && _hoveredHyperlink != null)
        {
            _hoveredHyperlink.RaiseClick();
            e.Handled = true;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Font == null || Inlines.Count == 0) return Vector2.Zero;

        float maxW = WidthConstraint ?? availableSize.X;
        if (float.IsInfinity(maxW)) maxW = 800f; // reasonable fallback bound

        PerformRichLayout(maxW);

        float measuredH = 0f;
        float measuredW = 0f;
        foreach (var pc in _positionedChars)
        {
            float adv = 0f;
            if (pc.Info.EmbeddedElement != null)
            {
                adv = pc.Info.EmbeddedElement.DesiredSize.X + 4f;
            }
            else
            {
                ushort idx = Font.GetGlyphIndex(pc.Info.Character);
                adv = Font.GetAdvanceWidth(idx, pc.Info.FontSize);
            }
            measuredW = Math.Max(measuredW, pc.Position.X + adv);
            measuredH = Math.Max(measuredH, pc.Position.Y + pc.Info.FontSize);
        }

        return new Vector2(measuredW, measuredH + 4f);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
        PerformRichLayout(arrangeRect.Width);

        // Arrange nested child controls
        foreach (var pc in _positionedChars)
        {
            if (pc.Info.EmbeddedElement != null)
            {
                var child = pc.Info.EmbeddedElement;
                child.Arrange(new Rect(pc.Position.X, pc.Position.Y, child.DesiredSize.X, child.DesiredSize.Y));
            }
        }
    }

    public void PerformRichLayout(float maxWidth)
    {
        _positionedChars.Clear();
        _tableDecorations.Clear();
        if (Font == null) return;

        var charList = new List<RichChar>();
        var defaultFg = Foreground ?? new SolidColorBrush(0xFFFFFFFF);

        var currentChildren = new List<Visual>(Children);
        var encounteredChildren = new HashSet<Visual>();

        foreach (var inline in Inlines)
        {
            AccumulateInlines(inline, charList, defaultFg, FontSize, false, false, false, null, 0f);
        }

        if (charList.Count == 0) return;

        // Structured paragraph word wrapping
        float scale = FontSize / Font.UnitsPerEm;
        float lineSpacing = (Font.Ascender - Font.Descender + Font.LineGap) * scale;

        float cursorX = Padding.Left;
        float cursorY = Padding.Top;

        var lines = new List<List<PositionedRichChar>>();
        var currentLine = new List<PositionedRichChar>();
        int lastWordStart = -1;
        float lastWordStartCursorX = Padding.Left;

        float availableWidth = maxWidth - Padding.Horizontal;
        bool hasResetLineIndent = false;

        void CommitLines(List<List<PositionedRichChar>> linesToCommit, float availW)
        {
            for (int l = 0; l < linesToCommit.Count; l++)
            {
                var line = linesToCommit[l];
                if (line.Count == 0) continue;

                float lineW = 0f;
                var lastPc = line[^1];
                float lastAdv = 0f;
                if (lastPc.Info.EmbeddedElement != null)
                {
                    lastAdv = lastPc.Info.EmbeddedElement.DesiredSize.X + 4f;
                }
                else
                {
                    lastAdv = Font.GetAdvanceWidth(Font.GetGlyphIndex(lastPc.Info.Character), lastPc.Info.FontSize);
                }
                lineW = lastPc.Position.X + lastAdv - Padding.Left;

                float shiftX = 0f;
                if (TextAlignment == TextAlignment.Right)
                {
                    shiftX = availW - lineW;
                }
                else if (TextAlignment == TextAlignment.Center)
                {
                    shiftX = (availW - lineW) / 2f;
                }
                else if (TextAlignment == TextAlignment.Justify)
                {
                    bool isLastLine = (l == linesToCommit.Count - 1);
                    int spaceCount = 0;
                    for (int k = 0; k < line.Count - 1; k++)
                    {
                        if (line[k].Info.Character == ' ' || line[k].Info.Character == '\t')
                            spaceCount++;
                    }

                    if (!isLastLine && spaceCount > 0 && lineW < availW)
                    {
                        float extraW = availW - lineW;
                        float spaceAddition = extraW / spaceCount;
                        float runningAddition = 0f;
                        for (int k = 0; k < line.Count; k++)
                        {
                            var pc = line[k];
                            pc.Position.X += runningAddition;
                            if (pc.Info.Character == ' ' || pc.Info.Character == '\t')
                            {
                                runningAddition += spaceAddition;
                            }
                        }
                    }
                }

                if (shiftX > 0f && !float.IsInfinity(shiftX))
                {
                    foreach (var pc in line)
                    {
                        pc.Position.X += shiftX;
                    }
                }

                _positionedChars.AddRange(line);
            }
            linesToCommit.Clear();
        }

        for (int i = 0; i < charList.Count; i++)
        {
            var rc = charList[i];
            char c = rc.Character;

            if (c == '\n')
            {
                lines.Add(currentLine);
                currentLine = new List<PositionedRichChar>();
                cursorX = Padding.Left;
                cursorY += lineSpacing;
                lastWordStart = -1;
                hasResetLineIndent = false;
                continue;
            }

            if (c == '\uFFFD' && rc.SourceInline is Table table)
            {
                if (currentLine.Count > 0)
                {
                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();
                }
                CommitLines(lines, availableWidth);
                cursorX = Padding.Left;
                LayoutTable(table, ref cursorY, availableWidth, rc.LeftIndent);
                lastWordStart = -1;
                hasResetLineIndent = false;
                continue;
            }

            float advance = 0f;
            if (rc.EmbeddedElement != null)
            {
                var child = rc.EmbeddedElement;
                encounteredChildren.Add(child);
                if (child.Parent != this)
                {
                    AddChild(child);
                }
                child.Measure(new Vector2(availableWidth, float.PositiveInfinity));
                advance = child.DesiredSize.X + 4f;
            }
            else
            {
                ushort gIdx = Font.GetGlyphIndex(c);
                advance = Font.GetAdvanceWidth(gIdx, rc.FontSize);
            }

            // Word bounds tracking
            if (c == ' ' || c == '\t')
            {
                lastWordStart = -1;
            }
            else if (lastWordStart == -1)
            {
                lastWordStart = currentLine.Count;
                lastWordStartCursorX = cursorX;
            }

            // Word wrap
            if (cursorX + advance > maxWidth - Padding.Right && cursorX > Padding.Left + rc.LeftIndent)
            {
                if (lastWordStart > 0)
                {
                    // wrap word
                    int wrapCount = currentLine.Count - lastWordStart;
                    var wrapped = currentLine.GetRange(lastWordStart, wrapCount);
                    currentLine.RemoveRange(lastWordStart, wrapCount);

                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();

                    float wrapStart = Padding.Left + (wrapped.Count > 0 ? wrapped[0].Info.LeftIndent : rc.LeftIndent);
                    cursorX = wrapStart;
                    cursorY += lineSpacing;
                    hasResetLineIndent = true;

                    foreach (var wc in wrapped)
                    {
                        var remapped = wc;
                        float shift = wc.Position.X - lastWordStartCursorX;
                        remapped.Position = new Vector2(wrapStart + shift, cursorY);
                        currentLine.Add(remapped);
                        
                        float wAdv = 0f;
                        if (remapped.Info.EmbeddedElement != null)
                        {
                            wAdv = remapped.Info.EmbeddedElement.DesiredSize.X + 4f;
                        }
                        else
                        {
                            ushort wIdx = Font.GetGlyphIndex(remapped.Info.Character);
                            wAdv = Font.GetAdvanceWidth(wIdx, remapped.Info.FontSize);
                        }
                        cursorX = wrapStart + shift + wAdv;
                    }

                    // Add current character
                    if (rc.BulletOffset == 0 && !hasResetLineIndent)
                    {
                        cursorX = Padding.Left + rc.LeftIndent;
                        hasResetLineIndent = true;
                    }
                    float finalXVal = cursorX;
                    if (rc.BulletOffset > 0)
                    {
                        finalXVal = Padding.Left + rc.LeftIndent - rc.BulletOffset + (cursorX - Padding.Left);
                    }
                    var pos = new Vector2(finalXVal, cursorY);
                    currentLine.Add(new PositionedRichChar { Info = rc, Position = pos });
                    cursorX += advance;
                    lastWordStart = 0;
                    lastWordStartCursorX = Padding.Left + rc.LeftIndent;
                    continue;
                }
                else
                {
                    // hard wrap
                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();
                    float wrapStart = Padding.Left + rc.LeftIndent;
                    cursorX = wrapStart;
                    cursorY += lineSpacing;
                    hasResetLineIndent = true;
                }
            }

            if (rc.BulletOffset == 0 && !hasResetLineIndent)
            {
                cursorX = Padding.Left + rc.LeftIndent;
                hasResetLineIndent = true;
            }
            float finalX = cursorX;
            if (rc.BulletOffset > 0)
            {
                finalX = Padding.Left + rc.LeftIndent - rc.BulletOffset + (cursorX - Padding.Left);
            }
            var charPos = new Vector2(finalX, cursorY);
            currentLine.Add(new PositionedRichChar { Info = rc, Position = charPos });
            cursorX += advance;
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        CommitLines(lines, availableWidth);

        // Clean up children that are no longer referenced
        foreach (var child in currentChildren)
        {
            if (child is FrameworkElement fe && !encounteredChildren.Contains(fe))
            {
                RemoveChild(fe);
            }
        }
    }

    public void AccumulateInlines(Inline inline, List<RichChar> list, Brush defaultFg, float defaultSize, bool isBold, bool isItalic, bool isUnderline, Inline? parentInline = null, float leftIndent = 0f)
    {
        Brush fg = inline.Foreground ?? defaultFg;
        float size = inline.FontSize ?? defaultSize;
        Inline source = parentInline ?? inline;

        if (inline is Run run)
        {
            foreach (char c in run.Text)
            {
                list.Add(new RichChar
                {
                    Character = c,
                    Foreground = fg,
                    FontSize = size,
                    IsBold = isBold,
                    IsItalic = isItalic,
                    IsUnderline = isUnderline,
                    SourceInline = source,
                    LeftIndent = leftIndent
                });
            }
        }
        else if (inline is InlineUIContainer uic)
        {
            list.Add(new RichChar
            {
                Character = '\uFFFC',
                Foreground = fg,
                FontSize = size,
                IsBold = isBold,
                IsItalic = isItalic,
                IsUnderline = isUnderline,
                SourceInline = uic,
                EmbeddedElement = uic.Child,
                LeftIndent = leftIndent
            });
        }
        else if (inline is ListBlock listBlock)
        {
            int itemIdx = 1;
            foreach (var item in listBlock.Items)
            {
                if (list.Count > 0 && list[^1].Character != '\n')
                {
                    list.Add(new RichChar { Character = '\n', Foreground = fg, FontSize = size, SourceInline = item, LeftIndent = leftIndent });
                }

                string prefix = listBlock.IsOrdered ? $"{itemIdx}. " : "• ";
                itemIdx++;

                foreach (char bulletChar in prefix)
                {
                    list.Add(new RichChar
                    {
                        Character = bulletChar,
                        Foreground = fg,
                        FontSize = size,
                        IsBold = isBold,
                        IsItalic = isItalic,
                        IsUnderline = isUnderline,
                        SourceInline = item,
                        LeftIndent = leftIndent + listBlock.Indentation,
                        BulletOffset = listBlock.Indentation - 8f
                    });
                }

                foreach (var sub in item.Inlines)
                {
                    AccumulateInlines(sub, list, fg, size, isBold, isItalic, isUnderline, item, leftIndent + listBlock.Indentation);
                }
            }
        }
        else if (inline is Table table)
        {
            if (list.Count > 0 && list[^1].Character != '\n')
            {
                list.Add(new RichChar { Character = '\n', Foreground = fg, FontSize = size, SourceInline = table, LeftIndent = leftIndent });
            }

            list.Add(new RichChar
            {
                Character = '\uFFFD',
                Foreground = fg,
                FontSize = size,
                SourceInline = table,
                LeftIndent = leftIndent
            });

            list.Add(new RichChar { Character = '\n', Foreground = fg, FontSize = size, SourceInline = table, LeftIndent = leftIndent });
        }
        else if (inline is Span span)
        {
            bool nextBold = isBold || (span is Bold);
            bool nextItalic = isItalic || (span is Italic);
            bool nextUnderline = isUnderline || (span is Underline || span is Hyperlink);

            if (span is Hyperlink && inline.Foreground == null)
            {
                fg = new SolidColorBrush(0x0078D4FF);
            }

            foreach (var sub in span.Inlines)
            {
                AccumulateInlines(sub, list, fg, size, nextBold, nextItalic, nextUnderline, span is Hyperlink ? span : source, leftIndent);
            }
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (Font == null) return;

        // Draw table decorations (backgrounds and borders)
        foreach (var dec in _tableDecorations)
        {
            if (dec.Background != null)
            {
                context.DrawRectangle(dec.Background, null, dec.Rect);
            }
            if (dec.BorderBrush != null && dec.BorderThickness > 0f)
            {
                context.DrawRectangle(null, new Pen(dec.BorderBrush, dec.BorderThickness), dec.Rect);
            }
        }

        if (_positionedChars.Count == 0) return;

        // Draw translucent Segoe Blue highlighted selection boxes behind selected characters
        if (SelectionStart >= 0 && SelectionLength > 0)
        {
            var highlightBrush = new SolidColorBrush(0x0078D435); // Translucent Segoe Blue
            for (int i = 0; i < _positionedChars.Count; i++)
            {
                if (i >= SelectionStart && i < SelectionStart + SelectionLength)
                {
                    var pc = _positionedChars[i];
                    if (pc.Info.EmbeddedElement != null) continue;
                    ushort gIdx = Font.GetGlyphIndex(pc.Info.Character);
                    float advance = Font.GetAdvanceWidth(gIdx, pc.Info.FontSize);
                    context.DrawRectangle(highlightBrush, null, new Rect(pc.Position.X, pc.Position.Y, advance, pc.Info.FontSize));
                }
            }
        }

        // Group same-style adjacent characters into single runs
        string runBuffer = "";
        Vector2 startPos = Vector2.Zero;
        RichChar style = default;

        foreach (var pc in _positionedChars)
        {
            if (pc.Info.EmbeddedElement != null)
            {
                if (runBuffer.Length > 0)
                {
                    RenderRun(context, runBuffer, startPos, style);
                    runBuffer = "";
                }
                continue;
            }

            var pcStyle = pc.Info;
            if (pc.Info.SourceInline is Hyperlink hl && hl == _hoveredHyperlink)
            {
                pcStyle.Foreground = new SolidColorBrush(0x005A9EFF);
            }

            if (runBuffer.Length == 0)
            {
                runBuffer = pc.Info.Character.ToString();
                startPos = pc.Position;
                style = pcStyle;
            }
            else if (pcStyle.IsBold == style.IsBold &&
                     pcStyle.IsItalic == style.IsItalic &&
                     pcStyle.IsUnderline == style.IsUnderline &&
                     pcStyle.FontSize == style.FontSize &&
                     pcStyle.Foreground.Equals(style.Foreground) &&
                     Math.Abs(pc.Position.Y - startPos.Y) < 1f)
            {
                runBuffer += pc.Info.Character;
            }
            else
            {
                RenderRun(context, runBuffer, startPos, style);
                runBuffer = pc.Info.Character.ToString();
                startPos = pc.Position;
                style = pcStyle;
            }
        }

        if (runBuffer.Length > 0)
        {
            RenderRun(context, runBuffer, startPos, style);
        }

        base.OnRender(context);
    }

    private void RenderRun(DrawingContext context, string text, Vector2 pos, RichChar style)
    {
        if (Font == null) return;
        context.DrawText(text, Font, style.FontSize, style.Foreground!, pos, style.IsBold, style.IsItalic);
        if (style.IsUnderline)
        {
            float runW = 0f;
            foreach (char c in text)
            {
                ushort idx = Font.GetGlyphIndex(c);
                runW += Font.GetAdvanceWidth(idx, style.FontSize);
            }
            context.DrawRectangle(style.Foreground, null, new Rect(pos.X, pos.Y + style.FontSize - 1f, runW, 1f));
        }
    }

    private List<PositionedRichChar> LayoutCellChars(TableCell cell, float cellWidth, float cellPadding, out float cellHeight)
    {
        var positionedChars = new List<PositionedRichChar>();
        cellHeight = cellPadding * 2f;
        if (Font == null) return positionedChars;

        var charList = new List<RichChar>();
        var defaultFg = Foreground ?? new SolidColorBrush(0xFFFFFFFF);
        foreach (var inline in cell.Inlines)
        {
            AccumulateInlines(inline, charList, defaultFg, FontSize, false, false, false, null, 0f);
        }

        if (charList.Count == 0) return positionedChars;

        float scale = FontSize / Font.UnitsPerEm;
        float lineSpacing = (Font.Ascender - Font.Descender + Font.LineGap) * scale;

        float cursorX = cellPadding;
        float cursorY = cellPadding;
        float maxTextW = cellWidth - cellPadding * 2f;

        var lines = new List<List<PositionedRichChar>>();
        var currentLine = new List<PositionedRichChar>();
        int lastWordStart = -1;
        float lastWordStartCursorX = cellPadding;

        for (int i = 0; i < charList.Count; i++)
        {
            var rc = charList[i];
            char c = rc.Character;

            if (c == '\n')
            {
                lines.Add(currentLine);
                currentLine = new List<PositionedRichChar>();
                cursorX = cellPadding;
                cursorY += lineSpacing;
                lastWordStart = -1;
                continue;
            }

            float advance = 0f;
            if (rc.EmbeddedElement != null)
            {
                rc.EmbeddedElement.Measure(new Vector2(maxTextW, float.PositiveInfinity));
                advance = rc.EmbeddedElement.DesiredSize.X + 4f;
            }
            else
            {
                ushort gIdx = Font.GetGlyphIndex(c);
                advance = Font.GetAdvanceWidth(gIdx, rc.FontSize);
            }

            if (c == ' ' || c == '\t')
            {
                lastWordStart = -1;
            }
            else if (lastWordStart == -1)
            {
                lastWordStart = currentLine.Count;
                lastWordStartCursorX = cursorX;
            }

            if (cursorX + advance > cellWidth - cellPadding && cursorX > cellPadding)
            {
                if (lastWordStart > 0)
                {
                    int wrapCount = currentLine.Count - lastWordStart;
                    var wrapped = currentLine.GetRange(lastWordStart, wrapCount);
                    currentLine.RemoveRange(lastWordStart, wrapCount);

                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();

                    cursorX = cellPadding;
                    cursorY += lineSpacing;

                    foreach (var wc in wrapped)
                    {
                        var remapped = wc;
                        float shift = wc.Position.X - lastWordStartCursorX;
                        remapped.Position = new Vector2(cellPadding + shift, cursorY);
                        currentLine.Add(remapped);

                        float wAdv = 0f;
                        if (remapped.Info.EmbeddedElement != null)
                        {
                            wAdv = remapped.Info.EmbeddedElement.DesiredSize.X + 4f;
                        }
                        else
                        {
                            ushort wIdx = Font.GetGlyphIndex(remapped.Info.Character);
                            wAdv = Font.GetAdvanceWidth(wIdx, remapped.Info.FontSize);
                        }
                        cursorX = cellPadding + shift + wAdv;
                    }

                    var pos = new Vector2(cursorX, cursorY);
                    currentLine.Add(new PositionedRichChar { Info = rc, Position = pos });
                    cursorX += advance;
                    lastWordStart = 0;
                    lastWordStartCursorX = cellPadding;
                    continue;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();
                    cursorX = cellPadding;
                    cursorY += lineSpacing;
                }
            }

            var charPos = new Vector2(cursorX, cursorY);
            currentLine.Add(new PositionedRichChar { Info = rc, Position = charPos });
            cursorX += advance;
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        foreach (var line in lines)
        {
            positionedChars.AddRange(line);
        }

        if (positionedChars.Count > 0)
        {
            float maxCharY = 0f;
            foreach (var pc in positionedChars)
            {
                maxCharY = Math.Max(maxCharY, pc.Position.Y + pc.Info.FontSize);
            }
            cellHeight = maxCharY + cellPadding;
        }

        return positionedChars;
    }

    private void LayoutTable(Table table, ref float cursorY, float availableWidth, float leftIndent)
    {
        int numCols = 0;
        foreach (var row in table.Rows)
        {
            numCols = Math.Max(numCols, row.Cells.Count);
        }
        if (numCols == 0) return;

        float[] colWidths = new float[numCols];
        float remainingW = availableWidth - leftIndent;
        if (table.ColumnWidths != null && table.ColumnWidths.Count > 0)
        {
            for (int col = 0; col < numCols; col++)
            {
                if (col < table.ColumnWidths.Count)
                {
                    colWidths[col] = table.ColumnWidths[col];
                }
                else
                {
                    colWidths[col] = remainingW / (numCols - col);
                }
                remainingW -= colWidths[col];
            }
        }
        else
        {
            float eqW = remainingW / numCols;
            for (int col = 0; col < numCols; col++)
            {
                colWidths[col] = eqW;
            }
        }

        foreach (var row in table.Rows)
        {
            var rowCellChars = new List<List<PositionedRichChar>>();
            float[] cellHeights = new float[row.Cells.Count];

            for (int col = 0; col < row.Cells.Count; col++)
            {
                var cell = row.Cells[col];
                float colW = colWidths[col];
                var pcList = LayoutCellChars(cell, colW, table.CellPadding, out float cHeight);
                rowCellChars.Add(pcList);
                cellHeights[col] = cHeight;
            }

            float rowHeight = 0f;
            foreach (float ch in cellHeights)
            {
                rowHeight = Math.Max(rowHeight, ch);
            }
            if (rowHeight == 0f) rowHeight = FontSize + table.CellPadding * 2f;

            float currentCellX = Padding.Left + leftIndent;
            for (int col = 0; col < row.Cells.Count; col++)
            {
                var cell = row.Cells[col];
                float colW = colWidths[col];
                var cellRect = new Rect(currentCellX, cursorY, colW, rowHeight);

                _tableDecorations.Add(new TableVisualDecoration
                {
                    Rect = cellRect,
                    Background = cell.Background,
                    BorderThickness = table.BorderThickness,
                    BorderBrush = table.BorderBrush
                });

                var pcList = rowCellChars[col];
                foreach (var pc in pcList)
                {
                    var remapped = new PositionedRichChar
                    {
                        Info = pc.Info,
                        Position = new Vector2(pc.Position.X + currentCellX, pc.Position.Y + cursorY)
                    };
                    _positionedChars.Add(remapped);
                }

                currentCellX += colW;
            }

            cursorY += rowHeight;
        }
    }
}

public class RichEditBox : Control
{
    private TtfFont? _font;
    private float _fontSize = 14f;
    private int _caretIndex;
    private readonly RichTextBlock _blockView;
    private RichChar? _activeTypingStyle;

    private int _selectionStart = 0;
    private int _selectionLength = 0;
    private int _selectionAnchor = 0;
    private bool _isDraggingSelection = false;
    private readonly HashSet<Key> _pressedKeys = new();

    private class UndoState
    {
        public List<RichChar> Chars { get; }
        public int CaretIndex { get; }
        public int SelectionStart { get; }
        public int SelectionLength { get; }

        public UndoState(List<RichChar> chars, int caretIndex, int selectionStart, int selectionLength)
        {
            Chars = new List<RichChar>(chars);
            CaretIndex = caretIndex;
            SelectionStart = selectionStart;
            SelectionLength = selectionLength;
        }
    }

    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();

    private void SaveUndoState()
    {
        _undoStack.Push(new UndoState(GetFlatChars(), CaretIndex, SelectionStart, SelectionLength));
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var currentState = new UndoState(GetFlatChars(), CaretIndex, SelectionStart, SelectionLength);
        _redoStack.Push(currentState);

        var previousState = _undoStack.Pop();
        ApplyUndoState(previousState);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var currentState = new UndoState(GetFlatChars(), CaretIndex, SelectionStart, SelectionLength);
        _undoStack.Push(currentState);

        var nextState = _redoStack.Pop();
        ApplyUndoState(nextState);
    }

    private void ApplyUndoState(UndoState state)
    {
        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(state.Chars));
        _blockView.Invalidate();
        
        SelectionStart = state.SelectionStart;
        SelectionLength = state.SelectionLength;
        CaretIndex = state.CaretIndex;

        Invalidate();
    }

    private string GetSelectedText()
    {
        if (SelectionLength <= 0) return string.Empty;
        var chars = GetFlatChars();
        if (chars.Count == 0) return string.Empty;

        int start = Math.Clamp(SelectionStart, 0, chars.Count);
        int length = Math.Clamp(SelectionLength, 0, chars.Count - start);
        if (length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (int i = start; i < start + length; i++)
        {
            sb.Append(chars[i].Character);
        }
        return sb.ToString();
    }

    private void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (SelectionLength > 0)
        {
            DeleteSelection();
        }

        var chars = GetFlatChars();
        int insertIdx = Math.Clamp(CaretIndex, 0, chars.Count);

        RichChar style = new RichChar
        {
            Character = ' ',
            Foreground = _blockView.Foreground ?? new SolidColorBrush(0xFFFFFFFF),
            FontSize = FontSize,
            IsBold = false,
            IsItalic = false,
            IsUnderline = false
        };

        if (_activeTypingStyle != null)
        {
            style = _activeTypingStyle.Value;
        }
        else if (chars.Count > 0)
        {
            int refIdx = insertIdx > 0 ? insertIdx - 1 : 0;
            style = chars[refIdx];
        }

        var newChars = new List<RichChar>();
        foreach (char c in text)
        {
            newChars.Add(new RichChar
            {
                Character = c,
                Foreground = style.Foreground,
                FontSize = style.FontSize,
                IsBold = style.IsBold,
                IsItalic = style.IsItalic,
                IsUnderline = style.IsUnderline
            });
        }

        chars.InsertRange(insertIdx, newChars);

        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(chars));
        _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
        _blockView.Invalidate();
        Invalidate();

        CaretIndex = insertIdx + text.Length;
    }

    private static class ClipboardHelper
    {
        private static string _globalStaticBuffer = string.Empty;

        public static void SetText(string text)
        {
            _globalStaticBuffer = text;
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    RunProcess("pbcopy", "", text);
                }
                else if (OperatingSystem.IsWindows())
                {
                    RunProcess("powershell", "-NoProfile -Command \"[Console]::InputEncoding = [System.Text.Encoding]::UTF8; Set-Clipboard\"", text);
                }
                else if (OperatingSystem.IsLinux())
                {
                    if (IsCommandAvailable("xclip"))
                    {
                        RunProcess("xclip", "-selection clipboard", text);
                    }
                    else if (IsCommandAvailable("xsel"))
                    {
                        RunProcess("xsel", "--clipboard --input", text);
                    }
                }
            }
            catch
            {
                // Fallback to static buffer
            }
        }

        public static string GetText()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    return ReadProcessOutput("pbpaste");
                }
                else if (OperatingSystem.IsWindows())
                {
                    return ReadProcessOutput("powershell", "-NoProfile -Command \"Get-Clipboard\"");
                }
                else if (OperatingSystem.IsLinux())
                {
                    if (IsCommandAvailable("xclip"))
                    {
                        return ReadProcessOutput("xclip", "-selection clipboard -o");
                    }
                    else if (IsCommandAvailable("xsel"))
                    {
                        return ReadProcessOutput("xsel", "--clipboard --output");
                    }
                }
            }
            catch
            {
                // Fallback to static buffer
            }
            return _globalStaticBuffer;
        }

        private static void RunProcess(string filename, string arguments, string input)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = filename;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            if (!string.IsNullOrEmpty(input))
            {
                using (var writer = process.StandardInput)
                {
                    writer.Write(input);
                }
            }
            process.WaitForExit(1000); // 1 second timeout
        }

        private static string ReadProcessOutput(string filename, string arguments = "")
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = filename;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000); // 1 second timeout
            return output.TrimEnd('\r', '\n');
        }

        private static bool IsCommandAvailable(string command)
        {
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "which";
                process.StartInfo.Arguments = command;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(500);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }


    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            _selectionStart = value;
            _blockView.SelectionStart = value;
            Invalidate();
        }
    }

    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            _selectionLength = value;
            _blockView.SelectionLength = value;
            Invalidate();
        }
    }

    public List<Inline> Inlines => _blockView.Inlines;

    public TtfFont? Font
    {
        get => _font;
        set { _font = value; _blockView.Font = value; Invalidate(); }
    }

    public float FontSize
    {
        get => _fontSize;
        set { _fontSize = value; _blockView.FontSize = value; Invalidate(); }
    }

    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            int total = GetTotalCharacters();
            int clamped = Math.Clamp(value, 0, total);
            if (_caretIndex != clamped)
            {
                _caretIndex = clamped;
                Invalidate();
            }
        }
    }

    public override void OnVisualStateChanged()
    {
        base.OnVisualStateChanged();
        if (!IsFocused)
        {
            _pressedKeys.Clear();
            _isDraggingSelection = false;
        }
    }

    public override void OnKeyUp(KeyRoutedEventArgs e)
    {
        _pressedKeys.Remove(e.Key);
        base.OnKeyUp(e);
    }

    public RichEditBox()
    {
        Padding = new Thickness(8);
        CornerRadius = 4f;
        _blockView = new RichTextBlock { Padding = new Thickness(0) };
        AddChild(_blockView);
        
        // Initial text run
        _blockView.Inlines.Add(new Run("Type here in "));
        _blockView.Inlines.Add(new Bold(new Run("Bold")));
        _blockView.Inlines.Add(new Run(" or "));
        _blockView.Inlines.Add(new Italic(new Run("Italic")));
        _blockView.Inlines.Add(new Run("..."));
    }

    private int GetTotalCharacters()
    {
        int count = 0;
        foreach (var inline in Inlines)
        {
            count += GetCharCount(inline);
        }
        return count;
    }

    private int GetCharCount(Inline inline)
    {
        if (inline is Run r) return r.Text.Length;
        if (inline is Span s)
        {
            int c = 0;
            foreach (var sub in s.Inlines) c += GetCharCount(sub);
            return c;
        }
        return 0;
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerPressed(e);

            // Locate clicked character offset for caret positioning
            float clickX = e.Position.X - Padding.Left;
            float clickY = e.Position.Y - Padding.Top;
            
            _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
            var pcs = _blockView.PositionedChars;

            if (pcs.Count == 0)
            {
                SelectionStart = 0;
                SelectionLength = 0;
                _selectionAnchor = 0;
                _isDraggingSelection = true;
                InputSystem.CapturePointer(this);
                CaretIndex = 0;
                return;
            }

            int bestIdx = 0;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < pcs.Count; i++)
            {
                var dist = Vector2.Distance(pcs[i].Position, new Vector2(clickX, clickY));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            _selectionAnchor = bestIdx;
            SelectionStart = bestIdx;
            SelectionLength = 0;
            _isDraggingSelection = true;
            InputSystem.CapturePointer(this);
            CaretIndex = bestIdx;
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerReleased(e);
            InputSystem.ReleasePointerCapture();
            _isDraggingSelection = false;
        }
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerMoved(e);
            if (_isDraggingSelection)
            {
                float clickX = e.Position.X - Padding.Left;
                float clickY = e.Position.Y - Padding.Top;
                
                _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
                var pcs = _blockView.PositionedChars;

                if (pcs.Count > 0)
                {
                    int currentIdx = 0;
                    float bestDist = float.PositiveInfinity;

                    for (int i = 0; i < pcs.Count; i++)
                    {
                        var dist = Vector2.Distance(pcs[i].Position, new Vector2(clickX, clickY));
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            currentIdx = i;
                        }
                    }

                    int start = Math.Min(_selectionAnchor, currentIdx);
                    int length = Math.Abs(_selectionAnchor - currentIdx);
                    SelectionStart = start;
                    SelectionLength = length;
                    CaretIndex = currentIdx;
                }
            }
        }
    }

    public override void OnCharacterReceived(CharacterReceivedRoutedEventArgs e)
    {
        if (IsEnabled && IsFocused)
        {
            // Avoid inserting control characters
            if (char.IsControl(e.Character) && e.Character != '\n' && e.Character != '\r' && e.Character != '\t')
            {
                return;
            }

            SaveUndoState();
            if (SelectionLength > 0)
            {
                DeleteSelection();
            }
            InsertChar(e.Character);
            CaretIndex++;
            e.Handled = true;
        }
        base.OnCharacterReceived(e);
    }

    private void InsertChar(char c)
    {
        var chars = GetFlatChars();
        int insertIdx = Math.Clamp(CaretIndex, 0, chars.Count);

        RichChar style = new RichChar
        {
            Character = ' ',
            Foreground = _blockView.Foreground ?? new SolidColorBrush(0xFFFFFFFF),
            FontSize = FontSize,
            IsBold = false,
            IsItalic = false,
            IsUnderline = false
        };

        if (_activeTypingStyle != null)
        {
            style = _activeTypingStyle.Value;
        }
        else if (chars.Count > 0)
        {
            int refIdx = insertIdx > 0 ? insertIdx - 1 : 0;
            style = chars[refIdx];
        }

        chars.Insert(insertIdx, new RichChar
        {
            Character = c,
            Foreground = style.Foreground,
            FontSize = style.FontSize,
            IsBold = style.IsBold,
            IsItalic = style.IsItalic,
            IsUnderline = style.IsUnderline
        });

        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(chars));
        _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
        _blockView.Invalidate();
        Invalidate();
    }

    public override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsEnabled && IsFocused)
        {
            _pressedKeys.Add(e.Key);

            bool isCtrlOrCmd = _pressedKeys.Contains(Key.ControlLeft) || 
                               _pressedKeys.Contains(Key.ControlRight) || 
                               _pressedKeys.Contains(Key.SuperLeft) || 
                               _pressedKeys.Contains(Key.SuperRight);

            bool isShift = _pressedKeys.Contains(Key.ShiftLeft) || 
                           _pressedKeys.Contains(Key.ShiftRight);

            if (isCtrlOrCmd)
            {
                if (e.Key == Key.A)
                {
                    SelectionStart = 0;
                    SelectionLength = GetTotalCharacters();
                    CaretIndex = SelectionLength;
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Z)
                {
                    Undo();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Y)
                {
                    Redo();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.C)
                {
                    string text = GetSelectedText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        ClipboardHelper.SetText(text);
                    }
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.X)
                {
                    string text = GetSelectedText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        ClipboardHelper.SetText(text);
                        SaveUndoState();
                        DeleteSelection();
                    }
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.V)
                {
                    string text = ClipboardHelper.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        SaveUndoState();
                        InsertText(text);
                    }
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.B)
                {
                    if (SelectionLength > 0) SaveUndoState();
                    ToggleStyle("bold");
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.I)
                {
                    if (SelectionLength > 0) SaveUndoState();
                    ToggleStyle("italic");
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.U)
                {
                    if (SelectionLength > 0) SaveUndoState();
                    ToggleStyle("underline");
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Backspace)
            {
                if (SelectionLength > 0 || CaretIndex > 0)
                {
                    SaveUndoState();
                }
                if (SelectionLength > 0)
                {
                    DeleteSelection();
                    e.Handled = true;
                }
                else if (CaretIndex > 0)
                {
                    if (isCtrlOrCmd)
                    {
                        int prevBoundary = FindPreviousWordBoundary(CaretIndex);
                        int len = CaretIndex - prevBoundary;
                        DeleteCharsRange(prevBoundary, len);
                        CaretIndex = prevBoundary;
                    }
                    else
                    {
                        DeleteChar(CaretIndex - 1);
                        CaretIndex--;
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                int total = GetTotalCharacters();
                if (SelectionLength > 0 || CaretIndex < total)
                {
                    SaveUndoState();
                }
                if (SelectionLength > 0)
                {
                    DeleteSelection();
                    e.Handled = true;
                }
                else if (CaretIndex < total)
                {
                    if (isCtrlOrCmd)
                    {
                        int nextBoundary = FindNextWordBoundary(CaretIndex);
                        int len = nextBoundary - CaretIndex;
                        DeleteCharsRange(CaretIndex, len);
                    }
                    else
                    {
                        DeleteChar(CaretIndex);
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Left)
            {
                if (isCtrlOrCmd)
                {
                    int newCaret = FindPreviousWordBoundary(CaretIndex);
                    if (isShift)
                    {
                        if (SelectionLength == 0) _selectionAnchor = CaretIndex;
                        CaretIndex = newCaret;
                        SelectionStart = Math.Min(_selectionAnchor, CaretIndex);
                        SelectionLength = Math.Abs(_selectionAnchor - CaretIndex);
                    }
                    else
                    {
                        CaretIndex = newCaret;
                        SelectionLength = 0;
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
                else if (isShift)
                {
                    if (SelectionLength == 0)
                    {
                        _selectionAnchor = CaretIndex;
                    }
                    if (CaretIndex > 0)
                    {
                        CaretIndex--;
                        SelectionStart = Math.Min(_selectionAnchor, CaretIndex);
                        SelectionLength = Math.Abs(_selectionAnchor - CaretIndex);
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
                else
                {
                    if (SelectionLength > 0)
                    {
                        CaretIndex = SelectionStart;
                        SelectionLength = 0;
                        e.Handled = true;
                    }
                    else if (CaretIndex > 0)
                    {
                        CaretIndex--;
                        e.Handled = true;
                    }
                    _activeTypingStyle = null;
                }
            }
            else if (e.Key == Key.Right)
            {
                int total = GetTotalCharacters();
                if (isCtrlOrCmd)
                {
                    int newCaret = FindNextWordBoundary(CaretIndex);
                    if (isShift)
                    {
                        if (SelectionLength == 0) _selectionAnchor = CaretIndex;
                        CaretIndex = newCaret;
                        SelectionStart = Math.Min(_selectionAnchor, CaretIndex);
                        SelectionLength = Math.Abs(_selectionAnchor - CaretIndex);
                    }
                    else
                    {
                        CaretIndex = newCaret;
                        SelectionLength = 0;
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
                else if (isShift)
                {
                    if (SelectionLength == 0)
                    {
                        _selectionAnchor = CaretIndex;
                    }
                    if (CaretIndex < total)
                    {
                        CaretIndex++;
                        SelectionStart = Math.Min(_selectionAnchor, CaretIndex);
                        SelectionLength = Math.Abs(_selectionAnchor - CaretIndex);
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
                else
                {
                    if (SelectionLength > 0)
                    {
                        CaretIndex = SelectionStart + SelectionLength;
                        SelectionLength = 0;
                        e.Handled = true;
                    }
                    else if (CaretIndex < total)
                    {
                        CaretIndex++;
                        e.Handled = true;
                    }
                    _activeTypingStyle = null;
                }
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
                var pcs = _blockView.PositionedChars;
                if (pcs.Count > 0)
                {
                    var lines = new List<List<int>>();
                    var currentLineIndices = new List<int> { 0 };
                    lines.Add(currentLineIndices);
                    for (int i = 1; i < pcs.Count; i++)
                    {
                        if (Math.Abs(pcs[i].Position.Y - pcs[currentLineIndices[0]].Position.Y) > 1f)
                        {
                            currentLineIndices = new List<int> { i };
                            lines.Add(currentLineIndices);
                        }
                        else
                        {
                            currentLineIndices.Add(i);
                        }
                    }

                    int currentLineIdx = -1;
                    for (int l = 0; l < lines.Count; l++)
                    {
                        if (CaretIndex < pcs.Count)
                        {
                            if (lines[l].Contains(CaretIndex))
                            {
                                currentLineIdx = l;
                                break;
                            }
                        }
                        else
                        {
                            currentLineIdx = lines.Count - 1;
                        }
                    }

                    if (currentLineIdx == -1)
                    {
                        currentLineIdx = 0;
                    }

                    int targetLineIdx = e.Key == Key.Up ? currentLineIdx - 1 : currentLineIdx + 1;

                    if (targetLineIdx >= 0 && targetLineIdx < lines.Count)
                    {
                        Vector2 currentPos = Vector2.Zero;
                        if (CaretIndex < pcs.Count)
                        {
                            currentPos = pcs[CaretIndex].Position;
                        }
                        else if (pcs.Count > 0)
                        {
                            var pc = pcs[pcs.Count - 1];
                            currentPos = pc.Position;
                            if (Font != null)
                            {
                                ushort lastG = Font.GetGlyphIndex(pc.Info.Character);
                                currentPos.X += Font.GetAdvanceWidth(lastG, pc.Info.FontSize);
                            }
                        }

                        int bestTargetCaretIdx = -1;
                        float bestDist = float.PositiveInfinity;

                        var targetLine = lines[targetLineIdx];
                        for (int k = 0; k < targetLine.Count; k++)
                        {
                            int charIdx = targetLine[k];
                            
                            Vector2 candPos = pcs[charIdx].Position;
                            float xDiff = Math.Abs(candPos.X - currentPos.X);
                            float yDiff = Math.Abs(candPos.Y - currentPos.Y);
                            float dist = xDiff + yDiff * 2f;

                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestTargetCaretIdx = charIdx;
                            }

                            if (k == targetLine.Count - 1)
                            {
                                float advance = 0f;
                                if (Font != null)
                                {
                                    ushort gIdx = Font.GetGlyphIndex(pcs[charIdx].Info.Character);
                                    advance = Font.GetAdvanceWidth(gIdx, pcs[charIdx].Info.FontSize);
                                }
                                Vector2 candPosAfter = new Vector2(pcs[charIdx].Position.X + advance, pcs[charIdx].Position.Y);
                                float xDiffAfter = Math.Abs(candPosAfter.X - currentPos.X);
                                float yDiffAfter = Math.Abs(candPosAfter.Y - currentPos.Y);
                                float distAfter = xDiffAfter + yDiffAfter * 2f;

                                if (distAfter < bestDist)
                                {
                                    bestDist = distAfter;
                                    bestTargetCaretIdx = charIdx + 1;
                                }
                            }
                        }

                        if (bestTargetCaretIdx != -1)
                        {
                            if (isShift)
                            {
                                if (SelectionLength == 0)
                                {
                                    _selectionAnchor = CaretIndex;
                                }
                                CaretIndex = bestTargetCaretIdx;
                                SelectionStart = Math.Min(_selectionAnchor, CaretIndex);
                                SelectionLength = Math.Abs(_selectionAnchor - CaretIndex);
                            }
                            else
                            {
                                CaretIndex = bestTargetCaretIdx;
                                SelectionStart = CaretIndex;
                                SelectionLength = 0;
                            }
                        }
                    }
                    _activeTypingStyle = null;
                    e.Handled = true;
                }
            }
        }
        base.OnKeyDown(e);
    }

    private void DeleteChar(int idx)
    {
        int index = idx;
        foreach (var inline in Inlines)
        {
            if (inline is Run run)
            {
                if (index < run.Text.Length)
                {
                    run.Text = run.Text.Remove(index, 1);
                    _blockView.Invalidate();
                    return;
                }
                index -= run.Text.Length;
            }
            else if (inline is Span span)
            {
                foreach (var sub in span.Inlines)
                {
                    if (sub is Run subRun)
                    {
                        if (index < subRun.Text.Length)
                        {
                            subRun.Text = subRun.Text.Remove(index, 1);
                            _blockView.Invalidate();
                            return;
                        }
                        index -= subRun.Text.Length;
                    }
                }
            }
        }
    }

    private void DeleteCharsRange(int start, int len)
    {
        if (len <= 0) return;
        var chars = GetFlatChars();
        if (chars.Count == 0) return;

        int idx = Math.Clamp(start, 0, chars.Count);
        int count = Math.Clamp(len, 0, chars.Count - idx);
        if (count <= 0) return;

        chars.RemoveRange(idx, count);

        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(chars));
        _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
        _blockView.Invalidate();
        Invalidate();
    }

    private int FindPreviousWordBoundary(int current)
    {
        if (current <= 0) return 0;
        var chars = GetFlatChars();
        if (chars.Count == 0) return 0;

        int idx = Math.Clamp(current - 1, 0, chars.Count - 1);
        while (idx > 0 && char.IsWhiteSpace(chars[idx].Character)) idx--;
        while (idx > 0 && !char.IsWhiteSpace(chars[idx].Character)) idx--;

        return idx > 0 ? idx + 1 : 0;
    }

    private int FindNextWordBoundary(int current)
    {
        var chars = GetFlatChars();
        if (chars.Count == 0 || current >= chars.Count) return chars.Count;

        int idx = Math.Clamp(current, 0, chars.Count - 1);
        while (idx < chars.Count && !char.IsWhiteSpace(chars[idx].Character)) idx++;
        while (idx < chars.Count && char.IsWhiteSpace(chars[idx].Character)) idx++;

        return idx;
    }

    private List<RichChar> GetFlatChars()
    {
        var list = new List<RichChar>();
        var defaultFg = _blockView.Foreground ?? new SolidColorBrush(0xFFFFFFFF);
        foreach (var inline in Inlines)
        {
            _blockView.AccumulateInlines(inline, list, defaultFg, FontSize, false, false, false);
        }
        return list;
    }

    private List<Inline> RebuildInlinesFromChars(List<RichChar> chars)
    {
        var newInlines = new List<Inline>();
        if (chars.Count == 0)
        {
            return newInlines;
        }

        int i = 0;
        while (i < chars.Count)
        {
            int start = i;
            var c = chars[i];
            
            while (i < chars.Count && 
                   chars[i].IsBold == c.IsBold &&
                   chars[i].IsItalic == c.IsItalic &&
                   chars[i].IsUnderline == c.IsUnderline &&
                   chars[i].FontSize == c.FontSize &&
                   Equals(chars[i].Foreground, c.Foreground))
            {
                i++;
            }

            var sb = new System.Text.StringBuilder();
            for (int k = start; k < i; k++)
            {
                sb.Append(chars[k].Character);
            }

            Inline element = new Run(sb.ToString())
            {
                Foreground = c.Foreground,
                FontSize = c.FontSize
            };

            if (c.IsBold)
            {
                element = new Bold(element);
            }
            if (c.IsItalic)
            {
                element = new Italic(element);
            }
            if (c.IsUnderline)
            {
                element = new Underline(element);
            }

            newInlines.Add(element);
        }

        return newInlines;
    }

    public void Copy()
    {
        string text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            ClipboardHelper.SetText(text);
        }
    }

    public void Cut()
    {
        string text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            ClipboardHelper.SetText(text);
            SaveUndoState();
            DeleteSelection();
        }
    }

    public void Paste()
    {
        string text = ClipboardHelper.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            SaveUndoState();
            InsertText(text);
        }
    }

    public void ToggleStyle(string styleType)
    {
        if (SelectionLength == 0)
        {
            if (_activeTypingStyle == null)
            {
                var flatChars = GetFlatChars();
                RichChar baseStyle = new RichChar
                {
                    Character = ' ',
                    Foreground = _blockView.Foreground ?? new SolidColorBrush(0xFFFFFFFF),
                    FontSize = FontSize,
                    IsBold = false,
                    IsItalic = false,
                    IsUnderline = false
                };
                if (flatChars.Count > 0)
                {
                    int refIdx = CaretIndex > 0 ? CaretIndex - 1 : 0;
                    baseStyle = flatChars[Math.Clamp(refIdx, 0, flatChars.Count - 1)];
                }
                _activeTypingStyle = baseStyle;
            }

            var ts = _activeTypingStyle.Value;
            if (styleType == "bold") ts.IsBold = !ts.IsBold;
            else if (styleType == "italic") ts.IsItalic = !ts.IsItalic;
            else if (styleType == "underline") ts.IsUnderline = !ts.IsUnderline;
            _activeTypingStyle = ts;
            return;
        }

        var chars = GetFlatChars();
        if (chars.Count == 0) return;

        int start = Math.Clamp(SelectionStart, 0, chars.Count);
        int end = Math.Clamp(SelectionStart + SelectionLength, 0, chars.Count);
        if (start >= end) return;

        bool allHaveStyle = true;
        for (int k = start; k < end; k++)
        {
            bool hasStyle = styleType switch
            {
                "bold" => chars[k].IsBold,
                "italic" => chars[k].IsItalic,
                "underline" => chars[k].IsUnderline,
                _ => false
            };
            if (!hasStyle)
            {
                allHaveStyle = false;
                break;
            }
        }

        bool targetState = !allHaveStyle;
        for (int k = start; k < end; k++)
        {
            var c = chars[k];
            if (styleType == "bold") c.IsBold = targetState;
            else if (styleType == "italic") c.IsItalic = targetState;
            else if (styleType == "underline") c.IsUnderline = targetState;
            chars[k] = c;
        }

        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(chars));
        _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
        _blockView.Invalidate();
        Invalidate();
    }

    private void DeleteSelection()
    {
        if (SelectionLength == 0) return;

        var chars = GetFlatChars();
        if (chars.Count == 0) return;

        int start = Math.Clamp(SelectionStart, 0, chars.Count);
        int length = Math.Clamp(SelectionLength, 0, chars.Count - start);
        if (length == 0) return;

        chars.RemoveRange(start, length);

        CaretIndex = start;
        SelectionStart = start;
        SelectionLength = 0;

        Inlines.Clear();
        Inlines.AddRange(RebuildInlinesFromChars(chars));
        _blockView.Invalidate();
        Invalidate();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float w = WidthConstraint ?? Math.Max(200f, availableSize.X);
        float h = HeightConstraint ?? 120f;
        _blockView.Measure(new Vector2(w - Padding.Horizontal, float.PositiveInfinity));
        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
        _blockView.Arrange(new Rect(Padding.Left, Padding.Top, arrangeRect.Width - Padding.Horizontal, arrangeRect.Height - Padding.Vertical));
    }

    public override void OnRender(DrawingContext context)
    {
        // Draw glassmorphic border card
        Brush bg;
        Pen borderPen;

        if (!IsEnabled)
        {
            bg = new SolidColorBrush(0x2A2A3540);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF08), 1f);
        }
        else if (IsFocused)
        {
            bg = new SolidColorBrush(0x13131AFF); // Mica/deep dark card
            borderPen = new Pen(new SolidColorBrush(0x0078D4FF), 2f); // Sharp Segoe Blue active focus ring
        }
        else if (IsPointerOver)
        {
            bg = new SolidColorBrush(0xFFFFFF15);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF30), 1f);
        }
        else
        {
            bg = new SolidColorBrush(0xFFFFFF0D);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF15), 1f);
        }

        var roundedPath = CreateRoundedRectPath(new Rect(Vector2.Zero, Size), CornerRadius);
        context.DrawPath(bg, borderPen, roundedPath);

        base.OnRender(context);

        // Draw caret using modern Segoe Blue active color
        if (IsFocused && Font != null && (DateTime.Now.Millisecond / 500) % 2 == 0)
        {
            _blockView.PerformRichLayout(Size.X - Padding.Horizontal);
            var pcs = _blockView.PositionedChars;

            Vector2 caretPos = new Vector2(Padding.Left, Padding.Top);
            float caretH = FontSize;
            if (pcs.Count > 0)
            {
                int cIdx = Math.Clamp(CaretIndex, 0, pcs.Count - 1);
                var pc = pcs[cIdx];
                caretPos = pc.Position + new Vector2(Padding.Left, Padding.Top);
                caretH = pc.Info.FontSize;
                if (CaretIndex >= pcs.Count)
                {
                    // place caret at end of last char
                    ushort lastG = Font.GetGlyphIndex(pc.Info.Character);
                    caretPos.X += Font.GetAdvanceWidth(lastG, pc.Info.FontSize);
                }
            }

            Rect caretRect = new Rect(caretPos.X, caretPos.Y, 1.5f, caretH + 2f);
            context.DrawRectangle(new SolidColorBrush(0x0078D4FF), null, caretRect);
        }
    }

    private static PathGeometry CreateRoundedRectPath(Rect rect, float r)
    {
        var geo = new PathGeometry();
        var fig = new PathFigure(new Vector2(rect.X + r, rect.Y), isClosed: true);
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + rect.Width - r, rect.Y)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X + rect.Width, rect.Y), new Vector2(rect.X + rect.Width, rect.Y + r)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + rect.Width, rect.Y + rect.Height - r)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X + rect.Width, rect.Y + rect.Height), new Vector2(rect.X + rect.Width - r, rect.Y + rect.Height)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + r, rect.Y + rect.Height)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X, rect.Y + rect.Height), new Vector2(rect.X, rect.Y + rect.Height - r)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X, rect.Y + r)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X, rect.Y), new Vector2(rect.X + r, rect.Y)));
        geo.Figures.Add(fig);
        return geo;
    }
}
