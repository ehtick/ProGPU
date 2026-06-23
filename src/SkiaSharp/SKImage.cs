using System;
using System.IO;
using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace SkiaSharp;

public class SKImage : IDisposable
{
    public GpuTexture Texture { get; }
    private readonly bool _ownsTexture;
    public int Width => (int)Texture.Width;
    public int Height => (int)Texture.Height;

    public SKImage(GpuTexture texture)
        : this(texture, ownsTexture: false)
    {
    }

    private SKImage(GpuTexture texture, bool ownsTexture)
    {
        Texture = texture;
        _ownsTexture = ownsTexture;
    }

    public static SKImage FromBitmap(SKBitmap bitmap)
    {
        // Upload CPU bitmap pixels to a GPU texture for zero-copy fast GPU drawing!
        var ctx = SKContextHelper.GetContext();
        var texture = new GpuTexture(
            ctx,
            (uint)bitmap.Width,
            (uint)bitmap.Height,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
            "SKImage Texture",
            alphaMode: bitmap.AlphaType == SKAlphaType.Unpremul
                ? GpuTextureAlphaMode.Straight
                : GpuTextureAlphaMode.Premultiplied
        );

        byte[] buffer = bitmap.CopyRgba8888Rows();
        if (bitmap.AlphaType == SKAlphaType.Opaque)
        {
            ForceOpaqueAlpha(buffer);
        }

        texture.WritePixels(new ReadOnlySpan<byte>(buffer));

        return new SKImage(texture, ownsTexture: true);
    }

    public static SKImage FromTexture(GpuTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!texture.Usage.HasFlag(TextureUsage.CopySrc))
        {
            throw new InvalidOperationException(
                "Textures wrapped by SKImage.FromTexture must include TextureUsage.CopySrc so SKCanvas.DrawImage can retain a copy for deferred rendering.");
        }

