using System;

namespace ProGPU.Backend;

public enum PixelDataFormat
{
    Pbgra32,
    Bgra32,
    Bgr32,
    Bgr101010,
    Bgr24,
    Rgb24,
    BlackWhite,
    Gray2,
    Gray4,
    Gray8,
    Gray16,
    Bgr555,
    Bgr565,
    Rgb48,
    Rgba64,
    Prgba64,
    Cmyk32,
    Gray32Float,
    Rgb128Float,
    Rgba128Float,
    Prgba128Float,
    Indexed1,
    Indexed2,
    Indexed4,
    Indexed8
}

public readonly record struct Pbgra32Color(byte B, byte G, byte R, byte A)
{
    public static Pbgra32Color FromStraightArgb(byte alpha, byte red, byte green, byte blue)
    {
        return new Pbgra32Color(
            PixelDataConverter.Premultiply(blue, alpha),
            PixelDataConverter.Premultiply(green, alpha),
            PixelDataConverter.Premultiply(red, alpha),
            alpha);
    }
}

public static class PixelDataConverter
{
    public static bool RequiresPalette(PixelDataFormat format)
    {
        return format is PixelDataFormat.Indexed1
            or PixelDataFormat.Indexed2
            or PixelDataFormat.Indexed4
            or PixelDataFormat.Indexed8;
    }

    public static bool TryConvertToPbgra32(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int sourceStride,
        PixelDataFormat format,
        ReadOnlySpan<Pbgra32Color> palette,
        out byte[] pixels,
        out int stride)
    {
        pixels = Array.Empty<byte>();
        stride = 0;

        if (width <= 0
            || height <= 0
            || !TryGetMinimumStride(width, format, out var minimumStride)
            || sourceStride < minimumStride)
        {
            return false;
        }

        int requiredLength = checked(sourceStride * height);
        if (source.Length < requiredLength)
        {
            return false;
        }

        stride = checked(width * 4);
        pixels = ConvertToPbgra32(source, width, height, sourceStride, format, palette);
        return true;
    }

