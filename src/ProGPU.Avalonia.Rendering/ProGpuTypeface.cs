using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using ProGPU.Text;

namespace Avalonia.ProGpu
{
    internal class ProGpuTypeface :
#if AVALONIA11
        IGlyphTypeface
#else
        IPlatformTypeface
#endif
    {
        public TtfFont Font { get; }
        private readonly byte[] _fontData;
#if AVALONIA11
        private readonly HarfBuzzSharp.Blob _shapingBlob;
        private readonly HarfBuzzSharp.Face _shapingFace;
        internal HarfBuzzSharp.Font ShapingFont { get; }
#endif
        public FontSimulations FontSimulations { get; }
        public string FamilyName { get; }
        public FontWeight Weight { get; }
        public FontStyle Style { get; }
        public FontStretch Stretch { get; }
#if AVALONIA11
        public int GlyphCount => Font.NumGlyphs;
        public FontMetrics Metrics { get; }
#endif

        public ProGpuTypeface(TtfFont font, byte[] fontData, string familyName, FontWeight weight, FontStyle style, FontStretch stretch, FontSimulations fontSimulations = FontSimulations.None)
        {
            Font = font ?? throw new ArgumentNullException(nameof(font));
            _fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
            FamilyName = familyName;
            Weight = weight;
            Style = style;
            Stretch = stretch;
            FontSimulations = fontSimulations;
#if AVALONIA11
            _shapingBlob = HarfBuzzSharp.Blob.FromStream(new MemoryStream(_fontData, writable: false));
            _shapingFace = new HarfBuzzSharp.Face(_shapingBlob, font.FaceIndex);
            ShapingFont = new HarfBuzzSharp.Font(_shapingFace);
            ShapingFont.SetFunctionsOpenType();
            Metrics = new FontMetrics
            {
                DesignEmHeight = (short)font.UnitsPerEm,
                Ascent = -font.Ascender,
                Descent = -font.Descender,
                LineGap = font.LineGap,
                UnderlinePosition = -(font.UnderlinePosition ?? 0),
                UnderlineThickness = font.UnderlineThickness ?? 0,
                StrikethroughPosition = -(font.StrikeoutPosition ?? 0),
                StrikethroughThickness = font.StrikeoutThickness ?? 0,
                IsFixedPitch = font.IsFixedPitch
            };
#endif
        }

#if AVALONIA11
        public bool TryGetGlyphMetrics(ushort glyph, out GlyphMetrics metrics)
        {
            metrics = default;
            if (!Font.TryGetGlyphBounds(glyph, out var xMin, out var yMin, out var xMax, out var yMax))
            {
                return false;
            }

            metrics = new GlyphMetrics
            {
                XBearing = xMin,
                YBearing = yMax,
                Width = xMax - xMin,
                Height = yMax - yMin
            };
            return true;
        }

        public ushort GetGlyph(uint codepoint) => Font.GetGlyphIndex(codepoint);

        public bool TryGetGlyph(uint codepoint, out ushort glyph)
        {
            glyph = GetGlyph(codepoint);
            return glyph != 0;
        }

        public ushort[] GetGlyphs(ReadOnlySpan<uint> codepoints)
        {
            var glyphs = new ushort[codepoints.Length];
            for (var i = 0; i < codepoints.Length; i++)
            {
                glyphs[i] = GetGlyph(codepoints[i]);
            }

            return glyphs;
        }

        public int GetGlyphAdvance(ushort glyph) =>
            (int)Math.Round(Font.GetAdvanceWidth(glyph, Font.UnitsPerEm));

        public int[] GetGlyphAdvances(ReadOnlySpan<ushort> glyphs)
        {
            var advances = new int[glyphs.Length];
            for (var i = 0; i < glyphs.Length; i++)
            {
                advances[i] = GetGlyphAdvance(glyphs[i]);
            }

            return advances;
        }

        public bool TryGetTable(uint tag, out byte[] table)
#else
        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
#endif
        {
#if !AVALONIA11
            var value = (uint)tag;
#else
            var value = tag;
#endif
            var tableTag = new string(new[]
            {
                (char)((value >> 24) & 0xFF),
                (char)((value >> 16) & 0xFF),
                (char)((value >> 8) & 0xFF),
                (char)(value & 0xFF)
            });
            if (Font.TryGetTable(tableTag, out var memory))
            {
#if AVALONIA11
                table = memory.ToArray();
#else
                table = memory;
#endif
                return true;
            }

            var reversedTag = new string(new[]
            {
                tableTag[3],
                tableTag[2],
                tableTag[1],
                tableTag[0]
            });
            if (Font.TryGetTable(reversedTag, out memory))
            {
#if AVALONIA11
                table = memory.ToArray();
#else
                table = memory;
#endif
                return true;
            }

#if AVALONIA11
            table = Array.Empty<byte>();
#else
            table = default;
#endif
            return false;
        }

        public bool TryGetStream([NotNullWhen(true)] out Stream? stream)
        {
            try
            {
                stream = new MemoryStream(_fontData);
                return true;
            }
            catch
            {
                stream = null;
                return false;
            }
        }

        public void Dispose()
        {
#if AVALONIA11
            ShapingFont.Dispose();
            _shapingFace.Dispose();
            _shapingBlob.Dispose();
#endif
        }
    }
}