        return new SKImage(texture);
    }

    private static void ForceOpaqueAlpha(byte[] rgbaPixels)
    {
        for (int i = 3; i < rgbaPixels.Length; i += 4)
        {
            rgbaPixels[i] = 255;
        }
    }

    internal static SKImage FromOwnedTexture(GpuTexture texture)
    {
        return new SKImage(texture, ownsTexture: true);
    }

    public void ScalePixels(SKPixmap dst, SKSamplingOptions sampling)
    {
        ArgumentNullException.ThrowIfNull(dst);
        if (dst.Pixels == IntPtr.Zero)
        {
            throw new ArgumentException("Destination pixmap must provide a pixel buffer.", nameof(dst));
        }

        if (dst.Info.Width <= 0 || dst.Info.Height <= 0 || Width <= 0 || Height <= 0)
        {
            return;
        }

        int dstRowBytes = dst.RowBytes > 0 ? dst.RowBytes : dst.Info.RowBytes;
        int minDstRowBytes = dst.Info.Width * 4;
        if (dstRowBytes < minDstRowBytes)
        {
            throw new ArgumentException("Destination row bytes must be large enough for one row.", nameof(dst));
        }

        byte[] src = ReadTexturePixelsAsRgba8888();
        bool sourcePremultiplied = Texture.AlphaMode == GpuTextureAlphaMode.Premultiplied;
        bool targetPremultiplied = dst.Info.AlphaType == SKAlphaType.Premul;
        bool forceOpaqueAlpha = dst.Info.AlphaType == SKAlphaType.Opaque;

        unsafe
        {
            fixed (byte* srcBase = src)
            {
                byte* dstBase = (byte*)dst.Pixels;
                for (int y = 0; y < dst.Info.Height; y++)
                {
                    int srcY = Math.Clamp((int)((long)y * Height / dst.Info.Height), 0, Height - 1);
                    byte* dstRow = dstBase + y * dstRowBytes;

                    for (int x = 0; x < dst.Info.Width; x++)
                    {
                        int srcX = Math.Clamp((int)((long)x * Width / dst.Info.Width), 0, Width - 1);
                        byte* srcPixel = srcBase + (srcY * Width + srcX) * 4;
                        byte* dstPixel = dstRow + x * 4;

                        byte alpha = srcPixel[3];
                        byte red = srcPixel[0];
                        byte green = srcPixel[1];
                        byte blue = srcPixel[2];

                        if (sourcePremultiplied && !targetPremultiplied)
                        {
                            red = UnpremultiplyChannel(red, alpha);
                            green = UnpremultiplyChannel(green, alpha);
                            blue = UnpremultiplyChannel(blue, alpha);
                        }
                        else if (!sourcePremultiplied && targetPremultiplied)
                        {
                            red = PremultiplyChannel(red, alpha);
                            green = PremultiplyChannel(green, alpha);
                            blue = PremultiplyChannel(blue, alpha);
                        }

                        if (forceOpaqueAlpha)
                        {
                            alpha = 255;
                        }

                        if (dst.Info.ColorType == SKColorType.Bgra8888)
                        {
                            dstPixel[0] = blue;
                            dstPixel[1] = green;
                            dstPixel[2] = red;
                            dstPixel[3] = alpha;
                        }
                        else
                        {
                            dstPixel[0] = red;
                            dstPixel[1] = green;
                            dstPixel[2] = blue;
                            dstPixel[3] = alpha;
                        }
                    }
                }
            }
        }
    }

    public void ReadPixels(SKImageInfo dstInfo, IntPtr dstPixels, int dstRowBytes, int srcX, int srcY, SKImageCachingHint cachingHint)
    {
        if (dstPixels == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(dstPixels));
        }

        byte[] pixels = ReadTexturePixelsAsRgba8888();
        int srcWidth = Width;
        int srcHeight = Height;
        
        int copySrcX = Math.Max(0, srcX);
        int copySrcY = Math.Max(0, srcY);
        int dstStartX = copySrcX - srcX;
        int dstStartY = copySrcY - srcY;
        int copyWidth = Math.Min(dstInfo.Width - dstStartX, srcWidth - copySrcX);
        int copyHeight = Math.Min(dstInfo.Height - dstStartY, srcHeight - copySrcY);
        
        if (copyWidth <= 0 || copyHeight <= 0) return;
        
        int actualDstRowBytes = dstRowBytes > 0 ? dstRowBytes : dstInfo.Width * 4;
        int minimumDstRowBytes = checked((dstStartX + copyWidth) * 4);
        if (actualDstRowBytes < minimumDstRowBytes)
        {
            throw new ArgumentException("Destination row bytes must cover the copied pixel range.", nameof(dstRowBytes));
        }

        bool forceOpaqueAlpha = dstInfo.AlphaType == SKAlphaType.Opaque;
        bool convertAlpha = forceOpaqueAlpha
            || (Texture.AlphaMode == GpuTextureAlphaMode.Premultiplied
            ? dstInfo.AlphaType == SKAlphaType.Unpremul
            : dstInfo.AlphaType == SKAlphaType.Premul);
        
        unsafe
        {
            fixed (byte* src = pixels)
            {
                byte* dst = (byte*)dstPixels;
                for (int y = 0; y < copyHeight; y++)
                {
                    int srcRowY = copySrcY + y;
                    byte* srcRow = src + (srcRowY * srcWidth + copySrcX) * 4;
                    byte* dstRow = dst + (dstStartY + y) * actualDstRowBytes + dstStartX * 4;
                    
                    if (dstInfo.ColorType == SKColorType.Bgra8888 || convertAlpha)
                    {
                        for (int x = 0; x < copyWidth; x++)
                        {
                            int srcIdx = x * 4;
                            int dstIdx = x * 4;
                            byte alpha = srcRow[srcIdx + 3];
                            byte red = srcRow[srcIdx];
                            byte green = srcRow[srcIdx + 1];
                            byte blue = srcRow[srcIdx + 2];

                            if (Texture.AlphaMode == GpuTextureAlphaMode.Premultiplied
                                && dstInfo.AlphaType == SKAlphaType.Unpremul)
                            {
                                red = UnpremultiplyChannel(red, alpha);
                                green = UnpremultiplyChannel(green, alpha);
                                blue = UnpremultiplyChannel(blue, alpha);
                            }
                            else if (Texture.AlphaMode == GpuTextureAlphaMode.Straight
                                && dstInfo.AlphaType == SKAlphaType.Premul)
                            {
                                red = PremultiplyChannel(red, alpha);
                                green = PremultiplyChannel(green, alpha);
                                blue = PremultiplyChannel(blue, alpha);
                            }

                            if (forceOpaqueAlpha)
                            {
                                alpha = 255;
                            }

                            if (dstInfo.ColorType == SKColorType.Bgra8888)
                            {
                                dstRow[dstIdx] = blue;
                                dstRow[dstIdx + 1] = green;
                                dstRow[dstIdx + 2] = red;
                                dstRow[dstIdx + 3] = alpha;
                            }
                            else
                            {
                                dstRow[dstIdx] = red;
                                dstRow[dstIdx + 1] = green;
                                dstRow[dstIdx + 2] = blue;
                                dstRow[dstIdx + 3] = alpha;
                            }
                        }
                    }
                    else
                    {
                        System.Buffer.MemoryCopy(srcRow, dstRow, actualDstRowBytes, copyWidth * 4);
                    }
                }
            }
        }
    }

    public SKImage ToRasterImage(bool share) => this;

    public SKData Encode()
    {
        return Encode(SKEncodedImageFormat.Png, 100);
    }

    public SKData Encode(SKEncodedImageFormat format, int quality)
    {
        byte[] pixels = ReadTexturePixelsAsRgba8888();
        if (Texture.AlphaMode == GpuTextureAlphaMode.Premultiplied)
        {
            UnpremultiplyRgba8888(pixels);
        }

        using (var ms = new MemoryStream())
        {
            var writer = new StbImageWriteSharp.ImageWriter();
            if (format == SKEncodedImageFormat.Jpeg)
            {
                writer.WriteJpg(pixels, Width, Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms, quality);
            }
            else
            {
                writer.WritePng(pixels, Width, Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
            }
            return new SKData(ms.ToArray());
        }
    }

    private byte[] ReadTexturePixelsAsRgba8888()
    {
        byte[] pixels = Texture.ReadPixels();
        if (Texture.Format is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb)
        {
            SwizzleBgraToRgba(pixels);
        }

        return pixels;
    }

    private static void SwizzleBgraToRgba(byte[] pixels)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    private static void UnpremultiplyRgba8888(byte[] pixels)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            if (alpha == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
                continue;
            }

            if (alpha == 255)
            {
                continue;
            }

            pixels[i] = UnpremultiplyChannel(pixels[i], alpha);
            pixels[i + 1] = UnpremultiplyChannel(pixels[i + 1], alpha);
            pixels[i + 2] = UnpremultiplyChannel(pixels[i + 2], alpha);
        }
    }

    private static byte UnpremultiplyChannel(byte value, int alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        return (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);
    }

    private static byte PremultiplyChannel(byte value, int alpha)
    {
        return (byte)((value * alpha + 127) / 255);
    }

    public void Dispose()
    {
        if (_ownsTexture)
        {
            Texture.Dispose();
        }
    }
}

