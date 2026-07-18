using ProGPU.Compute;
using Xunit;

namespace ProGPU.Tests;

public class WavefrontBinningTests
{
    [Fact]
    public void StableCellBinsPreservePainterOrderWithoutPerCellCap()
    {
        const int instanceCount = 257;
        var ranges = new (uint MinX, uint MinY, uint MaxX, uint MaxY)[instanceCount];
        Array.Fill(ranges, (0u, 0u, 0u, 0u));
        var cells = new GpuGridCell[1];
        var indices = new uint[instanceCount];

        WavefrontVectorEngine.BuildStableCellBinsCpu(ranges, 1, 1, cells, indices);

        Assert.Equal(0u, cells[0].ShapeStartOffset);
        Assert.Equal((uint)instanceCount, cells[0].ShapeCount);
        for (uint index = 0; index < instanceCount; index++)
        {
            Assert.Equal(index, indices[(int)index]);
        }
    }

    [Fact]
    public void StableCellBinsCreateExactOffsetsForSparseOverlaps()
    {
        var ranges = new (uint MinX, uint MinY, uint MaxX, uint MaxY)[]
        {
            (0, 0, 1, 0),
            (1, 0, 1, 1),
            (0, 1, 1, 1)
        };
        var cells = new GpuGridCell[4];
        var indices = new uint[6];

        WavefrontVectorEngine.BuildStableCellBinsCpu(ranges, 2, 2, cells, indices);

        Assert.Equal(new uint[] { 0 }, Slice(cells[0], indices));
        Assert.Equal(new uint[] { 0, 1 }, Slice(cells[1], indices));
        Assert.Equal(new uint[] { 2 }, Slice(cells[2], indices));
        Assert.Equal(new uint[] { 1, 2 }, Slice(cells[3], indices));
    }

    [Fact]
    public void ActiveCellCompactionPreservesRowMajorOrderAndBuildsIndirectDispatch()
    {
        var cells = new[]
        {
            new GpuGridCell(),
            new GpuGridCell { ShapeStartOffset = 0, ShapeCount = 3 },
            new GpuGridCell(),
            new GpuGridCell { ShapeStartOffset = 3, ShapeCount = 1 },
            new GpuGridCell(),
            new GpuGridCell { ShapeStartOffset = 4, ShapeCount = 2 }
        };
        var activeCells = new uint[3];

        var dispatch = WavefrontVectorEngine.BuildActiveCellListCpu(cells, activeCells);

        Assert.Equal(new uint[] { 1, 3, 5 }, activeCells);
        Assert.Equal(3u, dispatch.X);
        Assert.Equal(1u, dispatch.Y);
        Assert.Equal(1u, dispatch.Z);
    }

    [Fact]
    public void EmptyCellGridBuildsZeroWorkIndirectDispatch()
    {
        var dispatch = WavefrontVectorEngine.BuildActiveCellListCpu(
            new GpuGridCell[4],
            Span<uint>.Empty);

        Assert.Equal(0u, dispatch.X);
        Assert.Equal(1u, dispatch.Y);
        Assert.Equal(1u, dispatch.Z);
    }

    [Fact]
    public void SparseDispatchSplitsWorkAbovePortableDimensionLimit()
    {
        var dispatch = WavefrontVectorEngine.CreateSparseDispatchArgs(
            WavefrontVectorEngine.MaximumPortableDispatchDimension + 1u);

        Assert.Equal(WavefrontVectorEngine.MaximumPortableDispatchDimension, dispatch.X);
        Assert.Equal(2u, dispatch.Y);
        Assert.Equal(1u, dispatch.Z);
    }

    [Fact]
    public void CoarseCellClassificationKeepsUncertainCoverageOnEdgePath()
    {
        Assert.Equal(
            GpuCellShapeClass.Edge,
            WavefrontVectorEngine.ClassifyCellByCenterDistance(
                11.8f, 1f, 1f, 16f, 16f, centerIsInside: true));
        Assert.Equal(
            GpuCellShapeClass.Solid,
            WavefrontVectorEngine.ClassifyCellByCenterDistance(
                12f, 1f, 1f, 16f, 16f, centerIsInside: true));
        Assert.Equal(
            GpuCellShapeClass.Outside,
            WavefrontVectorEngine.ClassifyCellByCenterDistance(
                24f, 0.5f, 1f, 16f, 16f, centerIsInside: false));
        Assert.Equal(
            GpuCellShapeClass.Edge,
            WavefrontVectorEngine.ClassifyCellByCenterDistance(
                float.NaN, 1f, 1f, 16f, 16f, centerIsInside: true));
    }

    private static uint[] Slice(GpuGridCell cell, uint[] indices) =>
        indices.AsSpan((int)cell.ShapeStartOffset, (int)cell.ShapeCount).ToArray();
}
