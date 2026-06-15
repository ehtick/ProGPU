using System;

namespace ProGPU.Backend;

public readonly record struct Pbgra32PixelBuffer
{
    public Pbgra32PixelBuffer(int width, int height, int stride, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public byte[] Pixels { get; }

    public int RequiredByteLength => checked(Stride * Height);

    public bool IsValid
    {
        get
        {
            try
            {
                return Width > 0
                    && Height > 0
                    && Stride >= checked(Width * 4)
                    && Pixels != null
                    && Pixels.Length >= RequiredByteLength;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}

public readonly record struct PixelDataBuffer
{
    public PixelDataBuffer(
        int width,
        int height,
        int stride,
        PixelDataFormat format,
        byte[] pixels,
        Pbgra32Color[]? palette = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        Pixels = pixels;
        Palette = palette ?? Array.Empty<Pbgra32Color>();
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public PixelDataFormat Format { get; }

    public byte[] Pixels { get; }

    public Pbgra32Color[] Palette { get; }

    public bool TryGetRequiredByteLength(out int byteLength)
    {
        return PixelDataConverter.TryGetSourceByteLength(
            Width,
            Height,
            Stride,
            Format,
            out byteLength);
    }

    public bool TryConvertToPbgra32(out Pbgra32PixelBuffer buffer)
    {
        buffer = default;

        if (Pixels == null
            || (PixelDataConverter.RequiresPalette(Format) && (Palette == null || Palette.Length == 0))
            || !PixelDataConverter.TryConvertToPbgra32(
                Pixels,
                Width,
                Height,
                Stride,
                Format,
                Palette ?? Array.Empty<Pbgra32Color>(),
                out var pbgra32Pixels,
                out var pbgra32Stride))
        {
            return false;
        }

        buffer = new Pbgra32PixelBuffer(Width, Height, pbgra32Stride, pbgra32Pixels);
        return true;
    }

    public Pbgra32PixelBuffer ConvertToPbgra32()
    {
        if (!TryConvertToPbgra32(out var buffer))
        {
            throw new ArgumentException("Pixel data cannot be converted to PBgra32.", nameof(Pixels));
        }

        return buffer;
    }
}
