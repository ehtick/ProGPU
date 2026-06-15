using ProGPU.Backend;
using Xunit;

namespace ProGPU.Tests;

public class PixelDataConverterTests
{
    [Fact]
    public void ConvertsStraightBgra32ToPremultipliedPbgra32()
    {
        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 100, 50, 200, 128 },
            width: 1,
            height: 1,
            sourceStride: 4,
            PixelDataFormat.Bgra32,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] pixels,
            out int stride));

        Assert.Equal(4, stride);
        Assert.Equal(new byte[] { 50, 25, 100, 128 }, pixels);
    }

    [Fact]
    public void ConvertsPackedAndIndexedRows()
    {
        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 0b0001_1011 },
            width: 4,
            height: 1,
            sourceStride: 1,
            PixelDataFormat.Gray2,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] grayPixels,
            out _));
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 85, 85, 85, 255, 170, 170, 170, 255, 255, 255, 255, 255 },
            grayPixels);

        Pbgra32Color[] palette =
        {
            Pbgra32Color.FromStraightArgb(255, 0, 0, 0),
            Pbgra32Color.FromStraightArgb(128, 100, 50, 20),
            Pbgra32Color.FromStraightArgb(255, 0, 255, 0),
            Pbgra32Color.FromStraightArgb(255, 10, 20, 30),
        };

        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 0b0001_1011 },
            width: 4,
            height: 1,
            sourceStride: 1,
            PixelDataFormat.Indexed2,
            palette,
            out byte[] indexedPixels,
            out _));
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 10, 25, 50, 128, 0, 255, 0, 255, 30, 20, 10, 255 },
            indexedPixels);
    }

    [Fact]
    public void ConvertsHighBitDepthIntegerRows()
    {
        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 0xff, 0xff, 0x00, 0x80, 0x00, 0x00 },
            width: 1,
            height: 1,
            sourceStride: 6,
            PixelDataFormat.Rgb48,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] rgb48Pixels,
            out _));
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, rgb48Pixels);

        uint bgr101010Value = 1023u | (512u << 10);
        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            new[]
            {
                (byte)bgr101010Value,
                (byte)(bgr101010Value >> 8),
                (byte)(bgr101010Value >> 16),
                (byte)(bgr101010Value >> 24)
            },
            width: 1,
            height: 1,
            sourceStride: 4,
            PixelDataFormat.Bgr101010,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] bgr101010Pixels,
            out _));
        Assert.Equal(new byte[] { 255, 128, 0, 255 }, bgr101010Pixels);
    }

    [Fact]
    public void ConvertsScRgbFloatRows()
    {
        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            FloatPixels(0f, 0.5f, 1f),
            width: 3,
            height: 1,
            sourceStride: 12,
            PixelDataFormat.Gray32Float,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] grayPixels,
            out _));
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 188, 188, 188, 255, 255, 255, 255, 255 },
            grayPixels);

        Assert.True(PixelDataConverter.TryConvertToPbgra32(
            FloatPixels(0.5f, 0.25f, 0f, 0.5f),
            width: 1,
            height: 1,
            sourceStride: 16,
            PixelDataFormat.Prgba128Float,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] prgbaPixels,
            out _));
        Assert.Equal(new byte[] { 0, 94, 128, 128 }, prgbaPixels);
    }

    [Fact]
    public void RejectsInvalidDimensionsOrShortSource()
    {
        Assert.False(PixelDataConverter.TryConvertToPbgra32(
            Array.Empty<byte>(),
            width: 0,
            height: 1,
            sourceStride: 4,
            PixelDataFormat.Pbgra32,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] zeroWidthPixels,
            out int zeroWidthStride));
        Assert.Empty(zeroWidthPixels);
        Assert.Equal(0, zeroWidthStride);

        Assert.False(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 1, 2, 3 },
            width: 1,
            height: 1,
            sourceStride: 4,
            PixelDataFormat.Pbgra32,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] shortPixels,
            out int shortStride));
        Assert.Empty(shortPixels);
        Assert.Equal(0, shortStride);

        Assert.False(PixelDataConverter.TryConvertToPbgra32(
            new byte[] { 1, 2, 3, 4 },
            width: 1,
            height: 1,
            sourceStride: 4,
            (PixelDataFormat)999,
            ReadOnlySpan<Pbgra32Color>.Empty,
            out byte[] invalidFormatPixels,
            out int invalidFormatStride));
        Assert.Empty(invalidFormatPixels);
        Assert.Equal(0, invalidFormatStride);
    }

    [Fact]
    public void PixelDataBufferConvertsToPbgra32UploadBuffer()
    {
        var source = new PixelDataBuffer(
            width: 1,
            height: 1,
            stride: 4,
            PixelDataFormat.Bgra32,
            new byte[] { 100, 50, 200, 128 });

        Assert.True(source.TryConvertToPbgra32(out var upload));

        Assert.Equal(1, upload.Width);
        Assert.Equal(1, upload.Height);
        Assert.Equal(4, upload.Stride);
        Assert.True(upload.IsValid);
        Assert.Equal(new byte[] { 50, 25, 100, 128 }, upload.Pixels);
    }

    [Fact]
    public void PixelDataBufferRejectsIndexedRowsWithoutPalette()
    {
        var source = new PixelDataBuffer(
            width: 2,
            height: 1,
            stride: 1,
            PixelDataFormat.Indexed4,
            new byte[] { 0x12 });

        Assert.False(source.TryConvertToPbgra32(out var upload));
        Assert.False(upload.IsValid);
    }

    [Fact]
    public void ComputesSourceByteLengthFromFormatStride()
    {
        Assert.True(PixelDataConverter.TryGetMinimumStride(
            width: 9,
            PixelDataFormat.Indexed1,
            out var stride));
        Assert.Equal(2, stride);

        Assert.True(PixelDataConverter.TryGetSourceByteLength(
            width: 9,
            height: 3,
            sourceStride: stride,
            PixelDataFormat.Indexed1,
            out var byteLength));
        Assert.Equal(6, byteLength);

        Assert.False(PixelDataConverter.TryGetSourceByteLength(
            width: 9,
            height: 3,
            sourceStride: 1,
            PixelDataFormat.Indexed1,
            out var invalidByteLength));
        Assert.Equal(0, invalidByteLength);

        Assert.False(PixelDataConverter.TryGetSourceByteLength(
            width: int.MaxValue,
            height: int.MaxValue,
            sourceStride: int.MaxValue,
            PixelDataFormat.Pbgra32,
            out var overflowByteLength));
        Assert.Equal(0, overflowByteLength);
    }

    private static byte[] FloatPixels(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, i * sizeof(float), sizeof(float));
        }

        return bytes;
    }
}
