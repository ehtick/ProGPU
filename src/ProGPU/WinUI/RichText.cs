using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using ProGPU.Text;

namespace ProGPU.WinUI;

public abstract class TextElement
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

public struct RichChar
{
    public char Character;
    public Brush Foreground;
    public float FontSize;
    public bool IsBold;
    public bool IsItalic;
    public bool IsUnderline;
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
            measuredW = Math.Max(measuredW, pc.Position.X + pc.Info.FontSize / 2f);
            measuredH = Math.Max(measuredH, pc.Position.Y + pc.Info.FontSize);
        }

        return new Vector2(measuredW, measuredH + 4f);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
        PerformRichLayout(arrangeRect.Width);
    }

    public void PerformRichLayout(float maxWidth)
    {
        _positionedChars.Clear();
        if (Font == null) return;

        var charList = new List<RichChar>();
        var defaultFg = Foreground ?? new SolidColorBrush(0xFFFFFFFF);

        foreach (var inline in Inlines)
        {
            AccumulateInlines(inline, charList, defaultFg, FontSize, false, false, false);
        }

        if (charList.Count == 0) return;

        // Structured paragraph word wrapping
        float scale = FontSize / Font.UnitsPerEm;
        float lineSpacing = (Font.Ascender - Font.Descender + Font.LineGap) * scale;
        float fontAscent = Font.Ascender * scale;

        float cursorX = Padding.Left;
        float cursorY = Padding.Top;

        var lines = new List<List<PositionedRichChar>>();
        var currentLine = new List<PositionedRichChar>();
        int lastWordStart = -1;
        float lastWordStartCursorX = Padding.Left;

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
                continue;
            }

            ushort gIdx = Font.GetGlyphIndex(c);
            float advance = Font.GetAdvanceWidth(gIdx, rc.FontSize);

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
            if (cursorX + advance > maxWidth - Padding.Right && cursorX > Padding.Left)
            {
                if (lastWordStart > 0)
                {
                    // wrap word
                    int wrapCount = currentLine.Count - lastWordStart;
                    var wrapped = currentLine.GetRange(lastWordStart, wrapCount);
                    currentLine.RemoveRange(lastWordStart, wrapCount);

                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();

                    cursorX = Padding.Left;
                    cursorY += lineSpacing;

                    foreach (var wc in wrapped)
                    {
                        var remapped = wc;
                        float shift = wc.Position.X - lastWordStartCursorX;
                        remapped.Position = new Vector2(Padding.Left + shift, cursorY);
                        currentLine.Add(remapped);
                        
                        ushort wIdx = Font.GetGlyphIndex(remapped.Info.Character);
                        cursorX = Padding.Left + shift + Font.GetAdvanceWidth(wIdx, remapped.Info.FontSize);
                    }

                    // Add current character
                    var pos = new Vector2(cursorX, cursorY);
                    currentLine.Add(new PositionedRichChar { Info = rc, Position = pos });
                    cursorX += advance;
                    lastWordStart = 0;
                    lastWordStartCursorX = Padding.Left;
                    continue;
                }
                else
                {
                    // hard wrap
                    lines.Add(currentLine);
                    currentLine = new List<PositionedRichChar>();
                    cursorX = Padding.Left;
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

        // Add back to final rendering coordinates
        foreach (var line in lines)
        {
            _positionedChars.AddRange(line);
        }
    }

    public void AccumulateInlines(Inline inline, List<RichChar> list, Brush defaultFg, float defaultSize, bool isBold, bool isItalic, bool isUnderline)
    {
        Brush fg = inline.Foreground ?? defaultFg;
        float size = inline.FontSize ?? defaultSize;

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
                    IsUnderline = isUnderline
                });
            }
        }
        else if (inline is Span span)
        {
            bool nextBold = isBold || (span is Bold);
            bool nextItalic = isItalic || (span is Italic);
            bool nextUnderline = isUnderline || (span is Underline);
            foreach (var sub in span.Inlines)
            {
                AccumulateInlines(sub, list, fg, size, nextBold, nextItalic, nextUnderline);
            }
        }
    }

    public override void OnRender(DrawingContext context)
    {
        if (Font == null || _positionedChars.Count == 0) return;

        // Draw translucent Segoe Blue highlighted selection boxes behind selected characters
        if (SelectionStart >= 0 && SelectionLength > 0)
        {
            var highlightBrush = new SolidColorBrush(0x0078D435); // Translucent Segoe Blue
            for (int i = 0; i < _positionedChars.Count; i++)
            {
                if (i >= SelectionStart && i < SelectionStart + SelectionLength)
                {
                    var pc = _positionedChars[i];
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
            if (runBuffer.Length == 0)
            {
                runBuffer = pc.Info.Character.ToString();
                startPos = pc.Position;
                style = pc.Info;
            }
            else if (pc.Info.IsBold == style.IsBold &&
                     pc.Info.IsItalic == style.IsItalic &&
                     pc.Info.IsUnderline == style.IsUnderline &&
                     pc.Info.FontSize == style.FontSize &&
                     pc.Info.Foreground.Equals(style.Foreground) &&
                     Math.Abs(pc.Position.Y - startPos.Y) < 1f)
            {
                runBuffer += pc.Info.Character;
            }
            else
            {
                context.DrawText(runBuffer, Font, style.FontSize, style.Foreground!, startPos);
                if (style.IsUnderline)
                {
                    float runW = pc.Position.X - startPos.X;
                    context.DrawRectangle(style.Foreground, null, new Rect(startPos.X, startPos.Y + style.FontSize - 1f, runW, 1f));
                }
                runBuffer = pc.Info.Character.ToString();
                startPos = pc.Position;
                style = pc.Info;
            }
        }

        if (runBuffer.Length > 0)
        {
            context.DrawText(runBuffer, Font, style.FontSize, style.Foreground!, startPos);
            if (style.IsUnderline)
            {
                var lastPc = _positionedChars[_positionedChars.Count - 1];
                float advance = Font.GetAdvanceWidth(Font.GetGlyphIndex(lastPc.Info.Character), lastPc.Info.FontSize);
                float runW = lastPc.Position.X + advance - startPos.X;
                context.DrawRectangle(style.Foreground, null, new Rect(startPos.X, startPos.Y + style.FontSize - 1f, runW, 1f));
            }
        }

        base.OnRender(context);
    }
}

public class RichEditBox : Control
{
    private TtfFont? _font;
    private float _fontSize = 14f;
    private int _caretIndex;
    private readonly RichTextBlock _blockView;

    private int _selectionStart = 0;
    private int _selectionLength = 0;
    private int _selectionAnchor = 0;
    private bool _isDraggingSelection = false;
    private readonly HashSet<Key> _pressedKeys = new();

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
            CaretIndex = bestIdx;
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerReleased(e);
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
        // Simple insert into first run or appropriate segment
        if (Inlines.Count == 0)
        {
            Inlines.Add(new Run(c.ToString()));
            return;
        }

        int index = CaretIndex;
        foreach (var inline in Inlines)
        {
            if (inline is Run run)
            {
                if (index <= run.Text.Length)
                {
                    run.Text = run.Text.Insert(index, c.ToString());
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
                        if (index <= subRun.Text.Length)
                        {
                            subRun.Text = subRun.Text.Insert(index, c.ToString());
                            _blockView.Invalidate();
                            return;
                        }
                        index -= subRun.Text.Length;
                    }
                }
            }
        }

        // fallback append
        if (Inlines[Inlines.Count - 1] is Run lastRun)
        {
            lastRun.Text += c;
        }
        else
        {
            Inlines.Add(new Run(c.ToString()));
        }
        _blockView.Invalidate();
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

            if (isCtrlOrCmd)
            {
                if (e.Key == Key.B)
                {
                    ToggleStyle("bold");
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.I)
                {
                    ToggleStyle("italic");
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.U)
                {
                    ToggleStyle("underline");
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Backspace)
            {
                if (SelectionLength > 0)
                {
                    DeleteSelection();
                    e.Handled = true;
                }
                else if (CaretIndex > 0)
                {
                    DeleteChar(CaretIndex - 1);
                    CaretIndex--;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (SelectionLength > 0)
                {
                    DeleteSelection();
                    e.Handled = true;
                }
                else if (CaretIndex < GetTotalCharacters())
                {
                    DeleteChar(CaretIndex);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Left)
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
            }
            else if (e.Key == Key.Right)
            {
                if (SelectionLength > 0)
                {
                    CaretIndex = SelectionStart + SelectionLength;
                    SelectionLength = 0;
                    e.Handled = true;
                }
                else if (CaretIndex < GetTotalCharacters())
                {
                    CaretIndex++;
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

    private void ToggleStyle(string styleType)
    {
        if (SelectionLength == 0) return;

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
