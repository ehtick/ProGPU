using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCodecCompatibilityTests
{
    [Fact]
    public void StaticPngMetadataAndOptionsMatchNative()
    {
        var defaults = SKCodecOptions.Default;
        Assert.Equal(SKZeroInitialized.No, defaults.ZeroInitialized);
        Assert.False(defaults.HasSubset);
        Assert.Equal(0, defaults.FrameIndex);
        Assert.Equal(-1, defaults.PriorFrame);

        var subsetOptions = new SKCodecOptions(new SKRectI(0, 0, 1, 1));
        Assert.True(subsetOptions.HasSubset);
        Assert.Equal(-1, subsetOptions.PriorFrame);
        Assert.Equal(new SKCodecOptions(SKZeroInitialized.No, subsetOptions.Subset!.Value), subsetOptions);

        using var data = SKData.CreateCopy(TwoPixelPngBytes());
        using var codec = SKCodec.Create(data);
        Assert.Equal(32, SKCodec.MinBufferedBytesNeeded);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(new SKSizeI(2, 1), codec.Info.Size);
        Assert.Equal(0, codec.FrameCount);
        Assert.Empty(codec.FrameInfo);
        Assert.Equal(0, codec.RepetitionCount);
        Assert.False(codec.GetFrameInfo(0, out var frameInfo));
        Assert.Equal(default, frameInfo);
        Assert.Equal(SKCodecScanlineOrder.TopDown, codec.ScanlineOrder);
        Assert.Equal(-1, codec.NextScanline);

        var requestedSubset = new SKRectI(-1, 0, 1, 1);
        Assert.False(codec.GetValidSubset(ref requestedSubset));
        Assert.Equal(new SKRectI(-1, 0, 1, 1), requestedSubset);
        Assert.Equal(SKSizeI.Empty, codec.GetScaledDimensions(0f));
        Assert.Equal(SKSizeI.Empty, codec.GetScaledDimensions(-1f));
        Assert.Equal(codec.Info.Size, codec.GetScaledDimensions(float.NaN));
        Assert.Equal(codec.Info.Size, codec.GetScaledDimensions(0.5f));
        Assert.Equal(codec.Info.Size, codec.GetScaledDimensions(1.5f));
    }

    [Fact]
    public void FullFramePixelsUseCpuConversionAndPreserveStridePadding()
    {
        using var data = SKData.CreateCopy(TwoPixelPngBytes());
        using var codec = SKCodec.Create(data);

        Assert.Equal(SKCodecResult.Success, codec.GetPixels(out var pixels));
        Assert.Equal("FF0000FF00FF00FF", Convert.ToHexString(pixels));
        Assert.Equal(pixels, codec.Pixels);

        var info = codec.Info.WithColorType(SKColorType.Bgra8888);
        const int rowBytes = 12;
        var destination = Enumerable.Repeat((byte)0xa5, rowBytes).ToArray();
        var address = Marshal.AllocHGlobal(destination.Length);
        try
        {
            Marshal.Copy(destination, 0, address, destination.Length);
            Assert.Equal(
                SKCodecResult.Success,
                codec.GetPixels(info, address, rowBytes, SKCodecOptions.Default));
            Marshal.Copy(address, destination, 0, destination.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }

        Assert.Equal("0000FFFF00FF00FF", Convert.ToHexString(destination.AsSpan(0, info.RowBytes)));
        Assert.Equal("A5A5A5A5", Convert.ToHexString(destination.AsSpan(info.RowBytes)));
    }

    [Fact]
    public void PngRejectsUnsupportedScalingSubsetsAndFrames()
    {
        using var data = SKData.CreateCopy(TwoPixelPngBytes());
        using var codec = SKCodec.Create(data);
        var scaledInfo = codec.Info.WithSize(1, 1);
        Assert.Equal(
            SKCodecResult.InvalidParameters,
            codec.StartIncrementalDecode(codec.Info, IntPtr.Zero, codec.Info.RowBytes));
        Assert.Throws<ArgumentNullException>(() => codec.StartIncrementalDecode(
            codec.Info,
            IntPtr.Zero,
            codec.Info.RowBytes,
            SKCodecOptions.Default));
        var address = Marshal.AllocHGlobal(codec.Info.BytesSize);
        try
        {
            Assert.Equal(SKCodecResult.InvalidScale, codec.GetPixels(scaledInfo, address));
            Assert.Equal(
                SKCodecResult.Unimplemented,
                codec.GetPixels(
                    scaledInfo,
                    address,
                    new SKCodecOptions(SKZeroInitialized.No, new SKRectI(0, 0, 1, 1))));
            Assert.Equal(
                SKCodecResult.InvalidParameters,
                codec.GetPixels(codec.Info, address, new SKCodecOptions(frameIndex: 1)));
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }

        Assert.Equal(
            SKCodecResult.InvalidParameters,
            codec.GetPixels(codec.Info, new byte[codec.Info.BytesSize - 1]));
    }

    [Fact]
    public void IncrementalDecodeWorksWhileScanlineDecodeRemainsUnavailable()
    {
        using var data = SKData.CreateCopy(TwoPixelPngBytes());
        using var codec = SKCodec.Create(data);
        var destination = new byte[codec.Info.BytesSize];
        var address = Marshal.AllocHGlobal(destination.Length);
        try
        {
            Assert.Equal(
                SKCodecResult.Success,
                codec.StartIncrementalDecode(codec.Info, address, codec.Info.RowBytes));
            Assert.Equal(SKCodecResult.Success, codec.IncrementalDecode(out var rowsDecoded));
            Assert.Equal(0, rowsDecoded);
            Marshal.Copy(address, destination, 0, destination.Length);
            Assert.Equal("FF0000FF00FF00FF", Convert.ToHexString(destination));
            Assert.Equal(SKCodecResult.InvalidParameters, codec.IncrementalDecode());

            Array.Clear(destination);
            Marshal.Copy(destination, 0, address, destination.Length);
            Assert.Equal(SKCodecResult.Unimplemented, codec.StartScanlineDecode(codec.Info));
            Assert.Equal(0, codec.GetScanlines(address, 1, codec.Info.RowBytes));
            Assert.Equal(0, codec.GetOutputScanline(0));
            Assert.False(codec.SkipScanlines(1));
            Marshal.Copy(address, destination, 0, destination.Length);
            Assert.Equal(new byte[destination.Length], destination);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [Fact]
    public void FactoryResultCodesDistinguishFilesFormatsAndTruncation()
    {
        var encoded = TwoPixelPngBytes();
        using (var stream = new MemoryStream(encoded))
        using (var codec = SKCodec.Create(stream, out var result))
        {
            Assert.NotNull(codec);
            Assert.Equal(SKCodecResult.Success, result);
        }

        var prefixed = new byte[encoded.Length + 3];
        encoded.CopyTo(prefixed, 3);
        using (var stream = new MemoryStream(prefixed) { Position = 3 })
        using (var managedStream = new SKManagedStream(stream))
        using (var codec = SKCodec.Create(managedStream, out var result))
        {
            Assert.NotNull(codec);
            Assert.Equal(SKCodecResult.Success, result);
            Assert.Equal(stream.Length, stream.Position);
        }

        using (var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 }))
        using (var codec = SKCodec.Create(stream, out var result))
        {
            Assert.Null(codec);
            Assert.Equal(SKCodecResult.Unimplemented, result);
        }

        using (var stream = new MemoryStream(TwoPixelPngBytes()[..8]))
        using (var codec = SKCodec.Create(stream, out var result))
        {
            Assert.Null(codec);
            Assert.Equal(SKCodecResult.IncompleteInput, result);
        }

        using (var codec = SKCodec.Create(
                   Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"),
                   out var result))
        {
            Assert.Null(codec);
            Assert.Equal(SKCodecResult.InternalError, result);
        }

        using var invalidFile = new SKFileStream(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"));
        Assert.Throws<ArgumentException>(() => SKCodec.Create(invalidFile, out _));
    }

    [Fact]
    public void JpegScalingUsesNativeEighthResolutionSteps()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            16,
            8,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var codec = SKCodec.Create(encoded);

        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(new SKSizeI(2, 1), codec.GetScaledDimensions(0.01f));
        Assert.Equal(new SKSizeI(4, 2), codec.GetScaledDimensions(0.2f));
        Assert.Equal(new SKSizeI(4, 2), codec.GetScaledDimensions(0.3f));
        Assert.Equal(new SKSizeI(8, 4), codec.GetScaledDimensions(0.5f));
        Assert.Equal(new SKSizeI(12, 6), codec.GetScaledDimensions(0.75f));
        Assert.Equal(new SKSizeI(16, 8), codec.GetScaledDimensions(1.5f));

        var info = codec.Info.WithSize(4, 2);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, out var pixels));
        Assert.Equal(info.BytesSize, pixels.Length);
        Assert.All(
            pixels.Chunk(4),
            pixel =>
            {
                Assert.InRange(pixel[0], (byte)240, byte.MaxValue);
                Assert.InRange(pixel[1], (byte)0, (byte)15);
                Assert.InRange(pixel[2], (byte)0, (byte)15);
                Assert.Equal(byte.MaxValue, pixel[3]);
            });
    }

    private static byte[] TwoPixelPngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwHwQBEPgD/U6VwW8AAAAASUVORK5CYII=");
}
