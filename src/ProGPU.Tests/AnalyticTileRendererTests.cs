using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Compute;
using Xunit;

namespace ProGPU.Tests;

public sealed class AnalyticTileRendererTests
{
    [Fact]
    public void HostLayoutsMatchTheShaderAbi()
    {
        Assert.Equal(16, Marshal.SizeOf<GpuAnalyticTile>());
        Assert.Equal(32, Marshal.SizeOf<GpuAnalyticFill>());
        Assert.Equal(24, Marshal.SizeOf<GpuAnalyticSegment>());
    }

    [Fact]
    public void SegmentCountAndRuleRoundTrips()
    {
        var nonZero = new GpuAnalyticFill
        {
            SegmentCountAndRule = GpuAnalyticFill.PackSegmentCountAndRule(123, evenOdd: false)
        };
        var evenOdd = new GpuAnalyticFill
        {
            SegmentCountAndRule = GpuAnalyticFill.PackSegmentCountAndRule(456, evenOdd: true)
        };

        Assert.Equal(123u, nonZero.SegmentCount);
        Assert.False(nonZero.IsEvenOdd);
        Assert.Equal(456u, evenOdd.SegmentCount);
        Assert.True(evenOdd.IsEvenOdd);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GpuAnalyticFill.PackSegmentCountAndRule(uint.MaxValue, evenOdd: false));
    }

    [Fact]
    public void CpuOracleRasterizesTileLocalRectangleWithAnalyticCoverage()
    {
        GpuAnalyticSegment[] segments = RectangleSegments();
        GpuAnalyticFill[] fills =
        [
            Fill(0, (uint)segments.Length, evenOdd: false, new Vector4(1f, 0f, 0f, 1f))
        ];
        var output = new Vector4[GpuAnalyticTileRenderer.TileSize * GpuAnalyticTileRenderer.TileSize];

        GpuAnalyticTileRenderer.RasterizeTileCpu(fills, segments, output);

        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), Pixel(output, 8, 8));
        Assert.Equal(Vector4.Zero, Pixel(output, 0, 0));
        Assert.Equal(Vector4.Zero, Pixel(output, 15, 15));
    }

    [Fact]
    public void CpuOraclePreservesFillRuleAndPainterOrder()
    {
        GpuAnalyticSegment[] rectangle = RectangleSegments();
        var doubled = new GpuAnalyticSegment[rectangle.Length * 2];
        rectangle.CopyTo(doubled, 0);
        rectangle.CopyTo(doubled, rectangle.Length);
        var evenOddOutput = new Vector4[256];
        GpuAnalyticTileRenderer.RasterizeTileCpu(
            [Fill(0, (uint)doubled.Length, evenOdd: true, Vector4.One)],
            doubled,
            evenOddOutput);
        Assert.Equal(Vector4.Zero, Pixel(evenOddOutput, 8, 8));

        var painterOutput = new Vector4[256];
        GpuAnalyticTileRenderer.RasterizeTileCpu(
            [
                Fill(0, (uint)rectangle.Length, evenOdd: false, new Vector4(1f, 0f, 0f, 1f)),
                Fill(0, (uint)rectangle.Length, evenOdd: false, new Vector4(0f, 0.5f, 0f, 0.5f))
            ],
            rectangle,
            painterOutput);
        Assert.Equal(new Vector4(0.5f, 0.5f, 0f, 1f), Pixel(painterOutput, 8, 8));
    }

    [Fact]
    public void CpuOracleProducesFractionalAnalyticCoverageForSlopedEdges()
    {
        GpuAnalyticSegment[] triangle = TriangleSegments();
        var output = new Vector4[256];

        GpuAnalyticTileRenderer.RasterizeTileCpu(
            [Fill(0, (uint)triangle.Length, evenOdd: false, Vector4.One)],
            triangle,
            output);

        Assert.Contains(output, static pixel => pixel.W > 0f && pixel.W < 1f);
        Assert.Contains(output, static pixel => pixel.W == 1f);
        Assert.Contains(output, static pixel => pixel.W == 0f);
    }

    [Fact]
    public void UploadAbiRejectsOutOfRangeReferencesBeforeGpuEncoding()
    {
        var output = new Vector4[256];
        var invalid = Fill(1, 1, evenOdd: false, Vector4.One);

        Assert.Throws<ArgumentException>(() =>
            GpuAnalyticTileRenderer.RasterizeTileCpu([invalid], [], output));
    }

    [Fact]
    public void FineShaderUsesAnalyticAreaWithoutWavefrontDistanceOrBarriers()
    {
        string source = ComputeShaders.AnalyticTileFine;

        Assert.Contains("fn segment_area(", source, StringComparison.Ordinal);
        Assert.Contains("area0 += yEdge + segment_area", source, StringComparison.Ordinal);
        Assert.Contains("source_over(command.premultipliedColor", source, StringComparison.Ordinal);
        Assert.Contains("@workgroup_size(4, 16, 1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("workgroupBarrier", source, StringComparison.Ordinal);
        Assert.DoesNotContain("signed_distance", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evaluate_shape", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bvh_nodes", source, StringComparison.Ordinal);
    }

    internal static GpuAnalyticSegment[] RectangleSegments() =>
    [
        new GpuAnalyticSegment
        {
            Point0 = new Vector2(14f - 1e-6f, 2f),
            Point1 = new Vector2(14f - 1e-6f, 14f),
            YEdge = 1e9f
        },
        new GpuAnalyticSegment
        {
            Point0 = new Vector2(2f - 1e-6f, 14f),
            Point1 = new Vector2(2f - 1e-6f, 2f),
            YEdge = 1e9f
        }
    ];

    internal static GpuAnalyticFill Fill(
        uint segmentOffset,
        uint segmentCount,
        bool evenOdd,
        Vector4 premultipliedColor) => new()
    {
        SegmentOffset = segmentOffset,
        SegmentCountAndRule = GpuAnalyticFill.PackSegmentCountAndRule(segmentCount, evenOdd),
        PremultipliedColor = premultipliedColor
    };

    internal static GpuAnalyticSegment[] TriangleSegments() =>
    [
        new GpuAnalyticSegment
        {
            Point0 = new Vector2(2.25f, 2.5f),
            Point1 = new Vector2(13.5f, 4.25f),
            YEdge = 1e9f
        },
        new GpuAnalyticSegment
        {
            Point0 = new Vector2(13.5f, 4.25f),
            Point1 = new Vector2(6.75f, 14.2f),
            YEdge = 1e9f
        },
        new GpuAnalyticSegment
        {
            Point0 = new Vector2(6.75f, 14.2f),
            Point1 = new Vector2(2.25f, 2.5f),
            YEdge = 1e9f
        }
    ];

    private static Vector4 Pixel(Vector4[] pixels, int x, int y) => pixels[y * 16 + x];
}
