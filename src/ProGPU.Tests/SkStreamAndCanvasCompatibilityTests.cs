using System.Runtime.InteropServices;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkStreamAndCanvasCompatibilityTests
{
    [Fact]
    public void CanvasSaveCountsUseSkiaOneBasedSemantics()
    {
        using var canvas = new SKCanvas(new DrawingContext(), 16f, 16f);

        Assert.Equal(1, canvas.SaveCount);
        var saveCount = canvas.Save();
        Assert.Equal(1, saveCount);
        Assert.Equal(2, canvas.SaveCount);
        var nestedSaveCount = canvas.Save();
        Assert.Equal(2, nestedSaveCount);
        Assert.Equal(3, canvas.SaveCount);

        canvas.RestoreToCount(nestedSaveCount);
        Assert.Equal(2, canvas.SaveCount);
        canvas.RestoreToCount(saveCount);
        Assert.Equal(1, canvas.SaveCount);
        canvas.RestoreToCount(0);
        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void AutoCanvasRestoreRestoresNestedSavesExactlyOnce()
    {
        using var canvas = new SKCanvas(new DrawingContext(), 16f, 16f);
        using (var restore = new SKAutoCanvasRestore(canvas, doSave: true))
        {
            Assert.Equal(2, canvas.SaveCount);
            canvas.Save();
            Assert.Equal(3, canvas.SaveCount);
            restore.Restore();
            Assert.Equal(1, canvas.SaveCount);
            restore.Restore();
            Assert.Equal(1, canvas.SaveCount);
        }

        using (var restore = new SKAutoCanvasRestore(canvas, doSave: false))
        {
            Assert.Equal(1, canvas.SaveCount);
            canvas.Save();
            Assert.Equal(2, canvas.SaveCount);
        }

        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void FileStreamFeedsCodecAndPreservesInvalidPathContract()
    {
        var path = Path.Combine(Path.GetTempPath(), $"progpu-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+AvzL8QAAAABJRU5ErkJggg=="));

            using var stream = new SKFileStream(path);
            Assert.True(stream.IsValid);
            Assert.Equal(new FileInfo(path).Length, stream.Length);
            Assert.Equal(0, stream.Position);
            Assert.Equal(0x89, stream.ReadByte());
            Assert.Equal(0x4e50, stream.ReadUInt16());
            Assert.Equal(3, stream.Position);
            var peek = Marshal.AllocHGlobal(2);
            try
            {
                Assert.Equal(2, stream.Peek(peek, 2));
                Assert.Equal(0x47, Marshal.ReadByte(peek));
                Assert.Equal(0x0d, Marshal.ReadByte(peek, 1));
                Assert.Equal(3, stream.Position);
            }
            finally
            {
                Marshal.FreeHGlobal(peek);
            }
            using (var data = stream.GetData())
            {
                Assert.Equal(File.ReadAllBytes(path), data.ToArray());
            }
            Assert.Equal(3, stream.Position);
            Assert.True(stream.Rewind());
            Assert.Equal(0, stream.Position);
            using var codec = SKCodec.Create(stream);
            Assert.Equal(1, codec.Info.Width);
            Assert.Equal(1, codec.Info.Height);

            using var missing = new SKFileStream(path + ".missing");
            Assert.False(missing.IsValid);
            Assert.Equal(0, missing.Length);
            Assert.Null(SKFileStream.OpenStream(path + ".missing"));
            using var empty = new SKFileStream(string.Empty);
            using var nullPath = new SKFileStream(null!);
            Assert.False(empty.IsValid);
            Assert.False(nullPath.IsValid);
            Assert.Null(SKFileStream.OpenStream(null!));
            Assert.True(SKFileStream.IsPathSupported(string.Empty));
            Assert.True(SKFileStream.IsPathSupported(null!));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
