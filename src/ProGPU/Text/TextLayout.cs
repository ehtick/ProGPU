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
}

public class TextLayout
{
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

        // Layout metric scales
        float scale = FontSize / Font.UnitsPerEm;
        float lineSpacing = (Font.Ascender - Font.Descender + Font.LineGap) * scale;
        float fontAscent = Font.Ascender * scale;

        var lines = new List<List<TextRunGlyph>>();
        var currentLine = new List<TextRunGlyph>();

        float cursorX = 0f;
        float cursorY = 0f;
        uint prevCodePoint = 0;

        // Keep track of words to enable word wrapping
        int lastWordStartIdxInLine = -1;
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
                lines.Add(currentLine);
                currentLine = new List<TextRunGlyph>();
                cursorX = 0f;
                cursorY += lineSpacing;
                prevCodePoint = 0;
                lastWordStartIdxInLine = -1;
                continue;
            }

            ushort glyphIdx = Font.GetGlyphIndex(codePoint);
            float advance = Font.GetAdvanceWidth(glyphIdx, FontSize);
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
                lastWordStartIdxInLine = -1;
            }
            else if (lastWordStartIdxInLine == -1)
            {
                lastWordStartIdxInLine = currentLine.Count;
                lastWordStartCursorX = cursorX;
            }

            // Auto-wrapping logic on layout boundary
            if (cursorX + glyph.Advance > MaxWidth && cursorX > 0)
            {
                if (lastWordStartIdxInLine > 0)
                {
                    // Wrap the last partial word to the next line
                    int wrapCount = currentLine.Count - lastWordStartIdxInLine;
                    var wrappedGlyphs = currentLine.GetRange(lastWordStartIdxInLine, wrapCount);
                    currentLine.RemoveRange(lastWordStartIdxInLine, wrapCount);

                    lines.Add(currentLine);
                    currentLine = new List<TextRunGlyph>();
                    
                    cursorX = 0f;
                    cursorY += lineSpacing;
                    prevCodePoint = 0;

                    // Re-position the wrapped glyphs on the new line
                    foreach (var wg in wrappedGlyphs)
                    {
                        var remapped = wg;
                        float shift = wg.Position.X - lastWordStartCursorX;
                        remapped.Position = new Vector2(shift, cursorY + fontAscent + remapped.Glyph.BearY);
                        currentLine.Add(remapped);
                        cursorX = shift + remapped.Glyph.Advance;
                        prevCodePoint = remapped.CodePoint;
                    }
                    
                    // Add the current character
                    if (prevCodePoint != 0)
                    {
                        cursorX += Font.GetKerning(prevCodePoint, codePoint, FontSize);
                    }
                    var glyphPos = new Vector2(cursorX + glyph.BearX, cursorY + fontAscent + glyph.BearY);
                    currentLine.Add(new TextRunGlyph { Character = c, CodePoint = codePoint, Position = glyphPos, Glyph = glyph });
                    cursorX += glyph.Advance;
                    prevCodePoint = codePoint;
                    lastWordStartIdxInLine = 0;
                    lastWordStartCursorX = 0f;
                    continue;
                }
                else
                {
                    // Hard wrap (word is longer than MaxWidth)
                    lines.Add(currentLine);
                    currentLine = new List<TextRunGlyph>();
                    cursorX = 0f;
                    cursorY += lineSpacing;
                    prevCodePoint = 0;
                }
            }

            // Position calculation (Y is offset by the ascender height so baseline aligns perfectly)
            var pos = new Vector2(cursorX + glyph.BearX, cursorY + fontAscent + glyph.BearY);
            currentLine.Add(new TextRunGlyph { Character = c, CodePoint = codePoint, Position = pos, Glyph = glyph });
            cursorX += glyph.Advance;
            prevCodePoint = codePoint;
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        // Apply Horizontal Alignments and calculate layout size
        float maxLineWidth = 0f;
        float totalHeight = cursorY + lineSpacing;

        foreach (var line in lines)
        {
            if (line.Count == 0) continue;

            float lineWidth = 0f;
            foreach (var g in line)
            {
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
                for (int j = 0; j < line.Count; j++)
                {
                    var remap = line[j];
                    remap.Position.X += shiftX;
                    line[j] = remap;
                }
            }

            // Flatten all glyphs to output list
            Glyphs.AddRange(line);
        }

        MeasuredSize = new Vector2(
            float.IsInfinity(MaxWidth) ? maxLineWidth : MaxWidth, 
            totalHeight
        );
    }
}
