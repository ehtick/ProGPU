using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SkiaSharp;

internal static class SKEncodedImageDecoder
{
    public static DecodedImage Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (IsIconOrCursor(data))
        {
            return DecodeIcon(data);
        }

        var result = StbImageSharp.ImageResult.FromMemory(
            data,
            StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return new DecodedImage(result.Width, result.Height, result.Data, ReadPngColorSpace(data));
    }

    private static bool IsIconOrCursor(ReadOnlySpan<byte> data)
    {
        return data.Length >= 6
            && data[0] == 0
            && data[1] == 0
            && data[3] == 0
            && data[2] is 1 or 2;
    }

    private static DecodedImage DecodeIcon(byte[] data)
    {
        var span = data.AsSpan();
        var isCursor = span[2] == 2;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
        if (count == 0 || span.Length < 6 + count * 16)
        {
            throw new InvalidOperationException("Invalid ICO directory.");
        }

        IconEntry? selected = null;
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 6 + index * 16;
            var entry = span.Slice(entryOffset, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var byteCount = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            if ((ulong)imageOffset + byteCount > (ulong)span.Length || byteCount == 0)
            {
                continue;
            }

            var bitCount = isCursor
                ? ReadCursorFrameBitCount(span.Slice((int)imageOffset, (int)byteCount))
                : BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6, 2));
            var candidate = new IconEntry(width, height, bitCount, entry[2], (int)imageOffset, (int)byteCount);
            if (selected == null
                || candidate.Width * candidate.Height > selected.Value.Width * selected.Value.Height
                || candidate.Width * candidate.Height == selected.Value.Width * selected.Value.Height
                    && candidate.BitCount > selected.Value.BitCount)
            {
                selected = candidate;
            }
        }

        if (selected == null)
        {
            throw new InvalidOperationException("ICO contains no valid image frames.");
        }

        var icon = selected.Value;
        var payload = span.Slice(icon.Offset, icon.ByteCount);
        if (payload.Length >= 8
            && payload[0] == 0x89
            && payload[1] == 0x50
            && payload[2] == 0x4e
            && payload[3] == 0x47)
        {
            var result = StbImageSharp.ImageResult.FromMemory(
                payload.ToArray(),
                StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            return new DecodedImage(
                result.Width,
                result.Height,
                result.Data,
                ReadPngColorSpace(payload));
        }

        return DecodeIconBitmap(payload, icon);
    }

    private static ushort ReadCursorFrameBitCount(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12
            || (payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4e && payload[3] == 0x47))
        {
            return 0;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (headerSize == 12)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
        }

        if (payload.Length < 16)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
    }

    private static DecodedImage DecodeIconBitmap(ReadOnlySpan<byte> payload, IconEntry icon)
    {
        if (payload.Length < 12)
        {
            throw new InvalidOperationException("ICO bitmap header is truncated.");
        }

        var headerSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4)));
        var isCoreHeader = headerSize == 12;
        if (headerSize > payload.Length)
        {
            throw new InvalidOperationException("ICO bitmap header is truncated.");
        }

        if (!isCoreHeader && headerSize < 40)
        {
            throw new NotSupportedException("Only 12-byte core and 40-byte-or-newer ICO bitmap headers are supported.");
        }

        var dibWidth = isCoreHeader
            ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2))
            : BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
        var dibHeight = isCoreHeader
            ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2))
            : BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4));
        var bitCount = isCoreHeader
            ? BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2))
            : BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2));
        var compression = isCoreHeader
            ? 0u
            : BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(16, 4));
        var declaredImageByteCount = isCoreHeader
            ? 0u
            : BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(20, 4));
        var declaredColorCount = isCoreHeader
            ? icon.ColorCount
            : BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(32, 4));
        var isUncompressed = compression == 0;
        var usesBitFields = compression is 3 or 6;
        var usesRunLengthEncoding = compression is 1 or 2;
        var supportsCoreHeader = isCoreHeader && bitCount is 1 or 4 or 8 or 24;
        var supportsInfoHeader = !isCoreHeader
            && ((isUncompressed && bitCount is 1 or 4 or 8 or 16 or 24 or 32)
                || (usesBitFields && bitCount is 16 or 32)
                || (compression == 1 && bitCount == 8)
                || (compression == 2 && bitCount == 4));
        if (!supportsCoreHeader && !supportsInfoHeader)
        {
            throw new NotSupportedException(
                "Only indexed, RLE4/RLE8, 16-bit, 24-bit, 32-bit, and 16/32-bit bitfield ICO bitmap frames are supported.");
        }

        if (dibWidth == int.MinValue || dibHeight == int.MinValue)
        {
            throw new InvalidOperationException("ICO bitmap dimensions are invalid.");
        }

        var width = Math.Abs(dibWidth);
        var height = icon.Height > 0 ? icon.Height : Math.Abs(dibHeight) / 2;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("ICO bitmap dimensions are invalid.");
        }

        var pixelOffset = headerSize;
        var redMask = bitCount == 16 && isUncompressed ? 0x7c00u : 0u;
        var greenMask = bitCount == 16 && isUncompressed ? 0x03e0u : 0u;
        var blueMask = bitCount == 16 && isUncompressed ? 0x001fu : 0u;
        var alphaMask = 0u;
        if (usesBitFields)
        {
            if (headerSize >= 52)
            {
                redMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(40, 4));
                greenMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(44, 4));
                blueMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(48, 4));
                if (headerSize >= 56)
                {
                    alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(52, 4));
                }
            }
            else
            {
                var maskCount = compression == 6 ? 4 : 3;
                var maskByteCount = checked(maskCount * 4);
                if (pixelOffset > payload.Length - maskByteCount)
                {
                    throw new InvalidOperationException("ICO bitfield masks are truncated.");
                }

                redMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pixelOffset, 4));
                greenMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pixelOffset + 4, 4));
                blueMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pixelOffset + 8, 4));
                if (maskCount == 4)
                {
                    alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pixelOffset + 12, 4));
                }

                pixelOffset += maskByteCount;
            }

            ValidateBitFieldMasks(bitCount, compression, redMask, greenMask, blueMask, alphaMask);
        }

        ReadOnlySpan<byte> palette = default;
        var paletteEntrySize = isCoreHeader ? 3 : 4;
        if (bitCount <= 8)
        {
            var maximumColorCount = 1 << bitCount;
            var colorCount = declaredColorCount == 0
                ? maximumColorCount
                : checked((int)declaredColorCount);
            if (colorCount <= 0 || colorCount > maximumColorCount)
            {
                throw new InvalidOperationException("ICO color table size is invalid.");
            }

            var paletteByteCount = checked(colorCount * paletteEntrySize);
            if (pixelOffset > payload.Length - paletteByteCount)
            {
                throw new InvalidOperationException("ICO color table is truncated.");
            }

            palette = payload.Slice(pixelOffset, paletteByteCount);
            pixelOffset += paletteByteCount;
        }

        var bottomUp = dibHeight > 0;
        var xorRowBytes = 0;
        var xorByteCount = 0;
        byte[]? decodedIndices = null;
        if (usesRunLengthEncoding)
        {
            var availableByteCount = payload.Length - pixelOffset;
            var encodedByteCount = declaredImageByteCount == 0
                ? availableByteCount
                : checked((int)declaredImageByteCount);
            if (encodedByteCount > availableByteCount)
            {
                throw new InvalidOperationException("ICO RLE bitmap pixels are truncated.");
            }

            decodedIndices = new byte[checked(width * height)];
            var consumedByteCount = DecodeIconRle(
                payload.Slice(pixelOffset, encodedByteCount),
                decodedIndices,
                width,
                height,
                bitCount,
                bottomUp);
            xorByteCount = declaredImageByteCount == 0 ? consumedByteCount : encodedByteCount;
        }
        else
        {
            xorRowBytes = checked(((width * bitCount + 31) / 32) * 4);
            xorByteCount = checked(xorRowBytes * height);
        }

        if (pixelOffset > payload.Length - xorByteCount)
        {
            throw new InvalidOperationException("ICO bitmap pixels are truncated.");
        }

        var rgba = new byte[checked(width * height * 4)];
        var usesPixelAlpha = (bitCount == 32 && isUncompressed) || alphaMask != 0;
        var hasNonZeroAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var sourceY = bottomUp ? height - 1 - y : y;
            var sourceRow = payload.Slice(pixelOffset + sourceY * xorRowBytes, xorRowBytes);
            for (var x = 0; x < width; x++)
            {
                var destination = (y * width + x) * 4;
                if (bitCount <= 8)
                {
                    var colorIndex = decodedIndices is not null
                        ? decodedIndices[y * width + x]
                        : bitCount switch
                        {
                            1 => (sourceRow[x >> 3] >> (7 - (x & 7))) & 0x01,
                            4 => (sourceRow[x >> 1] >> (x % 2 == 0 ? 4 : 0)) & 0x0f,
                            _ => sourceRow[x]
                        };
                    var paletteOffset = colorIndex * paletteEntrySize;
                    if (paletteOffset + paletteEntrySize > palette.Length)
                    {
                        throw new InvalidOperationException("ICO bitmap references a missing color table entry.");
                    }

                    rgba[destination] = palette[paletteOffset + 2];
                    rgba[destination + 1] = palette[paletteOffset + 1];
                    rgba[destination + 2] = palette[paletteOffset];
                    rgba[destination + 3] = 255;
                    continue;
                }

                if (bitCount == 16)
                {
                    var packed = BinaryPrimitives.ReadUInt16LittleEndian(sourceRow.Slice(x * 2, 2));
                    rgba[destination] = ExtractMaskedChannel(packed, redMask);
                    rgba[destination + 1] = ExtractMaskedChannel(packed, greenMask);
                    rgba[destination + 2] = ExtractMaskedChannel(packed, blueMask);
                    rgba[destination + 3] = ExtractMaskedChannel(packed, alphaMask, 255);
                    hasNonZeroAlpha |= rgba[destination + 3] != 0;
                    continue;
                }

                if (bitCount == 24)
                {
                    var source = x * 3;
                    rgba[destination] = sourceRow[source + 2];
                    rgba[destination + 1] = sourceRow[source + 1];
                    rgba[destination + 2] = sourceRow[source];
                    rgba[destination + 3] = 255;
                    continue;
                }

                var source32 = x * 4;
                if (usesBitFields)
                {
                    var packed = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow.Slice(source32, 4));
                    rgba[destination] = ExtractMaskedChannel(packed, redMask);
                    rgba[destination + 1] = ExtractMaskedChannel(packed, greenMask);
                    rgba[destination + 2] = ExtractMaskedChannel(packed, blueMask);
                    rgba[destination + 3] = ExtractMaskedChannel(packed, alphaMask, 255);
                }
                else
                {
                    rgba[destination] = sourceRow[source32 + 2];
                    rgba[destination + 1] = sourceRow[source32 + 1];
                    rgba[destination + 2] = sourceRow[source32];
                    rgba[destination + 3] = sourceRow[source32 + 3];
                }

                hasNonZeroAlpha |= rgba[destination + 3] != 0;
            }
        }

        if (!usesPixelAlpha || !hasNonZeroAlpha)
        {
            ApplyIconMask(payload, pixelOffset + xorByteCount, rgba, width, height, bottomUp);
        }

        return new DecodedImage(width, height, rgba, null);
    }

    private static byte ExtractMaskedChannel(uint packed, uint mask, byte defaultValue = 0)
    {
        if (mask == 0)
        {
            return defaultValue;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var maximum = mask >> shift;
        var value = (packed & mask) >> shift;
        return (byte)(((ulong)value * 255UL + maximum / 2UL) / maximum);
    }

    private static int DecodeIconRle(
        ReadOnlySpan<byte> encoded,
        Span<byte> indices,
        int width,
        int height,
        ushort bitCount,
        bool bottomUp)
    {
        var offset = 0;
        var x = 0;
        var sourceY = 0;
        while (offset < encoded.Length)
        {
            if (offset > encoded.Length - 2)
            {
                throw new InvalidOperationException("ICO RLE command is truncated.");
            }

            var count = encoded[offset++];
            var value = encoded[offset++];
            if (count != 0)
            {
                for (var index = 0; index < count; index++)
                {
                    var colorIndex = bitCount == 8
                        ? value
                        : (byte)(index % 2 == 0 ? value >> 4 : value & 0x0f);
                    WriteIconRleIndex(indices, width, height, bottomUp, x++, sourceY, colorIndex);
                }

                continue;
            }

            if (value == 0)
            {
                x = 0;
                sourceY++;
                if (sourceY > height)
                {
                    throw new InvalidOperationException("ICO RLE rows exceed the bitmap height.");
                }

                continue;
            }

            if (value == 1)
            {
                return offset;
            }

            if (value == 2)
            {
                if (offset > encoded.Length - 2)
                {
                    throw new InvalidOperationException("ICO RLE delta is truncated.");
                }

                x = checked(x + encoded[offset++]);
                sourceY = checked(sourceY + encoded[offset++]);
                if (x > width || sourceY > height)
                {
                    throw new InvalidOperationException("ICO RLE delta exceeds the bitmap bounds.");
                }

                continue;
            }

            var absoluteCount = value;
            var absoluteByteCount = bitCount == 8
                ? absoluteCount
                : (absoluteCount + 1) / 2;
            var paddedByteCount = (absoluteByteCount + 1) & ~1;
            if (offset > encoded.Length - paddedByteCount)
            {
                throw new InvalidOperationException("ICO RLE absolute run is truncated.");
            }

            for (var index = 0; index < absoluteCount; index++)
            {
                var colorIndex = bitCount == 8
                    ? encoded[offset + index]
                    : (byte)(index % 2 == 0
                        ? encoded[offset + index / 2] >> 4
                        : encoded[offset + index / 2] & 0x0f);
                WriteIconRleIndex(indices, width, height, bottomUp, x++, sourceY, colorIndex);
            }

            offset += paddedByteCount;
        }

        throw new InvalidOperationException("ICO RLE bitmap is missing its end marker.");
    }

    private static void WriteIconRleIndex(
        Span<byte> indices,
        int width,
        int height,
        bool bottomUp,
        int x,
        int sourceY,
        byte colorIndex)
    {
        if ((uint)x >= (uint)width || (uint)sourceY >= (uint)height)
        {
            throw new InvalidOperationException("ICO RLE run exceeds the bitmap bounds.");
        }

        var destinationY = bottomUp ? height - 1 - sourceY : sourceY;
        indices[destinationY * width + x] = colorIndex;
    }

    private static void ValidateBitFieldMasks(
        ushort bitCount,
        uint compression,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask)
    {
        var validBits = bitCount == 32 ? uint.MaxValue : (1u << bitCount) - 1u;
        if (redMask == 0
            || greenMask == 0
            || blueMask == 0
            || compression == 6 && alphaMask == 0
            || ((redMask | greenMask | blueMask | alphaMask) & ~validBits) != 0
            || !IsContiguousMask(redMask)
            || !IsContiguousMask(greenMask)
            || !IsContiguousMask(blueMask)
            || alphaMask != 0 && !IsContiguousMask(alphaMask)
            || (redMask & greenMask) != 0
            || (redMask & blueMask) != 0
            || (redMask & alphaMask) != 0
            || (greenMask & blueMask) != 0
            || (greenMask & alphaMask) != 0
            || (blueMask & alphaMask) != 0)
        {
            throw new InvalidOperationException("ICO bitfield channel masks are invalid.");
        }
    }

    private static bool IsContiguousMask(uint mask)
    {
        var normalized = mask >> BitOperations.TrailingZeroCount(mask);
        return (normalized & (normalized + 1u)) == 0;
    }

    private static SKColorSpace? ReadPngColorSpace(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[]
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
        };
        if (data.Length < signature.Length || !data[..signature.Length].SequenceEqual(signature))
        {
            return null;
        }

        float? fileGamma = null;
        var offset = signature.Length;
        while (offset <= data.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            var chunkEnd = (ulong)offset + 12UL + chunkLength;
            if (chunkEnd > (ulong)data.Length)
            {
                break;
            }

            var type = data.Slice(offset + 4, 4);
            var payload = data.Slice(offset + 8, (int)chunkLength);
            if (type.SequenceEqual("sRGB"u8))
            {
                return SKColorSpace.CreateSrgb();
            }

            if (type.SequenceEqual("gAMA"u8) && payload.Length == 4)
            {
                var encodedGamma = BinaryPrimitives.ReadUInt32BigEndian(payload);
                if (encodedGamma != 0)
                {
                    fileGamma = encodedGamma / 100000f;
                }
            }

            if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset = checked((int)chunkEnd);
        }

        if (fileGamma is not { } gamma || !float.IsFinite(gamma) || gamma <= 0f)
        {
            return null;
        }

        if (MathF.Abs(gamma - 1f / 2.2f) <= 0.01f)
        {
            return SKColorSpace.CreateSrgb();
        }

        var transferFunction = new SKColorSpaceTransferFn(
            1f / gamma,
            1f,
            0f,
            0f,
            0f,
            0f,
            0f);
        return SKColorSpace.CreateRgb(transferFunction, SKColorSpaceXyz.Srgb);
    }

    private static void ApplyIconMask(
        ReadOnlySpan<byte> payload,
        int maskOffset,
        byte[] rgba,
        int width,
        int height,
        bool bottomUp)
    {
        var maskRowBytes = ((width + 31) / 32) * 4;
        if (maskOffset + maskRowBytes * height > payload.Length)
        {
            for (var pixel = 3; pixel < rgba.Length; pixel += 4)
            {
                rgba[pixel] = 255;
            }

            return;
        }

        for (var y = 0; y < height; y++)
        {
            var sourceY = bottomUp ? height - 1 - y : y;
            var maskRow = payload.Slice(maskOffset + sourceY * maskRowBytes, maskRowBytes);
            for (var x = 0; x < width; x++)
            {
                var transparent = (maskRow[x / 8] & (0x80 >> (x % 8))) != 0;
                rgba[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }

    internal readonly record struct DecodedImage(
        int Width,
        int Height,
        byte[] Pixels,
        SKColorSpace? ColorSpace);

    private readonly record struct IconEntry(
        int Width,
        int Height,
        ushort BitCount,
        byte ColorCount,
        int Offset,
        int ByteCount);
}
