using System;
using System.Buffers.Binary;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkiaSharpIconDecoderTests
{
    [Fact]
    public void Decode24BitIconAppliesAndMaskTransparency()
    {
        var icon = CreateIcon(
            bitCount: 24,
            xorPixels: [0, 0, 255, 0, 255, 0, 0, 0],
            andMask: [0x40, 0, 0, 0]);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(new SKColor(255, 0, 0, 255), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 0), bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeIndexedIconUsesColorTableAndAndMask()
    {
        var palette = new byte[]
        {
            0, 0, 0, 0,
            0, 0, 255, 0,
            0, 255, 0, 0
        };
        var icon = CreateIcon(
            bitCount: 4,
            xorPixels: [0x12, 0, 0, 0],
            andMask: [0x40, 0, 0, 0],
            palette: palette,
            colorCount: 3);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(new SKColor(255, 0, 0, 255), bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 0), bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeOneBitIconReadsMostSignificantBitFirst()
    {
        var icon = CreateIcon(
            bitCount: 1,
            xorPixels: [0x40, 0, 0, 0],
            andMask: [0, 0, 0, 0],
            palette: [0, 0, 0, 0, 255, 255, 255, 0],
            colorCount: 2);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeEightBitIconReadsPaletteIndices()
    {
        var icon = CreateIcon(
            bitCount: 8,
            xorPixels: [1, 2, 0, 0],
            andMask: [0, 0, 0, 0],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeRle4IconUsesEndMarkerToLocateAndMask()
    {
        var icon = CreateIcon(
            bitCount: 4,
            xorPixels: [2, 0x12, 0, 0, 0, 1],
            andMask: [0x40, 0, 0, 0],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3,
            compression: 2,
            imageByteCount: 0);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 0), bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeRle8IconSupportsEncodedRuns()
    {
        var icon = CreateIcon(
            bitCount: 8,
            xorPixels: [1, 1, 1, 2, 0, 0, 0, 1],
            andMask: [0, 0, 0, 0],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3,
            compression: 1);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeRle4IconSupportsAbsoluteRuns()
    {
        var icon = CreateIcon(
            bitCount: 4,
            xorPixels: [0, 5, 0x12, 0x12, 0x10, 0, 0, 0, 0, 1],
            andMask: [0, 0, 0, 0],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3,
            compression: 2,
            width: 5);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(2, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(3, 0));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(4, 0));
    }

    [Fact]
    public void DecodeRle8IconSupportsDeltaAndBottomUpRows()
    {
        var icon = CreateIcon(
            bitCount: 8,
            xorPixels: [0, 2, 1, 0, 2, 1, 0, 0, 4, 2, 0, 0, 0, 1],
            andMask: [0, 0, 0, 0, 0, 0, 0, 0],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3,
            compression: 1,
            width: 4,
            height: 2);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Lime, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(3, 0));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(0, 1));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(1, 1));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(2, 1));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(3, 1));
    }

    [Fact]
    public void DecodeRleIconRejectsMissingEndMarker()
    {
        var icon = CreateIcon(
            bitCount: 4,
            xorPixels: [2, 0x12],
            andMask: [],
            palette:
            [
                0, 0, 0, 0,
                0, 0, 255, 0,
                0, 255, 0, 0
            ],
            colorCount: 3,
            compression: 2);

        Assert.Throws<InvalidOperationException>(() => SKBitmap.Decode(new SKData(icon)));
    }

    [Fact]
    public void Decode16BitIconSupportsRgb555Pixels()
    {
        var icon = CreateIcon(
            bitCount: 16,
            xorPixels: [0x00, 0x7c, 0xe0, 0x03],
            andMask: [0, 0, 0, 0]);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void Decode16BitIconSupportsRgb565BitFields()
    {
        var masks = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0xf800);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0x07e0);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x001f);
        var icon = CreateIcon(
            bitCount: 16,
            xorPixels: [0x00, 0xf8, 0xe0, 0x07],
            andMask: [0, 0, 0, 0],
            compression: 3,
            bitFieldMasks: masks);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void Decode32BitIconUsesAndMaskWhenStoredAlphaIsEmpty()
    {
        var icon = CreateIcon(
            bitCount: 32,
            xorPixels: [0, 0, 255, 0, 0, 255, 0, 0],
            andMask: [0x40, 0, 0, 0]);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 0), bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void Decode32BitIconPreservesMeaningfulStoredAlpha()
    {
        var icon = CreateIcon(
            bitCount: 32,
            xorPixels: [0, 0, 255, 128, 0, 255, 0, 255],
            andMask: [0xc0, 0, 0, 0]);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(new SKColor(255, 0, 0, 128), bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void Decode32BitIconSupportsAlphaBitFields()
    {
        var masks = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0x00ff0000);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0x0000ff00);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x000000ff);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(12), 0xff000000);
        var icon = CreateIcon(
            bitCount: 32,
            xorPixels: [0, 0, 255, 128, 0, 255, 0, 255],
            andMask: [0xc0, 0, 0, 0],
            compression: 6,
            bitFieldMasks: masks);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(new SKColor(255, 0, 0, 128), bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeCursorUsesBitmapFrameAndIgnoresHotspotFields()
    {
        var cursor = CreateIcon(
            bitCount: 32,
            xorPixels: [0, 0, 255, 128, 0, 255, 0, 255],
            andMask: [0, 0, 0, 0]);
        BinaryPrimitives.WriteUInt16LittleEndian(cursor.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(cursor.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(cursor.AsSpan(12), 1);

        using var bitmap = SKBitmap.Decode(new SKData(cursor));

        Assert.Equal(new SKColor(255, 0, 0, 128), bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeCoreHeaderIconSupports24BitPixelsAndAndMask()
    {
        var icon = CreateCoreIcon(
            bitCount: 24,
            xorPixels: [0, 0, 255, 0, 255, 0, 0, 0],
            andMask: [0x40, 0, 0, 0]);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 0), bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeCoreHeaderIconSupportsThreeByteColorTable()
    {
        var icon = CreateCoreIcon(
            bitCount: 4,
            xorPixels: [0x12, 0, 0, 0],
            andMask: [0, 0, 0, 0],
            palette:
            [
                0, 0, 0,
                0, 0, 255,
                0, 255, 0
            ],
            colorCount: 3);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void Decode24BitIconWithoutAndMaskDefaultsToOpaque()
    {
        var icon = CreateIcon(
            bitCount: 24,
            xorPixels: [0, 0, 255, 0, 255, 0, 0, 0],
            andMask: []);

        using var bitmap = SKBitmap.Decode(new SKData(icon));

        Assert.Equal(SKColors.Red, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(1, 0));
    }

    [Fact]
    public void DecodeBitFieldIconRejectsOverlappingMasks()
    {
        var masks = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0xf800);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0xf800);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x001f);
        var icon = CreateIcon(
            bitCount: 16,
            xorPixels: [0, 0, 0, 0],
            andMask: [0, 0, 0, 0],
            compression: 3,
            bitFieldMasks: masks);

        Assert.Throws<InvalidOperationException>(() => SKBitmap.Decode(new SKData(icon)));
    }

    private static byte[] CreateIcon(
        ushort bitCount,
        byte[] xorPixels,
        byte[] andMask,
        byte[]? palette = null,
        uint colorCount = 0,
        uint compression = 0,
        byte[]? bitFieldMasks = null,
        int width = 2,
        int height = 1,
        uint? imageByteCount = null)
    {
        const int directorySize = 6 + 16;
        const int bitmapHeaderSize = 40;
        if (width is <= 0 or > 256 || height is <= 0 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        palette ??= [];
        bitFieldMasks ??= [];
        var payloadSize = checked(bitmapHeaderSize + bitFieldMasks.Length + palette.Length + xorPixels.Length + andMask.Length);
        var icon = new byte[checked(directorySize + payloadSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(4), 1);
        icon[6] = width == 256 ? (byte)0 : (byte)width;
        icon[7] = height == 256 ? (byte)0 : (byte)height;
        icon[8] = colorCount > byte.MaxValue ? (byte)0 : (byte)colorCount;
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(12), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(14), (uint)payloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(18), directorySize);

        var payload = icon.AsSpan(directorySize);
        BinaryPrimitives.WriteUInt32LittleEndian(payload, bitmapHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(20), imageByteCount ?? (uint)xorPixels.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.Slice(32), colorCount);

        var offset = bitmapHeaderSize;
        bitFieldMasks.CopyTo(payload.Slice(offset));
        offset += bitFieldMasks.Length;
        palette.CopyTo(payload.Slice(offset));
        offset += palette.Length;
        xorPixels.CopyTo(payload.Slice(offset));
        offset += xorPixels.Length;
        andMask.CopyTo(payload.Slice(offset));
        return icon;
    }

    private static byte[] CreateCoreIcon(
        ushort bitCount,
        byte[] xorPixels,
        byte[] andMask,
        byte[]? palette = null,
        byte colorCount = 0)
    {
        const int width = 2;
        const int height = 1;
        const int directorySize = 6 + 16;
        const int bitmapHeaderSize = 12;
        palette ??= [];
        var payloadSize = checked(bitmapHeaderSize + palette.Length + xorPixels.Length + andMask.Length);
        var icon = new byte[checked(directorySize + payloadSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(4), 1);
        icon[6] = width;
        icon[7] = height;
        icon[8] = colorCount;
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(12), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(14), (uint)payloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(18), directorySize);

        var payload = icon.AsSpan(directorySize);
        BinaryPrimitives.WriteUInt32LittleEndian(payload, bitmapHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(4), width);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(6), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(10), bitCount);

        var offset = bitmapHeaderSize;
        palette.CopyTo(payload.Slice(offset));
        offset += palette.Length;
        xorPixels.CopyTo(payload.Slice(offset));
        offset += xorPixels.Length;
        andMask.CopyTo(payload.Slice(offset));
        return icon;
    }
}