    public static byte[] ConvertToPbgra32(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int sourceStride,
        PixelDataFormat format,
        ReadOnlySpan<Pbgra32Color> palette)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(width <= 0 ? nameof(width) : nameof(height));
        }

        int minimumStride = GetMinimumStride(width, format);
        if (sourceStride < minimumStride)
        {
            throw new ArgumentException("Source stride is too small for the requested pixel format.", nameof(sourceStride));
        }

        int requiredLength = checked(sourceStride * height);
        if (source.Length < requiredLength)
        {
            throw new ArgumentException("Source buffer does not contain the requested image rows.", nameof(source));
        }

        var destinationStride = checked(width * 4);
        var destination = new byte[checked(destinationStride * height)];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * sourceStride;
            var destinationRow = y * destinationStride;

            for (var x = 0; x < width; x++)
            {
                var destinationOffset = destinationRow + x * 4;
                switch (format)
                {
                    case PixelDataFormat.Pbgra32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = source[sourceOffset + 3];
                        break;
                    }

                    case PixelDataFormat.Bgra32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var alpha = source[sourceOffset + 3];
                        destination[destinationOffset] = Premultiply(source[sourceOffset], alpha);
                        destination[destinationOffset + 1] = Premultiply(source[sourceOffset + 1], alpha);
                        destination[destinationOffset + 2] = Premultiply(source[sourceOffset + 2], alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case PixelDataFormat.Bgr32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Bgr101010:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var value = ReadUInt32(source, sourceOffset);
                        destination[destinationOffset] = Scale10BitChannel((int)(value & 0x3ff));
                        destination[destinationOffset + 1] = Scale10BitChannel((int)((value >> 10) & 0x3ff));
                        destination[destinationOffset + 2] = Scale10BitChannel((int)((value >> 20) & 0x3ff));
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Bgr24:
                    {
                        var sourceOffset = sourceRow + x * 3;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Rgb24:
                    {
                        var sourceOffset = sourceRow + x * 3;
                        destination[destinationOffset] = source[sourceOffset + 2];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset];
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Gray8:
                    {
                        var gray = source[sourceRow + x];
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.BlackWhite:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 1), 1);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Gray2:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 2), 3);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Gray4:
                    {
                        var gray = ExpandIndexedGray(ReadPackedValue(source, sourceRow, x, 4), 15);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Gray16:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        var gray = (byte)((value + 128) / 257);
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Bgr555:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        destination[destinationOffset] = Expand5BitChannel(value & 0x1f);
                        destination[destinationOffset + 1] = Expand5BitChannel((value >> 5) & 0x1f);
                        destination[destinationOffset + 2] = Expand5BitChannel((value >> 10) & 0x1f);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Bgr565:
                    {
                        var sourceOffset = sourceRow + x * 2;
                        var value = source[sourceOffset] | (source[sourceOffset + 1] << 8);
                        destination[destinationOffset] = Expand5BitChannel(value & 0x1f);
                        destination[destinationOffset + 1] = Expand6BitChannel((value >> 5) & 0x3f);
                        destination[destinationOffset + 2] = Expand5BitChannel((value >> 11) & 0x1f);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Rgb48:
                    {
                        var sourceOffset = sourceRow + x * 6;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        destination[destinationOffset] = Scale16BitChannel(blue);
                        destination[destinationOffset + 1] = Scale16BitChannel(green);
                        destination[destinationOffset + 2] = Scale16BitChannel(red);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Rgba64:
                    {
                        var sourceOffset = sourceRow + x * 8;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        var alpha = ReadUInt16(source, sourceOffset + 6);
                        destination[destinationOffset] = Premultiply16BitChannel(blue, alpha);
                        destination[destinationOffset + 1] = Premultiply16BitChannel(green, alpha);
                        destination[destinationOffset + 2] = Premultiply16BitChannel(red, alpha);
                        destination[destinationOffset + 3] = Scale16BitChannel(alpha);
                        break;
                    }

                    case PixelDataFormat.Prgba64:
                    {
                        var sourceOffset = sourceRow + x * 8;
                        var red = ReadUInt16(source, sourceOffset);
                        var green = ReadUInt16(source, sourceOffset + 2);
                        var blue = ReadUInt16(source, sourceOffset + 4);
                        var alpha = ReadUInt16(source, sourceOffset + 6);
                        destination[destinationOffset] = Scale16BitChannel(blue);
                        destination[destinationOffset + 1] = Scale16BitChannel(green);
                        destination[destinationOffset + 2] = Scale16BitChannel(red);
                        destination[destinationOffset + 3] = Scale16BitChannel(alpha);
                        break;
                    }

                    case PixelDataFormat.Cmyk32:
                    {
                        var sourceOffset = sourceRow + x * 4;
                        var cyan = source[sourceOffset];
                        var magenta = source[sourceOffset + 1];
                        var yellow = source[sourceOffset + 2];
                        var black = source[sourceOffset + 3];
                        destination[destinationOffset] = ConvertCmykChannel(yellow, black);
                        destination[destinationOffset + 1] = ConvertCmykChannel(magenta, black);
                        destination[destinationOffset + 2] = ConvertCmykChannel(cyan, black);
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Gray32Float:
                    {
                        var gray = ScRgbToSrgbByte(ReadSingle(source, sourceRow + x * 4));
                        destination[destinationOffset] = gray;
                        destination[destinationOffset + 1] = gray;
                        destination[destinationOffset + 2] = gray;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Rgb128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var red = ScRgbToSrgbByte(ReadSingle(source, sourceOffset));
                        var green = ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 4));
                        var blue = ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 8));
                        destination[destinationOffset] = blue;
                        destination[destinationOffset + 1] = green;
                        destination[destinationOffset + 2] = red;
                        destination[destinationOffset + 3] = 255;
                        break;
                    }

                    case PixelDataFormat.Rgba128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var alpha = ScRgbAlphaToByte(ReadSingle(source, sourceOffset + 12));
                        destination[destinationOffset] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 8)),
                            alpha);
                        destination[destinationOffset + 1] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset + 4)),
                            alpha);
                        destination[destinationOffset + 2] = Premultiply(
                            ScRgbToSrgbByte(ReadSingle(source, sourceOffset)),
                            alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case PixelDataFormat.Prgba128Float:
                    {
                        var sourceOffset = sourceRow + x * 16;
                        var alphaValue = Clamp01(ReadSingle(source, sourceOffset + 12));
                        var alpha = ScRgbAlphaToByte(alphaValue);
                        destination[destinationOffset] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset + 8), alphaValue)),
                            alpha);
                        destination[destinationOffset + 1] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset + 4), alphaValue)),
                            alpha);
                        destination[destinationOffset + 2] = Premultiply(
                            ScRgbToSrgbByte(UnpremultiplyScRgb(ReadSingle(source, sourceOffset), alphaValue)),
                            alpha);
                        destination[destinationOffset + 3] = alpha;
                        break;
                    }

                    case PixelDataFormat.Indexed1:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 1));
                        break;
                    }

                    case PixelDataFormat.Indexed2:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 2));
                        break;
                    }

                    case PixelDataFormat.Indexed4:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, ReadPackedValue(source, sourceRow, x, 4));
                        break;
                    }

                    case PixelDataFormat.Indexed8:
                    {
                        CopyPaletteColor(destination, destinationOffset, palette, source[sourceRow + x]);
                        break;
                    }

                    default:
                        throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.");
                }
            }
        }

        return destination;
    }

    internal static byte Premultiply(byte color, byte alpha)
    {
        return (byte)((color * alpha + 127) / 255);
    }

    private static int GetMinimumStride(int width, PixelDataFormat format)
    {
        if (!TryGetBitsPerPixel(format, out var bitsPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format.");
        }

        return checked((width * bitsPerPixel + 7) / 8);
    }

    private static bool TryGetMinimumStride(int width, PixelDataFormat format, out int stride)
    {
        stride = 0;
        if (!TryGetBitsPerPixel(format, out var bitsPerPixel))
        {
            return false;
        }

        stride = checked((width * bitsPerPixel + 7) / 8);
        return true;
    }

    private static bool TryGetBitsPerPixel(PixelDataFormat format, out int bitsPerPixel)
    {
        bitsPerPixel = format switch
        {
            PixelDataFormat.Pbgra32 or PixelDataFormat.Bgra32 or PixelDataFormat.Bgr32 or PixelDataFormat.Bgr101010
                or PixelDataFormat.Cmyk32 or PixelDataFormat.Gray32Float => 32,
            PixelDataFormat.Bgr24 or PixelDataFormat.Rgb24 => 24,
            PixelDataFormat.BlackWhite or PixelDataFormat.Indexed1 => 1,
            PixelDataFormat.Gray2 or PixelDataFormat.Indexed2 => 2,
            PixelDataFormat.Gray4 or PixelDataFormat.Indexed4 => 4,
            PixelDataFormat.Gray8 or PixelDataFormat.Indexed8 => 8,
            PixelDataFormat.Gray16 or PixelDataFormat.Bgr555 or PixelDataFormat.Bgr565 => 16,
            PixelDataFormat.Rgb48 => 48,
            PixelDataFormat.Rgba64 or PixelDataFormat.Prgba64 => 64,
            PixelDataFormat.Rgb128Float or PixelDataFormat.Rgba128Float or PixelDataFormat.Prgba128Float => 128,
            _ => 0
        };

        return bitsPerPixel > 0;
    }

    private static int ReadPackedValue(ReadOnlySpan<byte> source, int rowOffset, int x, int bitsPerPixel)
    {
        var bitOffset = x * bitsPerPixel;
        var packed = source[rowOffset + bitOffset / 8];
        var shift = 8 - bitsPerPixel - bitOffset % 8;
        return (packed >> shift) & ((1 << bitsPerPixel) - 1);
    }

    private static byte ExpandIndexedGray(int value, int maxValue)
    {
        return (byte)((value * 255 + maxValue / 2) / maxValue);
    }

    private static void CopyPaletteColor(byte[] destination, int destinationOffset, ReadOnlySpan<Pbgra32Color> palette, int index)
    {
        var color = index < palette.Length ? palette[index] : default;
        destination[destinationOffset] = color.B;
        destination[destinationOffset + 1] = color.G;
        destination[destinationOffset + 2] = color.R;
        destination[destinationOffset + 3] = color.A;
    }

    private static byte Expand5BitChannel(int value)
    {
        return (byte)((value << 3) | (value >> 2));
    }

    private static byte Expand6BitChannel(int value)
    {
        return (byte)((value << 2) | (value >> 4));
    }

    private static byte Scale10BitChannel(int value)
    {
        return (byte)((value * 255 + 511) / 1023);
    }

    private static int ReadUInt16(ReadOnlySpan<byte> source, int offset)
    {
        return source[offset] | (source[offset + 1] << 8);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
    {
        return (uint)(source[offset]
            | (source[offset + 1] << 8)
            | (source[offset + 2] << 16)
            | (source[offset + 3] << 24));
    }

    private static byte Scale16BitChannel(int value)
    {
        return (byte)((value + 128) / 257);
    }

    private static byte Premultiply16BitChannel(int color, int alpha)
    {
        var premultiplied = (int)(((long)color * alpha + 32767) / 65535);
        return Scale16BitChannel(premultiplied);
    }

    private static byte ConvertCmykChannel(byte colorant, byte black)
    {
        return (byte)(((255 - colorant) * (255 - black) + 127) / 255);
    }

    private static float ReadSingle(ReadOnlySpan<byte> source, int offset)
    {
        return BitConverter.ToSingle(source.Slice(offset, sizeof(float)));
    }

    private static byte ScRgbToSrgbByte(float value)
    {
        var clamped = Clamp01(value);
        var encoded = clamped <= 0.0031308f
            ? 12.92f * clamped
            : 1.055f * MathF.Pow(clamped, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    private static byte ScRgbAlphaToByte(float alpha)
    {
        return (byte)Math.Clamp((int)MathF.Round(Clamp01(alpha) * 255f), 0, 255);
    }

    private static float UnpremultiplyScRgb(float value, float alpha)
    {
        return alpha <= 0f ? 0f : value / alpha;
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }
}
