using System.Runtime.InteropServices;
using ProGPU.Fonts.Inter;
using ProGPU.Compute;
using ProGPU.Text;
using ProGPU.Text.Shaping;
using Xunit;

namespace ProGPU.Tests;

public sealed class ShapingContractsTests
{
    [Fact]
    public void OpenTypeTagRoundTripsFontByteOrder()
    {
        var tag = new OpenTypeTag("kern");

        Assert.Equal(0x6b65726eu, tag.Value);
        Assert.Equal("kern", tag.ToString());
        Assert.True(OpenTypeTag.TryParse("mark", out OpenTypeTag parsed));
        Assert.Equal(new OpenTypeTag("mark"), parsed);
        Assert.False(OpenTypeTag.TryParse("bad", out _));
    }

    [Fact]
    public void FeatureRangesAreHalfOpen()
    {
        var feature = new ShapingFeature(new OpenTypeTag("liga"), value: 1, start: 3, end: 5);

        Assert.False(feature.AppliesTo(2));
        Assert.True(feature.AppliesTo(3));
        Assert.True(feature.AppliesTo(4));
        Assert.False(feature.AppliesTo(5));
    }

    [Fact]
    public void GlyphRecordHasStableShaderInterchangeLayout()
    {
        Assert.Equal(32, Marshal.SizeOf<ShapingGlyph>());
        Assert.Equal(0, Marshal.OffsetOf<ShapingGlyph>(nameof(ShapingGlyph.GlyphId)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<ShapingGlyph>(nameof(ShapingGlyph.AdvanceX)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<ShapingGlyph>(nameof(ShapingGlyph.OffsetY)).ToInt32());
    }

    [Fact]
    public void BufferReplaceSupportsExpansionAndContraction()
    {
        using var buffer = new ShapingBuffer(initialCapacity: 2, maximumGlyphCount: 16);
        buffer.Append(Glyph(1));
        buffer.Append(Glyph(2));
        buffer.Append(Glyph(3));

        buffer.Replace(1, 1, [Glyph(20), Glyph(21), Glyph(22)]);
        Assert.Equal(new uint[] { 1, 20, 21, 22, 3 }, buffer.Glyphs.ToArray().Select(static glyph => glyph.GlyphId));

        buffer.Replace(1, 3, [Glyph(9)]);
        Assert.Equal(new uint[] { 1, 9, 3 }, buffer.Glyphs.ToArray().Select(static glyph => glyph.GlyphId));
    }

    [Fact]
    public void BufferEnforcesConfiguredExpansionLimit()
    {
        using var buffer = new ShapingBuffer(initialCapacity: 1, maximumGlyphCount: 2);
        buffer.Append(Glyph(1));
        buffer.Append(Glyph(2));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => buffer.Append(Glyph(3)));
        Assert.Contains("glyph limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutorRequestsRequireResolvedDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ShapingRequest(ShapingDirection.Unspecified, OpenTypeTag.DefaultScript));

        var request = new ShapingRequest(
            ShapingDirection.RightToLeft,
            new OpenTypeTag("arab"),
            language: "ar",
            flags: ShapingBufferFlags.BeginningOfText | ShapingBufferFlags.EndOfText);

        Assert.Equal(ShapingDirection.RightToLeft, request.Direction);
        Assert.Equal("arab", request.Script.ToString());
        Assert.Equal("ar", request.Language);
    }

    [Fact]
    public void ExistingFontAdapterExposesTablesMetricsAndNominalGlyphs()
    {
        var face = new TtfShapingFontFace(InterFontFamily.Regular);

        uint glyph = face.GetNominalGlyph('A');
        Assert.NotEqual(0u, glyph);
        Assert.Equal(InterFontFamily.Regular.UnitsPerEm, face.UnitsPerEm);
        Assert.Equal(InterFontFamily.Regular.NumGlyphs, face.GlyphCount);
        Assert.True(face.GetHorizontalAdvance(glyph) > 0);
        Assert.True(face.GetVerticalAdvance(glyph) > 0);
        Assert.True(face.GetHorizontalOrigin(glyph) > 0);
        Assert.True(face.TryGetTable(new OpenTypeTag("GSUB"), out ReadOnlyMemory<byte> table));
        Assert.False(table.IsEmpty);
        Assert.False(face.TryGetVariationGlyph('A', 0xfe0f, out _));
    }

    [Fact]
    public void CpuExecutorProducesTheRendererResultInDesignUnits()
    {
        TtfFont font = InterFontFamily.Regular;
        const string text = "office";
        var request = new ShapingRequest(
            ShapingDirection.LeftToRight,
            new OpenTypeTag("latn"),
            language: "en");
        using var buffer = new ShapingBuffer();

        CpuOpenTypeShaper.Instance.Shape(text, new TtfShapingFontFace(font), request, buffer);
        IReadOnlyList<ShapedGlyph> renderer = OpenTypeTextShaper.Shape(text, font, font.UnitsPerEm,
            new TextShapingOptions
            {
                Script = "latn",
                Language = "en",
                Direction = ShapingDirection.LeftToRight
            });

        Assert.Equal(renderer.Count, buffer.Count);
        for (var index = 0; index < renderer.Count; index++)
        {
            ShapedGlyph expected = renderer[index];
            ShapingGlyph actual = buffer[index];
            Assert.Equal(expected.GlyphIndex, actual.GlyphId);
            Assert.Equal(expected.CodePoint, actual.CodePoint);
            Assert.Equal(expected.Cluster, actual.Cluster);
            Assert.Equal(expected.AdvanceX, actual.AdvanceX);
            Assert.Equal(expected.AdvanceY, actual.AdvanceY);
            Assert.Equal(expected.OffsetX, actual.OffsetX);
            Assert.Equal(expected.OffsetY, actual.OffsetY);
        }
    }

    [Fact]
    public void CpuExecutorAppliesFeaturesToHalfOpenInputRanges()
    {
        var face = new TtfShapingFontFace(InterFontFamily.Regular);
        using var ranged = new ShapingBuffer();
        using var enabled = new ShapingBuffer();
        using var disabled = new ShapingBuffer();
        const string text = "33";

        CpuOpenTypeShaper.Instance.Shape(text, face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                features: new[] { new ShapingFeature(new OpenTypeTag("ss01"), 1, 0, 1) }),
            ranged);
        CpuOpenTypeShaper.Instance.Shape(text, face,
            new ShapingRequest(ShapingDirection.LeftToRight, new OpenTypeTag("latn")),
            enabled);
        CpuOpenTypeShaper.Instance.Shape(text, face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                features: new[] { new ShapingFeature(new OpenTypeTag("ss01"), 1) }),
            disabled);

        Assert.Equal(2, ranged.Count);
        Assert.NotEqual(enabled[0].GlyphId, ranged[0].GlyphId);
        Assert.Equal(enabled[1].GlyphId, ranged[1].GlyphId);
        Assert.Equal(disabled[0].GlyphId, ranged[0].GlyphId);
        Assert.NotEqual(disabled[1].GlyphId, ranged[1].GlyphId);
    }

