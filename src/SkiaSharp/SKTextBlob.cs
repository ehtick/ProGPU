using System;

namespace SkiaSharp;

public sealed class SKTextBlobRun
{
    public SKFont Font { get; }
    public ushort[] GlyphIndices { get; }
    public SKPoint[] GlyphPositions { get; }

    public SKTextBlobRun(SKFont font, ushort[] glyphIndices, SKPoint[] glyphPositions)
    {
        Font = font;
        GlyphIndices = glyphIndices;
        GlyphPositions = glyphPositions;
    }
}

public class SKTextBlob : IDisposable
{
    public SKTextBlobRun[] Runs { get; }
    public SKFont Font => Runs[0].Font;
    public ushort[] GlyphIndices { get; }
    public SKPoint[] GlyphPositions { get; }

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

    public float[] GetIntercepts(float lowerLimit, float upperLimit)
    {
        // Intercepts are used for text underline/strikeout intersections; we return an empty array for basic support.
        return Array.Empty<float>();
    }

    public void Dispose() { }
}

public class PositionedRunBuffer
{
    public SKFont Font { get; }
    public ushort[] Glyphs { get; }
    public SKPoint[] Positions { get; }

    public PositionedRunBuffer(SKFont font, int count)
    {
        Font = font;
        Glyphs = new ushort[count];
        Positions = new SKPoint[count];
    }

    public void SetPositions(ReadOnlySpan<SKPoint> positions)
    {
        positions.CopyTo(Positions);
    }

    public void SetPositions(SKPoint[] positions)
    {
        Array.Copy(positions, Positions, Positions.Length);
    }

    public void SetGlyphs(ReadOnlySpan<ushort> glyphs)
    {
        glyphs.CopyTo(Glyphs);
    }

    public void SetGlyphs(ushort[] glyphs)
    {
        Array.Copy(glyphs, Glyphs, Glyphs.Length);
    }
}

public class SKTextBlobBuilder : IDisposable
{
    private readonly System.Collections.Generic.List<PositionedRunBuffer> _runs = new();

    public PositionedRunBuffer AllocatePositionedRun(SKFont font, int count)
    {
        var run = new PositionedRunBuffer(font, count);
        _runs.Add(run);
        return run;
    }

    public SKTextBlob? Build()
    {
        if (_runs.Count == 0) return null;
        var runs = new SKTextBlobRun[_runs.Count];
        for (int i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            var glyphs = new ushort[run.Glyphs.Length];
            var positions = new SKPoint[run.Positions.Length];
            Array.Copy(run.Glyphs, glyphs, glyphs.Length);
            Array.Copy(run.Positions, positions, positions.Length);
            runs[i] = new SKTextBlobRun(run.Font, glyphs, positions);
        }

        var blob = new SKTextBlob(runs);
        _runs.Clear();
        return blob;
    }

    public void Dispose()
    {
        _runs.Clear();
    }
}

public class SKTextBlobBuilderCache
{
    private static readonly SKTextBlobBuilderCache _shared = new();
    public static SKTextBlobBuilderCache Shared => _shared;

    public SKTextBlobBuilder Get() => new();
    public void Return(SKTextBlobBuilder builder) => builder.Dispose();
}
