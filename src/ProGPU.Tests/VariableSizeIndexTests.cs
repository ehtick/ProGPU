using ProGPU.Virtualization;
using Xunit;

namespace ProGPU.Tests;

public sealed class VariableSizeIndexTests
{
    [Fact]
    public void PrefixLookupAndMeasurementUpdatesRemainLogarithmicIndexSemantics()
    {
        var index = new VariableSizeIndex(5, 10f);

        index.SetMeasuredSize(1, 25f);
        index.SetMeasuredSize(3, 5f);

        Assert.Equal(0f, index.GetOffset(0));
        Assert.Equal(10f, index.GetOffset(1));
        Assert.Equal(35f, index.GetOffset(2));
        Assert.Equal(45f, index.GetOffset(3));
        Assert.Equal(50f, index.GetOffset(4));
        Assert.Equal(60f, index.TotalSize);
        Assert.Equal(0, index.GetIndexAtOffset(0f));
        Assert.Equal(1, index.GetIndexAtOffset(10f));
        Assert.Equal(1, index.GetIndexAtOffset(34.99f));
        Assert.Equal(2, index.GetIndexAtOffset(35f));
        Assert.Equal(4, index.GetIndexAtOffset(100f));
    }

    [Fact]
    public void InsertRemoveAndAnchorPreserveMeasuredGeometry()
    {
        var index = new VariableSizeIndex(4, 20f);
        index.SetMeasuredSize(1, 40f);
        VirtualizationAnchor anchor = index.CaptureAnchor(65f);

        index.InsertRange(1, 2);
        Assert.Equal(6, index.Count);
        Assert.Equal(40f, index.GetSize(3));

        index.RemoveRange(1, 2);
        Assert.Equal(4, index.Count);
        Assert.Equal(65f, index.RestoreAnchor(anchor));
    }
}