    [Fact]
    public void CpuExecutorHonorsDefaultIgnorableBufferPolicy()
    {
        var face = new TtfShapingFontFace(InterFontFamily.Regular);
        using var normal = new ShapingBuffer();
        using var preserved = new ShapingBuffer();
        using var removed = new ShapingBuffer();
        CpuOpenTypeShaper.Instance.Shape("\u200d", face,
            new ShapingRequest(ShapingDirection.LeftToRight, new OpenTypeTag("latn")), normal);
        CpuOpenTypeShaper.Instance.Shape("\u200d", face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                flags: ShapingBufferFlags.PreserveDefaultIgnorables), preserved);
        CpuOpenTypeShaper.Instance.Shape("\u200d", face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                flags: ShapingBufferFlags.RemoveDefaultIgnorables), removed);

        Assert.Equal(1, normal.Count);
        Assert.Equal(1, preserved.Count);
        Assert.Equal(0, removed.Count);
        Assert.NotEqual(normal[0].GlyphId, preserved[0].GlyphId);
    }

    [Fact]
    public void CpuExecutorHonorsCharacterClusterLevels()
    {
        var face = new TtfShapingFontFace(InterFontFamily.Regular);
        using var graphemes = new ShapingBuffer();
        using var characters = new ShapingBuffer();
        const string text = "x\u030a";
        CpuOpenTypeShaper.Instance.Shape(text, face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                clusterLevel: ShapingClusterLevel.Graphemes), graphemes);
        CpuOpenTypeShaper.Instance.Shape(text, face,
            new ShapingRequest(
                ShapingDirection.LeftToRight,
                new OpenTypeTag("latn"),
                clusterLevel: ShapingClusterLevel.Characters), characters);

        Assert.Equal(2, graphemes.Count);
        Assert.Equal(2, characters.Count);
        Assert.Equal(0, graphemes[0].Cluster);
        Assert.Equal(0, graphemes[1].Cluster);
        Assert.Equal(0, characters[0].Cluster);
        Assert.Equal(1, characters[1].Cluster);
    }

    [Fact]
    public void GpuPlanPreservesNominalMappingAndDesignMetrics()
    {
        var face = new TtfShapingFontFace(InterFontFamily.Regular);
        GpuOpenTypeShapingPlan plan = GpuOpenTypeShapingPlanCompiler.Compile(face);

        Assert.Equal(16, Marshal.SizeOf<GpuCmapRange>());
        Assert.Equal(16, Marshal.SizeOf<GpuGlyphMetrics>());
        Assert.Equal(16, Marshal.SizeOf<GpuShapingScalar>());
        foreach (uint codePoint in new uint[] { 'A', 'z', 0x00e9, 0x03a9, 0x20ac, 0x1f642 })
            Assert.Equal(face.GetNominalGlyph(codePoint), plan.GetNominalGlyph(codePoint));
        uint glyph = face.GetNominalGlyph('A');
        GpuGlyphMetrics metric = plan.Metrics.Span[checked((int)glyph)];
        Assert.Equal(face.GetHorizontalAdvance(glyph), metric.AdvanceX);
        Assert.Equal(face.GetVerticalAdvance(glyph), metric.AdvanceY);
        Assert.Equal(face.GetVerticalOrigin(glyph), metric.OriginY);
    }

    private static ShapingGlyph Glyph(uint glyphId) => new() { GlyphId = glyphId };
}
