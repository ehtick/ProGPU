using System;
using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkImageBitmapTests
{
    [Fact]
    public void InstallPixelsPreservesRowBytesAndCopyUsesStride()
    {
        var info = new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pixels = Marshal.AllocHGlobal(24);
        try
        {
            WriteBytes(pixels, new byte[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 99, 99, 99, 99,
                9, 10, 11, 12, 13, 14, 15, 16, 88, 88, 88, 88
            });

            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, pixels, rowBytes: 12);

            Assert.Equal(12, bitmap.RowBytes);
            Assert.Equal(24, bitmap.BytesSize);
            Assert.Equal(12, bitmap.PeekPixels().RowBytes);

            using var copy = bitmap.Copy();
            Assert.Equal(8, copy.RowBytes);
            Assert.Equal(16, copy.BytesSize);
            Assert.Equal(new byte[]
            {
                1, 2, 3, 4, 5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16
            }, ReadBytes(copy.GetPixels(), 16));
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
        }
    }

    [Fact]
    public void FromBitmapConvertsBgraRowsBeforeUpload()
    {
        var info = new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = Marshal.AllocHGlobal(24);
        var dst = Marshal.AllocHGlobal(16);
        try
        {
            WriteBytes(pixels, new byte[]
            {
                0, 0, 255, 255, 0, 255, 0, 255, 99, 99, 99, 99,
                255, 0, 0, 255, 255, 255, 255, 255, 88, 88, 88, 88
            });

            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, pixels, rowBytes: 12);
            using var image = SKImage.FromBitmap(bitmap);

            image.ReadPixels(
                new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul),
                dst,
                dstRowBytes: 8,
                srcX: 0,
                srcY: 0,
                SKImageCachingHint.Allow);

            Assert.Equal(new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 255
            }, ReadBytes(dst, 16));
        }
        finally
        {
            Marshal.FreeHGlobal(dst);
            Marshal.FreeHGlobal(pixels);
        }
    }

    [Fact]
    public void FromBitmapMarksUnpremultipliedUploadsAsStraightAlpha()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        WriteBytes(bitmap.GetPixels(), new byte[] { 255, 0, 0, 128 });

        using var image = SKImage.FromBitmap(bitmap);

        Assert.Equal(GpuTextureAlphaMode.Straight, image.Texture.AlphaMode);
    }

    [Fact]
    public void FromBitmapForcesOpaqueUploadsToAlpha255()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Opaque));
        WriteBytes(bitmap.GetPixels(), new byte[] { 10, 20, 30, 0 });

        using var image = SKImage.FromBitmap(bitmap);

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, image.Texture.ReadPixels());
    }

    [Fact]
    public void EncodeUnpremultipliesPremultipliedPixels()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        WriteBytes(bitmap.GetPixels(), new byte[] { 128, 0, 0, 128 });
        using var image = SKImage.FromBitmap(bitmap);

        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = SKBitmap.Decode(data);

        Assert.Equal(new byte[] { 255, 0, 0, 128 }, ReadBytes(decoded.GetPixels(), 4));
    }

    [Fact]
    public void ScalePixelsWritesScaledDestinationPixmap()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        WriteBytes(bitmap.GetPixels(), new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255
        });
        using var image = SKImage.FromBitmap(bitmap);
        using var destination = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        WriteBytes(destination.GetPixels(), new byte[] { 9, 9, 9, 9 });

        image.ScalePixels(
            destination.PeekPixels(),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, ReadBytes(destination.GetPixels(), 4));
    }

    [Fact]
    public void DecodeCodecCopiesEncodedPixelsIntoBitmap()
    {
        using var codec = SKCodec.Create(new SKData(TwoPixelPngBytes()));

        using var bitmap = SKBitmap.Decode(
            codec,
            new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul));

        Assert.Equal(2, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
        Assert.Equal(8, bitmap.RowBytes);
        Assert.Equal(new byte[]
        {
            255, 0, 0, 255,
            0, 255, 0, 255
        }, ReadBytes(bitmap.GetPixels(), 8));
    }

    [Fact]
    public void DecodeCodecConvertsEncodedPixelsToRequestedBgraBitmap()
    {
        using var codec = SKCodec.Create(new SKData(TwoPixelPngBytes()));

        using var bitmap = SKBitmap.Decode(
            codec,
            new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));

        Assert.Equal(SKColorType.Bgra8888, bitmap.ColorType);
        Assert.Equal(new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255
        }, ReadBytes(bitmap.GetPixels(), 8));
    }

    [Fact]
    public void ReadPixelsClipsNegativeSourceOrigin()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        WriteBytes(bitmap.GetPixels(), new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255
        });
        using var image = SKImage.FromBitmap(bitmap);
        var dst = Marshal.AllocHGlobal(36);
        try
        {
            WriteBytes(dst, new byte[36]);

            image.ReadPixels(
                new SKImageInfo(3, 3, SKColorType.Rgba8888, SKAlphaType.Premul),
                dst,
                dstRowBytes: 12,
                srcX: -1,
                srcY: -1,
                SKImageCachingHint.Allow);

            Assert.Equal(new byte[]
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255
            }, ReadBytes(dst, 36));
        }
        finally
        {
            Marshal.FreeHGlobal(dst);
        }
    }

    [Fact]
    public void ReadPixelsUnpremultipliesWhenDestinationRequestsUnpremul()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        WriteBytes(bitmap.GetPixels(), new byte[] { 128, 0, 0, 128 });
        using var image = SKImage.FromBitmap(bitmap);
        var dst = Marshal.AllocHGlobal(4);
        try
        {
            image.ReadPixels(
                new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                dst,
                dstRowBytes: 4,
                srcX: 0,
                srcY: 0,
                SKImageCachingHint.Allow);

            Assert.Equal(new byte[] { 255, 0, 0, 128 }, ReadBytes(dst, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(dst);
        }
    }

    [Fact]
    public unsafe void DisposeDisposesOwnedImagesButLeavesBorrowedTexturesAlive()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        WriteBytes(bitmap.GetPixels(), new byte[] { 1, 2, 3, 4 });

        var ownedImage = SKImage.FromBitmap(bitmap);
        var ownedTexture = ownedImage.Texture;
        ownedImage.Dispose();
        Assert.True(ownedTexture.TexturePtr == null);

        using var context = new WgpuContext();
        context.Initialize(null);
        using var borrowedTexture = new GpuTexture(
            context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
            "Borrowed SKImage Test Texture");

        var borrowedImage = SKImage.FromTexture(borrowedTexture);
        borrowedImage.Dispose();
        Assert.True(borrowedTexture.TexturePtr != null);
    }

    private static void WriteBytes(IntPtr destination, byte[] bytes)
    {
        Marshal.Copy(bytes, 0, destination, bytes.Length);
    }

    private static byte[] ReadBytes(IntPtr source, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(source, bytes, 0, length);
        return bytes;
    }

    private static byte[] TwoPixelPngBytes()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwHwQBEPgD/U6VwW8AAAAASUVORK5CYII=");
    }
}
