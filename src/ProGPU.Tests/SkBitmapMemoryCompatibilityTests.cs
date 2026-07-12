using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkBitmapMemoryCompatibilityTests
{
    private static readonly SKImageInfo RgbaInfo = new(
        2,
        2,
        SKColorType.Rgba8888,
        SKAlphaType.Premul);

    [Fact]
    public void AllocationStateAndStrideAwareSpansMatchNative()
    {
        using var empty = new SKBitmap();
        Assert.False(empty.ReadyToDraw);
        Assert.True(empty.IsEmpty);
        Assert.True(empty.IsNull);
        Assert.True(empty.DrawsNothing);
        Assert.Equal(0, empty.ByteCount);

        using var bitmap = new SKBitmap(RgbaInfo, rowBytes: 12);
        Assert.True(bitmap.ReadyToDraw);
        Assert.False(bitmap.IsEmpty);
        Assert.False(bitmap.IsNull);
        Assert.False(bitmap.DrawsNothing);
        Assert.Equal(4, bitmap.BytesPerPixel);
        Assert.Equal(12, bitmap.RowBytes);
        Assert.Equal(20, bitmap.ByteCount);
        Assert.Equal(20, bitmap.GetPixelSpan().Length);
        Assert.Equal(4, bitmap.GetPixelSpan(1, 1).Length);
        Assert.Equal(bitmap.GetPixels(), bitmap.GetPixels(out var length));
        Assert.Equal((IntPtr)20, length);
    }

    [Fact]
    public void AllocationFlagsUnknownMetadataAndResetMatchNative()
    {
        using var bitmap = new SKBitmap(RgbaInfo, SKBitmapAllocFlags.ZeroPixels);
        Assert.Equal(new byte[16], bitmap.Bytes);

        bitmap.SetImmutable();
        Assert.True(bitmap.IsImmutable);

        Assert.True(bitmap.TryAllocPixels(new SKImageInfo(
            2,
            2,
            SKColorType.Unknown,
            SKAlphaType.Unknown)));
        Assert.False(bitmap.ReadyToDraw);
        Assert.False(bitmap.IsEmpty);
        Assert.True(bitmap.IsNull);
        Assert.Equal(0, bitmap.ByteCount);

        Assert.True(bitmap.TryAllocPixels(RgbaInfo, rowBytes: 12));
        Assert.True(bitmap.ReadyToDraw);
        Assert.False(bitmap.IsImmutable);
        bitmap.Reset();
        Assert.True(bitmap.IsEmpty);
        Assert.True(bitmap.IsNull);
        Assert.Equal(0, bitmap.RowBytes);
    }

    [Fact]
    public void InstalledPixelsValidateStrideAndReleaseExactlyOnce()
    {
        using var bitmap = new SKBitmap();
        var invalidAddress = Marshal.AllocHGlobal(16);
        try
        {
            Assert.False(bitmap.InstallPixels(RgbaInfo, invalidAddress, rowBytes: 4));
            Assert.True(bitmap.IsNull);
            Assert.True(bitmap.InstallPixels(RgbaInfo, IntPtr.Zero, rowBytes: 8));
            Assert.True(bitmap.IsNull);
            Assert.Equal(16, bitmap.ByteCount);
        }
        finally
        {
            Marshal.FreeHGlobal(invalidAddress);
        }

        var address = Marshal.AllocHGlobal(16);
        var releases = 0;
        Assert.True(bitmap.InstallPixels(RgbaInfo, address, rowBytes: 8, (value, _) =>
        {
            releases++;
            Marshal.FreeHGlobal(value);
        }));
        Assert.Equal(0, releases);

        bitmap.Reset();
        bitmap.Reset();
        Assert.Equal(1, releases);
    }

    [Fact]
    public void PixmapViewsExposeLogicalSizeAndStrideAwareStorage()
    {
        var address = Marshal.AllocHGlobal(20);
        try
        {
            using var pixmap = new SKPixmap(RgbaInfo, address, rowBytes: 12);
            Assert.Equal(new SKSizeI(2, 2), pixmap.Size);
            Assert.Equal(SKRectI.Create(2, 2), pixmap.Rect);
            Assert.Equal(16, pixmap.BytesSize);
            Assert.Equal(20, pixmap.GetPixelSpan().Length);
            Assert.Equal(4, pixmap.GetPixelSpan(1, 1).Length);
            Assert.Equal(5, pixmap.GetPixelSpan<uint>().Length);
            Assert.Equal(1, pixmap.GetPixelSpan<uint>(1, 1).Length);
            Assert.Throws<ArgumentException>(() => pixmap.GetPixelSpan<ushort>());

            using var bitmap = new SKBitmap();
            Assert.True(bitmap.InstallPixels(pixmap));
            using var peek = new SKPixmap();
            Assert.True(bitmap.PeekPixels(peek));
            Assert.Equal(address, peek.GetPixels());
            Assert.Equal(12, peek.RowBytes);

            pixmap.Reset();
            Assert.Equal(IntPtr.Zero, pixmap.GetPixels());
            Assert.Equal(0, pixmap.Width);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }
}