public class SKPixmap
{
    public SKImageInfo Info { get; }
    public IntPtr Pixels { get; }
    public int RowBytes { get; }

    public SKPixmap(SKImageInfo info, IntPtr pixels, int rowBytes)
    {
        Info = info;
        Pixels = pixels;
        RowBytes = rowBytes;
    }
}

public class SKBitmap : IDisposable
{
    private IntPtr _pixels;
    private bool _ownsPixels;
    private int _width;
    private int _height;
    private int _rowBytes;
    private SKImageInfo _info;

    public int Width => _width;
    public int Height => _height;
    public SKImageInfo Info => _info;
    public SKColorType ColorType => _info.ColorType;
    public SKAlphaType AlphaType => _info.AlphaType;
    public int RowBytes => _rowBytes;
    public int BytesSize => RowBytes * _height;

    public SKBitmap()
    {
        _pixels = IntPtr.Zero;
        _ownsPixels = false;
        _rowBytes = 0;
    }

    public SKBitmap(int width, int height, bool isOpaque = false)
    {
        _width = width;
        _height = height;
        _info = new SKImageInfo(width, height, SKColorType.Rgba8888, isOpaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        _rowBytes = _info.RowBytes;
        _pixels = Marshal.AllocHGlobal(BytesSize);
        _ownsPixels = true;
        // Zero-initialize
        byte[] zero = new byte[BytesSize];
        Marshal.Copy(zero, 0, _pixels, BytesSize);
    }

    public SKBitmap(SKImageInfo info)
    {
        _width = info.Width;
        _height = info.Height;
        _info = info;
        _rowBytes = _info.RowBytes;
        _pixels = Marshal.AllocHGlobal(BytesSize);
        _ownsPixels = true;
        // Zero-initialize
        byte[] zero = new byte[BytesSize];
        Marshal.Copy(zero, 0, _pixels, BytesSize);
    }

    public IntPtr GetPixels() => _pixels;

    public void InstallPixels(SKImageInfo info, IntPtr pixels, int rowBytes)
    {
        int actualRowBytes = rowBytes > 0 ? rowBytes : info.RowBytes;
        int minRowBytes = info.Width * 4;
        if (info.Height > 0 && actualRowBytes < minRowBytes)
        {
            throw new ArgumentException("Row bytes must be large enough for one bitmap row.", nameof(rowBytes));
        }

        if (_ownsPixels && _pixels != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_pixels);
        }
        _width = info.Width;
        _height = info.Height;
        _info = info;
        _rowBytes = actualRowBytes;
        _pixels = pixels;
        _ownsPixels = false;
    }

