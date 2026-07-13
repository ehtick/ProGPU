using System;
using System.Threading;

namespace SkiaSharp;

public sealed class SKTextBlobRun
{
    public SKFont Font { get; }
    public ushort[] GlyphIndices { get; }
    public SKPoint[] GlyphPositions { get; }
    public SKRotationScaleMatrix[]? RotationScaleMatrices { get; }

    public SKTextBlobRun(SKFont font, ushort[] glyphIndices, SKPoint[] glyphPositions)
        : this(font, glyphIndices, glyphPositions, null)
    {
    }

    public SKTextBlobRun(
        SKFont font,
        ushort[] glyphIndices,
        SKPoint[] glyphPositions,
        SKRotationScaleMatrix[]? rotationScaleMatrices)
    {
        Font = font;
        GlyphIndices = glyphIndices;
        GlyphPositions = glyphPositions;
        RotationScaleMatrices = rotationScaleMatrices;
    }
}

public partial class SKTextBlob : IDisposable
{
    private static int s_nextUniqueId;
    private SKRect? _bounds;

    public IntPtr Handle { get; } = SKObjectHandle.Create();
    public SKTextBlobRun[] Runs { get; }
    public SKFont Font => Runs[0].Font;
    public ushort[] GlyphIndices { get; }
    public SKPoint[] GlyphPositions { get; }
    public SKRect Bounds => _bounds ??= ComputeBounds();
    public uint UniqueId { get; } = unchecked((uint)Interlocked.Increment(ref s_nextUniqueId));
    internal bool HasEmboldenedRuns { get; }

    public SKTextBlob(SKFont font, ushort[] glyphIndices, SKPoint[] glyphPositions)
        : this(new[] { new SKTextBlobRun(font, glyphIndices, glyphPositions) })
    {
    }

    public SKTextBlob(SKTextBlobRun[] runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Length == 0)
        {
            throw new ArgumentException("Text blob requires at least one run.", nameof(runs));
        }

        Runs = runs;
        var glyphCount = 0;
        foreach (var run in runs)
        {
            glyphCount += run.GlyphIndices.Length;
            HasEmboldenedRuns |= run.Font.Embolden;
        }

        GlyphIndices = new ushort[glyphCount];
        GlyphPositions = new SKPoint[glyphCount];

