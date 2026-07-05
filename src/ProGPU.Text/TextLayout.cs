using System;
using System.Collections.Generic;
using System.Numerics;

namespace ProGPU.Text;

public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public struct TextRunGlyph
{
    public char Character;
    public uint CodePoint;
    public Vector2 Position; // Top-Left screen coordinates of the glyph box
    public GlyphInfo Glyph;
    public TtfFont Font; // The font that owns/defines this glyph
}

public class TextLayout
{
    private readonly struct LineRange
    {
        public LineRange(int start, int count)
        {
            Start = start;
            Count = count;
        }

        public int Start { get; }
        public int Count { get; }
    }

    private static readonly string[] FallbackFontPaths = new[]
    {
        "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",
        "/System/Library/Fonts/PingFang.ttc",
        "/System/Library/Fonts/Apple Symbols.ttf",
        "/System/Library/Fonts/Apple Color Emoji.ttc"
    };

    private static readonly List<TtfFont> _fallbackFonts = new();
    private static bool _fallbacksInitialized = false;

    private static int EstimateGlyphCapacity(string text)
    {
        var capacity = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                continue;
            }

            capacity++;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        return Math.Max(1, capacity);
    }

    private static int EstimateLineCapacity(string text)
    {
        var capacity = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                capacity++;
            }
        }

        return capacity;
    }

    private static void AddLineRange(List<LineRange> lines, int start, int end)
    {
        if (end > start)
        {
            lines.Add(new LineRange(start, end - start));
        }
    }

    private static void InitializeFallbacks()
    {
        if (_fallbacksInitialized) return;
        _fallbacksInitialized = true;

        for (int pathIndex = 0; pathIndex < FallbackFontPaths.Length; pathIndex++)
        {
            var path = FallbackFontPaths[pathIndex];
            if (System.IO.File.Exists(path))
            {
                try
                {
                    _fallbackFonts.Add(new TtfFont(path));
                    ProGpuTextDiagnostics.WriteLine($"[TextLayout] Loaded system fallback font: {path}");
                }
                catch (Exception ex)
                {
                    ProGpuTextDiagnostics.WriteLine($"[TextLayout] Warning: Failed to load fallback font '{path}': {ex.Message}");
                }
            }
        }
    }

    public string Text { get; }
    public TtfFont Font { get; }
    public float FontSize { get; }
    public float MaxWidth { get; }
    public TextAlignment Alignment { get; }

    public List<TextRunGlyph> Glyphs { get; } = new();
    public Vector2 MeasuredSize { get; private set; }
    public bool HasTextures { get; private set; }

    public TextLayout(string text, TtfFont font, float fontSize, float maxWidth = float.PositiveInfinity, TextAlignment alignment = TextAlignment.Left, GlyphAtlas? atlas = null)
    {
        Text = text ?? string.Empty;
        Font = font;
        FontSize = fontSize;
        MaxWidth = maxWidth;
        Alignment = alignment;

        GenerateLayout(atlas);
    }

    public void GenerateLayout(GlyphAtlas? atlas)
    {
        HasTextures = true;
        Glyphs.Clear();
        if (string.IsNullOrEmpty(Text))
        {
            MeasuredSize = Vector2.Zero;
            return;
        }

        int estimatedGlyphCapacity = EstimateGlyphCapacity(Text);
        Glyphs.EnsureCapacity(estimatedGlyphCapacity);

        // Layout metric scales
        float scale = FontSize / Font.UnitsPerEm;
        float lineSpacing = (Font.Ascender - Font.Descender + Font.LineGap) * scale;
        float fontAscent = Font.Ascender * scale;

        var lines = new List<LineRange>(EstimateLineCapacity(Text));
        int currentLineStart = 0;

        float cursorX = 0f;
        float cursorY = 0f;
        uint prevCodePoint = 0;

        // Keep track of words to enable word wrapping
        int lastWordStartIndex = -1;
        float lastWordStartCursorX = 0f;

        for (int i = 0; i < Text.Length; i++)
        {
            char c = Text[i];
            uint codePoint = c;

            // Handle surrogate pairs to decode to full 32-bit UTF-32 code point
            if (char.IsHighSurrogate(c) && i + 1 < Text.Length && char.IsLowSurrogate(Text[i + 1]))
            {
                codePoint = (uint)char.ConvertToUtf32(c, Text[i + 1]);
                i++; // skip low surrogate
            }

            if (codePoint == '\n')
            {
                // Explicit line break
                AddLineRange(lines, currentLineStart, Glyphs.Count);
                currentLineStart = Glyphs.Count;
                cursorX = 0f;
                cursorY += lineSpacing;
                prevCodePoint = 0;
                lastWordStartIndex = -1;
                continue;
            }

            ushort glyphIdx = Font.GetGlyphIndex(codePoint);
            TtfFont resolvedFont = Font;

            // If the character is not supported in the primary font, try system fallback fonts (e.g. CJK or Emojis)
            if (glyphIdx == 0 && codePoint != ' ' && codePoint != '\t' && codePoint != '\n')
            {
                InitializeFallbacks();
                for (int fallbackIndex = 0; fallbackIndex < _fallbackFonts.Count; fallbackIndex++)
                {
                    var fbFont = _fallbackFonts[fallbackIndex];
                    ushort fbIdx = fbFont.GetGlyphIndex(codePoint);
                    if (fbIdx != 0)
                    {
                        glyphIdx = fbIdx;
                        resolvedFont = fbFont;
                        break;
                    }
                }
            }

            float advance = resolvedFont.GetAdvanceWidth(glyphIdx, FontSize);
            GlyphInfo glyph = new GlyphInfo
            {
                X = 0,
                Y = 0,
                Width = (uint)advance,
                Height = (uint)lineSpacing,
                BearX = 0,
                BearY = 0,
                Advance = advance,
                TexCoordMin = Vector2.Zero,
                TexCoordMax = Vector2.Zero
            };
            
            // Add kerning offset
            if (prevCodePoint != 0)
            {
                cursorX += Font.GetKerning(prevCodePoint, codePoint, FontSize);
            }

            // Word boundary tracking
            if (codePoint == ' ' || codePoint == '\t')
            {
                lastWordStartIndex = -1;
            }
            else if (lastWordStartIndex == -1)
            {
                lastWordStartIndex = Glyphs.Count;
                lastWordStartCursorX = cursorX;
            }

            // Auto-wrapping logic on layout boundary
            if (cursorX + glyph.Advance > MaxWidth && cursorX > 0)
            {
                if (lastWordStartIndex > currentLineStart)
                {
                    // Wrap the last partial word to the next line
                    int wrapStartIndex = lastWordStartIndex;
                    int previousLineEnd = Glyphs.Count;
                    int wrapCount = previousLineEnd - wrapStartIndex;
                    AddLineRange(lines, currentLineStart, wrapStartIndex);
                    
                    cursorX = 0f;
                    cursorY += lineSpacing;
                    prevCodePoint = 0;

                    // Re-position the wrapped glyphs on the new line
                    for (int wrapIndex = wrapStartIndex; wrapIndex < previousLineEnd; wrapIndex++)
                    {
                        var wg = Glyphs[wrapIndex];
                        var remapped = wg;
                        float shift = wg.Position.X - lastWordStartCursorX;
                        remapped.Position = new Vector2(shift, cursorY + fontAscent + remapped.Glyph.BearY);
                        Glyphs.Add(remapped);
                        cursorX = shift + remapped.Glyph.Advance;
                        prevCodePoint = remapped.CodePoint;
                    }

                    Glyphs.RemoveRange(wrapStartIndex, wrapCount);
                    currentLineStart = wrapStartIndex;
                    
                    // Add the current character
                    if (prevCodePoint != 0)
                    {
                        cursorX += Font.GetKerning(prevCodePoint, codePoint, FontSize);
                    }
                    var glyphPos = new Vector2(cursorX + glyph.BearX, cursorY + fontAscent + glyph.BearY);
                    Glyphs.Add(new TextRunGlyph { Character = c, CodePoint = codePoint, Position = glyphPos, Glyph = glyph, Font = resolvedFont });
                    cursorX += glyph.Advance;
                    prevCodePoint = codePoint;
                    lastWordStartIndex = currentLineStart;
                    lastWordStartCursorX = 0f;
                    continue;
                }
                else
                {
                    // Hard wrap (word is longer than MaxWidth)
                    AddLineRange(lines, currentLineStart, Glyphs.Count);
                    currentLineStart = Glyphs.Count;
                    cursorX = 0f;
                    cursorY += lineSpacing;
                    prevCodePoint = 0;
                    lastWordStartIndex = codePoint == ' ' || codePoint == '\t' ? -1 : currentLineStart;
                    lastWordStartCursorX = 0f;
                }
            }

            // Position calculation (Y is offset by the ascender height so baseline aligns perfectly)
            var pos = new Vector2(cursorX + glyph.BearX, cursorY + fontAscent + glyph.BearY);
            Glyphs.Add(new TextRunGlyph { Character = c, CodePoint = codePoint, Position = pos, Glyph = glyph, Font = resolvedFont });
            cursorX += glyph.Advance;
            prevCodePoint = codePoint;
        }

        AddLineRange(lines, currentLineStart, Glyphs.Count);

        // Apply Horizontal Alignments and calculate layout size
        float maxLineWidth = 0f;
        float totalHeight = cursorY + lineSpacing;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            int lineEnd = line.Start + line.Count;

            float lineWidth = 0f;
            for (int glyphIndex = line.Start; glyphIndex < lineEnd; glyphIndex++)
            {
                var g = Glyphs[glyphIndex];
                lineWidth = Math.Max(lineWidth, g.Position.X - g.Glyph.BearX + g.Glyph.Advance);
            }
            maxLineWidth = Math.Max(maxLineWidth, lineWidth);

            // Calculate shift for Center/Right alignment
            float shiftX = 0f;
            if (Alignment == TextAlignment.Center)
            {
                shiftX = (MaxWidth - lineWidth) / 2.0f;
            }
            else if (Alignment == TextAlignment.Right)
            {
                shiftX = MaxWidth - lineWidth;
            }

            if (shiftX > 0f && !float.IsInfinity(shiftX))
            {
                for (int glyphIndex = line.Start; glyphIndex < lineEnd; glyphIndex++)
                {
                    var remap = Glyphs[glyphIndex];
                    remap.Position.X += shiftX;
                    Glyphs[glyphIndex] = remap;
                }
            }
        }

        MeasuredSize = new Vector2(
            float.IsInfinity(MaxWidth) ? maxLineWidth : MaxWidth, 
            totalHeight
        );
    }
}
