#if AVALONIA11
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace Avalonia.ProGpu
{
    internal sealed class TextShaperImpl : ITextShaperImpl
    {
        [ThreadStatic]
        private static Buffer? s_buffer;

        private static readonly ConcurrentDictionary<int, Language> s_cachedLanguages = new();

        public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
        {
            var typeface = (ProGpuTypeface)options.Typeface;
            var buffer = s_buffer ??= new Buffer();
            buffer.Reset();

            var containingText = GetContainingMemory(text, out var start, out var length).Span;
            buffer.AddUtf16(containingText, start, length);
            buffer.GuessSegmentProperties();
            buffer.Direction = (options.BidiLevel & 1) == 0
                ? Direction.LeftToRight
                : Direction.RightToLeft;

            var culture = options.Culture ?? CultureInfo.CurrentCulture;
            buffer.Language = s_cachedLanguages.GetOrAdd(
                culture.LCID,
                static (_, value) => new Language(value),
                culture);

            lock (typeface.ShapingFont)
            {
                typeface.ShapingFont.Shape(buffer, GetFeatures(options));
            }

            if (buffer.Direction == Direction.RightToLeft)
            {
                buffer.Reverse();
            }

            typeface.ShapingFont.GetScale(out var scaleX, out _);
            var textScale = options.FontRenderingEmSize / scaleX;
            var bufferLength = buffer.Length;
            var shapedBuffer = new ShapedBuffer(
                text,
                bufferLength,
                typeface,
                options.FontRenderingEmSize,
                options.BidiLevel);
            var glyphInfos = buffer.GetGlyphInfoSpan();
            var glyphPositions = buffer.GetGlyphPositionSpan();

            for (var i = 0; i < bufferLength; i++)
            {
                var sourceInfo = glyphInfos[i];
                var glyphIndex = (ushort)sourceInfo.Codepoint;
                var glyphCluster = (int)sourceInfo.Cluster;
                var position = glyphPositions[i];
                var glyphAdvance = position.XAdvance * textScale + options.LetterSpacing;
                var glyphOffset = new Vector(
                    position.XOffset * textScale,
                    -position.YOffset * textScale);

                if (glyphCluster < containingText.Length && containingText[glyphCluster] == '\t')
                {
                    glyphIndex = typeface.GetGlyph(' ');
                    glyphAdvance = options.IncrementalTabWidth > 0
                        ? options.IncrementalTabWidth
                        : 4 * typeface.GetGlyphAdvance(glyphIndex) * textScale;
                }

                shapedBuffer[i] = new Avalonia.Media.TextFormatting.GlyphInfo(
                    glyphIndex,
                    glyphCluster,
                    glyphAdvance,
                    glyphOffset);
            }

            return shapedBuffer;
        }

        private static ReadOnlyMemory<char> GetContainingMemory(
            ReadOnlyMemory<char> memory,
            out int start,
            out int length)
        {
            if (MemoryMarshal.TryGetString(memory, out var containingString, out start, out length))
            {
                return containingString.AsMemory();
            }

            if (MemoryMarshal.TryGetArray(memory, out var segment))
            {
                start = segment.Offset;
                length = segment.Count;
                return segment.Array.AsMemory();
            }

            if (MemoryMarshal.TryGetMemoryManager(
                    memory,
                    out System.Buffers.MemoryManager<char>? memoryManager,
                    out start,
                    out length))
            {
                return memoryManager.Memory;
            }

            throw new InvalidOperationException("Text memory is not backed by a string, array, or memory manager.");
        }

        private static Feature[] GetFeatures(TextShaperOptions options)
        {
            if (options.FontFeatures is null || options.FontFeatures.Count == 0)
            {
                return Array.Empty<Feature>();
            }

            var features = new Feature[options.FontFeatures.Count];
            for (var i = 0; i < options.FontFeatures.Count; i++)
            {
                var feature = options.FontFeatures[i];
                features[i] = new Feature(
                    Tag.Parse(feature.Tag),
                    (uint)feature.Value,
                    (uint)feature.Start,
                    (uint)feature.End);
            }

            return features;
        }
    }
}
#endif