        var offset = 0;
        foreach (var run in runs)
        {
            Array.Copy(run.GlyphIndices, 0, GlyphIndices, offset, run.GlyphIndices.Length);
            Array.Copy(run.GlyphPositions, 0, GlyphPositions, offset, run.GlyphPositions.Length);
            offset += run.GlyphIndices.Length;
        }
    }

    public static SKTextBlob? Create(
        string text,
        SKFont font,
        SKPoint origin = default) =>
        Create(text.AsSpan(), font, origin);

    public static SKTextBlob? Create(
        ReadOnlySpan<char> text,
        SKFont font,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(font.GetGlyphs(text), font, font.GetGlyphPositions(text, origin));
    }

    public static SKTextBlob? Create(
        IntPtr text,
        int length,
        SKTextEncoding encoding,
        SKFont font,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(
            font.GetGlyphs(text, length, encoding),
            font,
            font.GetGlyphPositions(text, length, encoding, origin));
    }

    public static SKTextBlob? Create(
        ReadOnlySpan<byte> text,
        SKTextEncoding encoding,
        SKFont font,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(
            font.GetGlyphs(text, encoding),
            font,
            font.GetGlyphPositions(text, encoding, origin));
    }

    public static SKTextBlob? CreateHorizontal(
        string text,
        SKFont font,
        ReadOnlySpan<float> positions,
        float y) =>
        CreateHorizontal(text.AsSpan(), font, positions, y);

    public static SKTextBlob? CreateHorizontal(
        ReadOnlySpan<char> text,
        SKFont font,
        ReadOnlySpan<float> positions,
        float y)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateHorizontalCore(font.GetGlyphs(text), font, positions, y);
    }

    public static SKTextBlob? CreateHorizontal(
        IntPtr text,
        int length,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<float> positions,
        float y)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateHorizontalCore(font.GetGlyphs(text, length, encoding), font, positions, y);
    }

    public static SKTextBlob? CreateHorizontal(
        ReadOnlySpan<byte> text,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<float> positions,
        float y)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateHorizontalCore(font.GetGlyphs(text, encoding), font, positions, y);
    }

    public static SKTextBlob? CreatePositioned(
        string text,
        SKFont font,
        ReadOnlySpan<SKPoint> positions) =>
        CreatePositioned(text.AsSpan(), font, positions);

    public static SKTextBlob? CreatePositioned(
        ReadOnlySpan<char> text,
        SKFont font,
        ReadOnlySpan<SKPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(font.GetGlyphs(text), font, positions);
    }

    public static SKTextBlob? CreatePositioned(
        IntPtr text,
        int length,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<SKPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(font.GetGlyphs(text, length, encoding), font, positions);
    }

    public static SKTextBlob? CreatePositioned(
        ReadOnlySpan<byte> text,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<SKPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePositionedCore(font.GetGlyphs(text, encoding), font, positions);
    }

    public static SKTextBlob? CreateRotationScale(
        ReadOnlySpan<char> text,
        SKFont font,
        ReadOnlySpan<SKRotationScaleMatrix> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateRotationScaleCore(font.GetGlyphs(text), font, positions);
    }

    public static SKTextBlob? CreateRotationScale(
        string text,
        SKFont font,
        ReadOnlySpan<SKRotationScaleMatrix> positions)
    {
        return CreateRotationScale(text.AsSpan(), font, positions);
    }

    public static SKTextBlob? CreateRotationScale(
        IntPtr text,
        int length,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<SKRotationScaleMatrix> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateRotationScaleCore(font.GetGlyphs(text, length, encoding), font, positions);
    }

    public static SKTextBlob? CreateRotationScale(
        ReadOnlySpan<byte> text,
        SKTextEncoding encoding,
        SKFont font,
        ReadOnlySpan<SKRotationScaleMatrix> positions)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreateRotationScaleCore(font.GetGlyphs(text, encoding), font, positions);
    }

    public static SKTextBlob? CreatePathPositioned(
        string text,
        SKFont font,
        SKPath path,
        SKTextAlign textAlign = SKTextAlign.Left,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CreatePathPositioned(text.AsSpan(), font, path, textAlign, origin);
    }

    public static SKTextBlob? CreatePathPositioned(
        ReadOnlySpan<char> text,
        SKFont font,
        SKPath path,
        SKTextAlign textAlign = SKTextAlign.Left,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePathPositioned(font.GetGlyphs(text), font, path, textAlign, origin);
    }

    public static SKTextBlob? CreatePathPositioned(
        ReadOnlySpan<byte> text,
        SKTextEncoding encoding,
        SKFont font,
        SKPath path,
        SKTextAlign textAlign = SKTextAlign.Left,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        return CreatePathPositioned(font.GetGlyphs(text, encoding), font, path, textAlign, origin);
    }

    public static unsafe SKTextBlob? CreatePathPositioned(
        IntPtr text,
        int length,
        SKTextEncoding encoding,
        SKFont font,
        SKPath path,
        SKTextAlign textAlign = SKTextAlign.Left,
        SKPoint origin = default)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (text == IntPtr.Zero || length <= 0)
        {
            return null;
        }

        return CreatePathPositioned(
            new ReadOnlySpan<byte>((void*)text, length),
            encoding,
            font,
            path,
            textAlign,
            origin);
    }

    private static SKTextBlob? CreatePathPositioned(
        ushort[] glyphs,
        SKFont font,
        SKPath path,
        SKTextAlign textAlign,
        SKPoint origin)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (glyphs.Length == 0)
        {
            return null;
        }

        var widths = font.GetGlyphWidths(glyphs);
        var offsets = font.GetGlyphPositions(glyphs, origin);
        using var builder = new SKTextBlobBuilder();
        builder.AddPathPositionedRun(glyphs, font, widths, offsets, path, textAlign);
        return builder.Build();
    }

    private static SKTextBlob? CreateHorizontalCore(
        ushort[] glyphs,
        SKFont font,
        ReadOnlySpan<float> positions,
        float y)
    {
        if (glyphs.Length == 0)
        {
            return null;
        }

        var horizontalPositions = new float[glyphs.Length];
        positions.CopyTo(horizontalPositions);
        var points = new SKPoint[glyphs.Length];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new SKPoint(horizontalPositions[index], y);
        }

        return new SKTextBlob(font, glyphs, points);
    }

    private static SKTextBlob? CreatePositionedCore(
        ushort[] glyphs,
        SKFont font,
        ReadOnlySpan<SKPoint> positions)
    {
        if (glyphs.Length == 0)
        {
            return null;
        }

        var points = new SKPoint[glyphs.Length];
        positions.CopyTo(points);
        return new SKTextBlob(font, glyphs, points);
    }

    private static SKTextBlob? CreateRotationScaleCore(
        ushort[] glyphs,
        SKFont font,
        ReadOnlySpan<SKRotationScaleMatrix> positions)
    {
        if (glyphs.Length == 0)
        {
            return null;
        }

        var matrices = new SKRotationScaleMatrix[glyphs.Length];
        positions.CopyTo(matrices);
        var points = new SKPoint[matrices.Length];
        for (var index = 0; index < matrices.Length; index++)
        {
            points[index] = new SKPoint(matrices[index].TX, matrices[index].TY);
        }

        return new SKTextBlob(new[]
        {
            new SKTextBlobRun(font, glyphs, points, matrices),
        });
    }

    private SKRect ComputeBounds()
    {
        var bounds = SKRect.Empty;
        var hasBounds = false;
        foreach (var run in Runs)
        {
            var glyphCount = Math.Min(run.GlyphIndices.Length, run.GlyphPositions.Length);
            for (var index = 0; index < glyphCount; index++)
            {
                using var glyphPath = run.Font.GetGlyphPath(run.GlyphIndices[index]);
                if (glyphPath is null || glyphPath.IsEmpty)
                {
                    continue;
                }

                SKRect glyphBounds;
                if (run.RotationScaleMatrices is { } matrices && index < matrices.Length)
                {
                    using var transformed = new SKPath(glyphPath);
                    transformed.Transform(matrices[index].ToMatrix());
                    glyphBounds = transformed.Bounds;
                }
                else
                {
                    var position = run.GlyphPositions[index];
                    glyphBounds = glyphPath.Bounds;
                    glyphBounds.Offset(position.X, position.Y);
                }

                if (!hasBounds)
                {
                    bounds = glyphBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Union(glyphBounds);
                }
            }
        }

        return hasBounds ? bounds : SKRect.Empty;
    }

    public void Dispose() { }
}

public class SKTextBlobBuilderCache
{
    private static readonly SKTextBlobBuilderCache _shared = new();
    public static SKTextBlobBuilderCache Shared => _shared;

    public SKTextBlobBuilder Get() => new();
    public void Return(SKTextBlobBuilder builder) => builder.Dispose();
}
