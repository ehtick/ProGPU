using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkDataCompatibilityTests
{
    [Fact]
    public void OwnedDataExposesStableWritableMemoryAndCopiesAtBoundaries()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        using var data = SKData.CreateCopy(source);
        var address = data.Data;

        source[0] = 9;
        data.Span[1] = 8;
        GC.Collect();

        Assert.Equal(address, data.Data);
        Assert.Equal(4, data.Size);
        Assert.False(data.IsEmpty);
        Assert.Equal(new byte[] { 1, 8, 3, 4 }, data.AsSpan().ToArray());
        Assert.Equal(new byte[] { 1, 8, 3, 4 }, data.ToArray());
        Assert.Equal(new byte[] { 9, 2, 3, 4 }, source);

        using var output = new MemoryStream();
        data.SaveTo(output);
        Assert.Equal(new byte[] { 1, 8, 3, 4 }, output.ToArray());

        Assert.Empty(typeof(SKData).GetConstructors());
        Assert.Same(SKData.Empty, SKData.Empty);
        SKData.Empty.Dispose();
        Assert.True(SKData.Empty.IsEmpty);
        Assert.Equal(IntPtr.Zero, SKData.Empty.Data);
    }

    [Fact]
    public void SubsetsShareStorageAndOutliveTheirParent()
    {
        var data = SKData.CreateCopy(new byte[] { 1, 2, 3, 4, 5 });
        using var subset = data.Subset(1, 3);

        data.Span[2] = 9;
        Assert.Equal(new byte[] { 2, 9, 4 }, subset.ToArray());
        Assert.Equal(1, subset.Data.ToInt64() - data.Data.ToInt64());

        data.Dispose();
        Assert.Equal(new byte[] { 2, 9, 4 }, subset.ToArray());

        using var source = SKData.CreateCopy(new byte[] { 1, 2, 3, 4, 5 });
        using var tooLong = source.Subset(3, 9);
        using var beyond = source.Subset(9, 2);
        Assert.True(tooLong.IsEmpty);
        Assert.True(beyond.IsEmpty);
    }

    [Fact]
    public void StreamAndFileFactoriesPreserveNativeLengthContracts()
    {
        using var remainingSource = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        remainingSource.Position = 2;
        using var remaining = SKData.Create(remainingSource);
        Assert.Equal(new byte[] { 3, 4, 5 }, remaining.ToArray());
        Assert.Equal(5, remainingSource.Position);

        using var prefixSource = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        prefixSource.Position = 1;
        using var prefix = SKData.Create(prefixSource, 2);
        Assert.Equal(new byte[] { 2, 3 }, prefix.ToArray());
        Assert.Equal(3, prefixSource.Position);

        using var shortSource = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Null(SKData.Create(shortSource, 5));
        Assert.Equal(3, shortSource.Position);

        var missing = Path.Combine(Path.GetTempPath(), $"progpu-data-{Guid.NewGuid():N}.missing");
        Assert.Throws<ArgumentException>(() => SKData.Create(string.Empty));
        Assert.Null(SKData.Create(missing));
    }

    [Fact]
    public void ExternalStorageReleasesAfterTheLastSharedView()
    {
        var address = Marshal.AllocHGlobal(3);
        Marshal.Copy(new byte[] { 1, 2, 3 }, 0, address, 3);
        var releases = 0;
        object? releasedContext = null;
        IntPtr releasedAddress = IntPtr.Zero;

        var data = SKData.Create(address, 3, (value, context) =>
        {
            releases++;
            releasedAddress = value;
            releasedContext = context;
            Marshal.FreeHGlobal(value);
        }, "context");
        var subset = data.Subset(1, 2);
        data.Span[1] = 9;

        Assert.Equal(new byte[] { 9, 3 }, subset.ToArray());
        data.Dispose();
        Assert.Equal(0, releases);
        subset.Dispose();
        subset.Dispose();

        Assert.Equal(1, releases);
        Assert.Equal(address, releasedAddress);
        Assert.Equal("context", releasedContext);
    }
}
