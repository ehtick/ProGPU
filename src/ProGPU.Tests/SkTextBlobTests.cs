using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkTextBlobTests
{
    [Fact]
    public void BuilderPreservesAllPositionedRuns()
    {
        using var builder = new SKTextBlobBuilder();
        using var font = new SKFont(SKTypeface.Default, 12f);
        using var fallbackFont = new SKFont(SKTypeface.Default, 24f);

        var first = builder.AllocatePositionedRun(font, 2);
        first.SetGlyphs(new ushort[] { 10, 11 });
        first.SetPositions(new[] { new SKPoint(1f, 2f), new SKPoint(3f, 4f) });

        var second = builder.AllocatePositionedRun(fallbackFont, 1);
        second.SetGlyphs(new ushort[] { 20 });
        second.SetPositions(new[] { new SKPoint(5f, 6f) });

        using var blob = builder.Build();

        Assert.NotNull(blob);
        Assert.Equal(2, blob.Runs.Length);
        Assert.Equal(new ushort[] { 10, 11 }, blob.Runs[0].GlyphIndices);
        Assert.Equal(new ushort[] { 20 }, blob.Runs[1].GlyphIndices);
        Assert.Equal(12f, blob.Runs[0].Font.Size);
        Assert.Equal(24f, blob.Runs[1].Font.Size);
        Assert.Equal(new ushort[] { 10, 11, 20 }, blob.GlyphIndices);
        Assert.Equal(3, blob.GlyphPositions.Length);
    }

    [Fact]
    public void BuildSnapshotsRunBuffersAndClearsBuilder()
    {
        using var builder = new SKTextBlobBuilder();
        using var font = new SKFont(SKTypeface.Default, 12f);

        var run = builder.AllocatePositionedRun(font, 1);
        run.SetGlyphs(new ushort[] { 42 });
        run.SetPositions(new[] { new SKPoint(7f, 8f) });

        using var blob = builder.Build();
        run.Glyphs[0] = 99;
        run.Positions[0] = new SKPoint(9f, 10f);

        Assert.NotNull(blob);
        Assert.Equal(new ushort[] { 42 }, blob.Runs[0].GlyphIndices);
        Assert.Equal(7f, blob.Runs[0].GlyphPositions[0].X);
        Assert.Null(builder.Build());
    }
}