    public SKBitmap Copy()
    {
        var copy = new SKBitmap(_info);
        CopyRows(_pixels, RowBytes, copy.GetPixels(), copy.RowBytes, _width, _height);
        return copy;
    }

    public void SetImmutable() { }

    public bool CanCopyTo(SKColorType type) => type == SKColorType.Rgba8888 || type == SKColorType.Bgra8888;

    public static SKBitmap Decode(SKData data)
    {
        using (var ms = new MemoryStream(data.Bytes))
        {
            var result = StbImageSharp.ImageResult.FromStream(ms, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var bmp = new SKBitmap(new SKImageInfo(result.Width, result.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(result.Data, 0, bmp.GetPixels(), result.Data.Length);
            return bmp;
        }
    }

    public static SKBitmap Decode(SKCodec codec, SKImageInfo info)
    {
        using var ms = new MemoryStream(codec.EncodedBytes);
        var result = StbImageSharp.ImageResult.FromStream(ms, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var targetInfo = info.Width > 0 && info.Height > 0
            ? info
            : new SKImageInfo(result.Width, result.Height, info.ColorType, info.AlphaType, info.ColorSpace);
        var bitmap = new SKBitmap(targetInfo);

        unsafe
        {
            fixed (byte* src = result.Data)
            {
                byte* dst = (byte*)bitmap.GetPixels();
                for (int y = 0; y < targetInfo.Height; y++)
                {
                    int srcY = targetInfo.Height == result.Height
                        ? y
                        : Math.Clamp((int)((long)y * result.Height / targetInfo.Height), 0, result.Height - 1);
                    byte* dstRow = dst + y * bitmap.RowBytes;

                    for (int x = 0; x < targetInfo.Width; x++)
                    {
                        int srcX = targetInfo.Width == result.Width
                            ? x
                            : Math.Clamp((int)((long)x * result.Width / targetInfo.Width), 0, result.Width - 1);
                        byte* srcPixel = src + (srcY * result.Width + srcX) * 4;
                        byte* dstPixel = dstRow + x * 4;
                        byte alpha = srcPixel[3];
                        byte red = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[0], alpha) : srcPixel[0];
                        byte green = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[1], alpha) : srcPixel[1];
                        byte blue = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[2], alpha) : srcPixel[2];

                        if (targetInfo.ColorType == SKColorType.Bgra8888)
                        {
                            dstPixel[0] = blue;
                            dstPixel[1] = green;
                            dstPixel[2] = red;
                            dstPixel[3] = alpha;
                        }
                        else
                        {
                            dstPixel[0] = red;
                            dstPixel[1] = green;
                            dstPixel[2] = blue;
                            dstPixel[3] = alpha;
                        }
                    }
                }
            }
        }

        return bitmap;
    }

    public SKPixmap PeekPixels()
    {
        return new SKPixmap(_info, _pixels, RowBytes);
    }

    public SKBitmap Resize(SKImageInfo info, SKSamplingOptions sampling)
    {
        // Create scaled bitmap
        var resized = new SKBitmap(info);
        // Simple pixel scaling (nearest neighbor/bilinear stub)
        byte[] src = new byte[BytesSize];
        Marshal.Copy(_pixels, src, 0, BytesSize);

        byte[] dst = new byte[resized.BytesSize];
        float xRatio = (float)_width / info.Width;
        float yRatio = (float)_height / info.Height;

        for (int y = 0; y < info.Height; y++)
        {
            int srcY = (int)(y * yRatio);
            srcY = Math.Clamp(srcY, 0, _height - 1);
            for (int x = 0; x < info.Width; x++)
            {
                int srcX = (int)(x * xRatio);
                srcX = Math.Clamp(srcX, 0, _width - 1);

                int srcOffset = srcY * RowBytes + srcX * 4;
                int dstOffset = (y * info.Width + x) * 4;

                Array.Copy(src, srcOffset, dst, dstOffset, 4);
            }
        }

        Marshal.Copy(dst, 0, resized.GetPixels(), dst.Length);
        return resized;
    }

    internal byte[] CopyRgba8888Rows()
    {
        byte[] buffer = new byte[_width * _height * 4];
        if (_pixels == IntPtr.Zero || _width <= 0 || _height <= 0)
        {
            return buffer;
        }

        unsafe
        {
            byte* src = (byte*)_pixels;
            fixed (byte* dst = buffer)
            {
                for (int y = 0; y < _height; y++)
                {
                    byte* srcRow = src + y * RowBytes;
                    byte* dstRow = dst + y * _width * 4;

                    if (ColorType == SKColorType.Bgra8888)
                    {
                        for (int x = 0; x < _width; x++)
                        {
                            int srcIdx = x * 4;
                            int dstIdx = x * 4;
                            dstRow[dstIdx] = srcRow[srcIdx + 2];
                            dstRow[dstIdx + 1] = srcRow[srcIdx + 1];
                            dstRow[dstIdx + 2] = srcRow[srcIdx];
                            dstRow[dstIdx + 3] = srcRow[srcIdx + 3];
                        }
                    }
                    else
                    {
                        System.Buffer.MemoryCopy(srcRow, dstRow, _width * 4, _width * 4);
                    }
                }
            }
        }

        return buffer;
    }

    private static unsafe void CopyRows(IntPtr source, int sourceRowBytes, IntPtr destination, int destinationRowBytes, int width, int height)
    {
        byte* src = (byte*)source;
        byte* dst = (byte*)destination;
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++)
        {
            System.Buffer.MemoryCopy(src + y * sourceRowBytes, dst + y * destinationRowBytes, destinationRowBytes, rowBytes);
        }
    }

    private static byte Premultiply(byte color, byte alpha)
    {
        return (byte)((color * alpha + 127) / 255);
    }

    public void Dispose()
    {
        if (_ownsPixels && _pixels != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_pixels);
            _pixels = IntPtr.Zero;
        }
    }
}

public class SKManagedStream : SKStream
{
    public Stream Stream { get; }

    public SKManagedStream(Stream stream)
    {
        Stream = stream;
    }
}
