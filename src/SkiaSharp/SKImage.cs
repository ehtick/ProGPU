using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace SkiaSharp;

public class SKImage : IDisposable
{
    public IntPtr Handle { get; } = SKObjectHandle.Create();
    public GpuTexture Texture { get; }
    private readonly bool _ownsTexture;
    private readonly SKImageInfo _info;
    public int Width => (int)Texture.Width;
    public int Height => (int)Texture.Height;
    public SKImageInfo Info => _info;
    public SKColorType ColorType => _info.ColorType;
    public SKAlphaType AlphaType => _info.AlphaType;
    public SKColorSpace? ColorSpace => _info.ColorSpace;
    public bool IsAlphaOnly => ColorType == SKColorType.Alpha8;
    public bool IsLazyGenerated => false;
    public bool IsTextureBacked => true;

    public SKImage(GpuTexture texture)
        : this(texture, ownsTexture: false, CreateTextureInfo(texture))
    {
    }

    private SKImage(GpuTexture texture, bool ownsTexture, SKImageInfo info)
    {
        Texture = texture;
        _ownsTexture = ownsTexture;
        _info = new SKImageInfo(
            (int)texture.Width,
            (int)texture.Height,
            info.ColorType,
            info.AlphaType,
            info.ColorSpace);
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

        return new SKImage(texture, ownsTexture: true, bitmap.Info);
    }

    public static SKImage? FromEncodedData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            using var bitmap = SKBitmap.Decode(new SKData(data));
            return FromBitmap(bitmap);
        }
        catch (Exception exception) when (IsInvalidEncodedImageException(exception))
        {
            return null;
        }
    }

    public static SKImage? FromEncodedData(ReadOnlySpan<byte> data) =>
        FromEncodedData(data.ToArray());

    public static SKImage? FromEncodedData(SKData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var bitmap = SKBitmap.Decode(data);
        return FromBitmap(bitmap);
    }

    public static SKImage? FromEncodedData(Stream data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var encoded = SKData.Create(data);
        return FromEncodedData(encoded);
    }

    private static bool IsInvalidEncodedImageException(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or FormatException or IndexOutOfRangeException;

    public static SKImage FromPixels(SKImageInfo info, IntPtr pixels, int rowBytes)
    {
        using var bitmap = new SKBitmap();
        bitmap.InstallPixels(info, pixels, rowBytes);
        return FromBitmap(bitmap);
    }

    public static SKImage FromPicture(SKPicture picture, SKSizeI dimensions)
    {
        return FromPicture(picture, dimensions, SKMatrix.Identity);
    }

    public static SKImage FromPicture(SKPicture picture, SKSizeI dimensions, SKMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (dimensions.Width <= 0 || dimensions.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Picture image dimensions must be positive.");
        }

        using var surface = SKSurface.Create(new SKImageInfo(
            dimensions.Width,
            dimensions.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SetMatrix(matrix);
        surface.Canvas.DrawPicture(picture);
        return surface.Snapshot();
    }

    public static SKImage? FromAdoptedTexture(
        GRContext context,
        GRBackendTexture texture,
        GRSurfaceOrigin origin,
        SKColorType colorType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(texture);
        var gpuTexture = texture.BackendTexture;
        if (gpuTexture == null)
        {
            return null;
        }

        if (!ReferenceEquals(gpuTexture.Context, context.Context))
        {
            throw new InvalidOperationException("The adopted backend texture belongs to a different ProGPU context.");
        }

        if ((gpuTexture.Usage & TextureUsage.TextureBinding) == 0 ||
            (gpuTexture.Usage & TextureUsage.CopySrc) == 0)
        {
            throw new InvalidOperationException(
                "Adopted backend textures must include TextureUsage.TextureBinding and TextureUsage.CopySrc.");
        }

        if (gpuTexture.SampleCount != 1)
        {
            throw new NotSupportedException("This WebGPU-backed Skia shim can only adopt single-sampled textures.");
        }

        var alphaType = gpuTexture.AlphaMode == GpuTextureAlphaMode.Straight
            ? SKAlphaType.Unpremul
            : SKAlphaType.Premul;
        return new SKImage(
            gpuTexture,
            ownsTexture: true,
            new SKImageInfo(texture.Width, texture.Height, colorType, alphaType));
    }

    public static SKImage? FromAdoptedTexture(
        GRContext context,
        GRBackendTexture texture,
        SKColorType colorType) =>
        FromAdoptedTexture(context, texture, GRSurfaceOrigin.TopLeft, colorType);

    public static SKImage FromTexture(GpuTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!texture.Usage.HasFlag(TextureUsage.CopySrc))
        {
            throw new InvalidOperationException(
                "Textures wrapped by SKImage.FromTexture must include TextureUsage.CopySrc so SKCanvas.DrawImage can retain a copy for deferred rendering.");
        }

        return new SKImage(texture, ownsTexture: false, CreateTextureInfo(texture));
    }

    private static void ForceOpaqueAlpha(byte[] rgbaPixels)
    {
        for (int i = 3; i < rgbaPixels.Length; i += 4)
        {
            rgbaPixels[i] = 255;
        }
    }

    internal static SKImage FromOwnedTexture(GpuTexture texture, SKImageInfo? info = null)
    {
        return new SKImage(texture, ownsTexture: true, info ?? CreateTextureInfo(texture));
    }

    internal SKImage CreateOwnedCopy()
    {
        var texture = new GpuTexture(
            Texture.Context,
            Texture.Width,
            Texture.Height,
            Texture.Format,
            TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
            "SKImage Shader Retained Texture",
            alphaMode: Texture.AlphaMode);
        texture.CopyFrom(Texture);
        return FromOwnedTexture(texture, _info);
    }

    public SKShader ToShader(
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix)
    {
        return SKShader.CreateImage(CreateOwnedCopy(), tileModeX, tileModeY, localMatrix);
    }

    public SKShader ToShader(SKShaderTileMode tileModeX, SKShaderTileMode tileModeY)
    {
        return ToShader(tileModeX, tileModeY, SKMatrix.Identity);
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
                                && (dstInfo.AlphaType == SKAlphaType.Unpremul || forceOpaqueAlpha))
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

    private static SKImageInfo CreateTextureInfo(GpuTexture texture)
    {
        var colorType = texture.Format is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb
            ? SKColorType.Bgra8888
            : SKColorType.Rgba8888;
        var alphaType = texture.AlphaMode == GpuTextureAlphaMode.Straight
            ? SKAlphaType.Unpremul
            : SKAlphaType.Premul;
        var colorSpace = texture.Format is TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8UnormSrgb
            ? SKColorSpace.CreateSrgb()
            : null;
        return new SKImageInfo((int)texture.Width, (int)texture.Height, colorType, alphaType, colorSpace);
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
    private readonly object _canvasSync = new();
    private WeakReference<SKCanvas>? _attachedCanvas;
    private IntPtr _pixels;
    private bool _ownsPixels;
    private int _width;
    private int _height;
    private int _rowBytes;
    private SKImageInfo _info;
    private SKBitmapReleaseDelegate? _releaseDelegate;
    private object? _releaseContext;

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

    public SKBitmap(int width, int height, SKColorType colorType, SKAlphaType alphaType)
        : this(new SKImageInfo(width, height, colorType, alphaType))
    {
    }

    internal void AttachCanvas(SKCanvas canvas)
    {
        SKCanvas? previous = null;
        lock (_canvasSync)
        {
            if (_attachedCanvas?.TryGetTarget(out var attached) == true && !ReferenceEquals(attached, canvas))
            {
                previous = attached;
            }

            _attachedCanvas = new WeakReference<SKCanvas>(canvas);
        }

        previous?.Flush();
    }

    internal void DetachCanvas(SKCanvas canvas)
    {
        lock (_canvasSync)
        {
            if (_attachedCanvas?.TryGetTarget(out var attached) == true && ReferenceEquals(attached, canvas))
            {
                _attachedCanvas = null;
            }
        }
    }

    private void FlushAttachedCanvas()
    {
        SKCanvas? canvas = null;
        lock (_canvasSync)
        {
            _attachedCanvas?.TryGetTarget(out canvas);
        }

        canvas?.Flush();
    }

    public IntPtr GetPixels() => _pixels;

    public IntPtr GetAddress(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinates must be inside the bitmap.");
        }

        return IntPtr.Add(_pixels, checked(y * RowBytes + x * _info.BytesPerPixel));
    }

    public SKColor GetPixel(int x, int y)
    {
        var address = GetAddress(x, y);
        unsafe
        {
            var pixel = (byte*)address;
            if (ColorType == SKColorType.Rgb565)
            {
                ushort value = (ushort)(pixel[0] | (pixel[1] << 8));
                return new SKColor(
                    (byte)(((value >> 11) & 0x1f) * 255 / 31),
                    (byte)(((value >> 5) & 0x3f) * 255 / 63),
                    (byte)((value & 0x1f) * 255 / 31),
                    255);
            }

            var alpha = pixel[3];
            var red = ColorType == SKColorType.Bgra8888 ? pixel[2] : pixel[0];
            var green = pixel[1];
            var blue = ColorType == SKColorType.Bgra8888 ? pixel[0] : pixel[2];
            if (AlphaType == SKAlphaType.Premul && alpha is > 0 and < 255)
            {
                red = Unpremultiply(red, alpha);
                green = Unpremultiply(green, alpha);
                blue = Unpremultiply(blue, alpha);
            }

            return new SKColor(red, green, blue, alpha);
        }
    }

    public void SetPixel(int x, int y, SKColor color)
    {
        var address = GetAddress(x, y);
        unsafe
        {
            var pixel = (byte*)address;
            var alpha = AlphaType == SKAlphaType.Opaque ? (byte)255 : color.A;
            var red = AlphaType == SKAlphaType.Premul ? Premultiply(color.R, alpha) : color.R;
            var green = AlphaType == SKAlphaType.Premul ? Premultiply(color.G, alpha) : color.G;
            var blue = AlphaType == SKAlphaType.Premul ? Premultiply(color.B, alpha) : color.B;
            if (ColorType == SKColorType.Rgb565)
            {
                var value = PackRgb565(red, green, blue);
                pixel[0] = (byte)value;
                pixel[1] = (byte)(value >> 8);
                return;
            }

            if (ColorType == SKColorType.Bgra8888)
            {
                pixel[0] = blue;
                pixel[1] = green;
                pixel[2] = red;
            }
            else
            {
                pixel[0] = red;
                pixel[1] = green;
                pixel[2] = blue;
            }

            pixel[3] = alpha;
        }
    }

    public void InstallPixels(SKImageInfo info, IntPtr pixels, int rowBytes)
    {
        int actualRowBytes = rowBytes > 0 ? rowBytes : info.RowBytes;
        int minRowBytes = info.RowBytes;
        if (info.Height > 0 && actualRowBytes < minRowBytes)
        {
            throw new ArgumentException("Row bytes must be large enough for one bitmap row.", nameof(rowBytes));
        }

        ReleasePixels();
        _width = info.Width;
        _height = info.Height;
        _info = info;
        _rowBytes = actualRowBytes;
        _pixels = pixels;
        _ownsPixels = false;
    }

    public void InstallPixels(
        SKImageInfo info,
        IntPtr pixels,
        int rowBytes,
        SKBitmapReleaseDelegate releaseProc,
        object context)
    {
        InstallPixels(info, pixels, rowBytes);
        _releaseDelegate = releaseProc;
        _releaseContext = context;
    }

    public void Erase(SKColor color)
    {
        if (_pixels == IntPtr.Zero || _width <= 0 || _height <= 0)
        {
            return;
        }

        var alpha = color.A;
        var red = _info.AlphaType == SKAlphaType.Premul ? Premultiply(color.R, alpha) : color.R;
        var green = _info.AlphaType == SKAlphaType.Premul ? Premultiply(color.G, alpha) : color.G;
        var blue = _info.AlphaType == SKAlphaType.Premul ? Premultiply(color.B, alpha) : color.B;
        unsafe
        {
            var destination = (byte*)_pixels;
            for (var y = 0; y < _height; y++)
            {
                var row = destination + y * RowBytes;
                for (var x = 0; x < _width; x++)
                {
                    if (_info.ColorType == SKColorType.Rgb565)
                    {
                        var pixel565 = row + x * 2;
                        ushort value = PackRgb565(red, green, blue);
                        pixel565[0] = (byte)value;
                        pixel565[1] = (byte)(value >> 8);
                        continue;
                    }

                    var pixel = row + x * 4;
                    if (_info.ColorType == SKColorType.Bgra8888)
                    {
                        pixel[0] = blue;
                        pixel[1] = green;
                        pixel[2] = red;
                    }
                    else
                    {
                        pixel[0] = red;
                        pixel[1] = green;
                        pixel[2] = blue;
                    }

                    pixel[3] = alpha;
                }
            }
        }
    }

    public void NotifyPixelsChanged()
    {
    }

    public SKBitmap Copy()
    {
        var copy = new SKBitmap(_info);
        CopyRows(_pixels, RowBytes, copy.GetPixels(), copy.RowBytes, _info.RowBytes, _height);
        return copy;
    }

    public void SetImmutable() { }

    public bool CanCopyTo(SKColorType type) =>
        type is SKColorType.Rgba8888 or SKColorType.Bgra8888 or SKColorType.Rgb565;

    public static SKBitmap Decode(SKData data)
    {
        var result = SKEncodedImageDecoder.Decode(data.Bytes);
        var bmp = new SKBitmap(new SKImageInfo(
            result.Width,
            result.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul,
            result.ColorSpace));
        Marshal.Copy(result.Pixels, 0, bmp.GetPixels(), result.Pixels.Length);
        return bmp;
    }

    public static SKBitmap FromImage(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var info = new SKImageInfo(
            image.Width,
            image.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            image.ColorSpace);
        var bitmap = new SKBitmap(info);
        image.ReadPixels(
            info,
            bitmap.GetPixels(),
            bitmap.RowBytes,
            0,
            0,
            SKImageCachingHint.Allow);
        return bitmap;
    }

    public static SKBitmap Decode(Stream? stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var data = SKData.Create(stream);
        return Decode(data);
    }

    public static SKBitmap Decode(SKCodec codec, SKImageInfo info)
    {
        var result = SKEncodedImageDecoder.Decode(codec.EncodedBytes);
        var targetInfo = info.Width > 0 && info.Height > 0
            ? info
            : new SKImageInfo(result.Width, result.Height, info.ColorType, info.AlphaType, info.ColorSpace);
        var bitmap = new SKBitmap(targetInfo);

        unsafe
        {
            fixed (byte* src = result.Pixels)
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
                        byte* dstPixel = dstRow + x * targetInfo.BytesPerPixel;
                        byte alpha = targetInfo.AlphaType == SKAlphaType.Opaque ? (byte)255 : srcPixel[3];
                        byte red = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[0], alpha) : srcPixel[0];
                        byte green = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[1], alpha) : srcPixel[1];
                        byte blue = targetInfo.AlphaType == SKAlphaType.Premul ? Premultiply(srcPixel[2], alpha) : srcPixel[2];

                        if (targetInfo.ColorType == SKColorType.Rgb565)
                        {
                            ushort value = PackRgb565(red, green, blue);
                            dstPixel[0] = (byte)value;
                            dstPixel[1] = (byte)(value >> 8);
                        }
                        else if (targetInfo.ColorType == SKColorType.Bgra8888)
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
        FlushAttachedCanvas();
        if (_pixels == IntPtr.Zero
            || _width <= 0
            || _height <= 0
            || info.Width <= 0
            || info.Height <= 0
            || !SupportsResizeColorType(ColorType)
            || !SupportsResizeColorType(info.ColorType)
            || (sampling.UseCubic
                && (!float.IsFinite(sampling.CubicResampler.B)
                    || !float.IsFinite(sampling.CubicResampler.C))))
        {
            return null!;
        }

        var resized = new SKBitmap(info);
        if (!sampling.UseCubic && sampling.FilterMode != SKFilterMode.Linear)
        {
            ResizeNearestPixels(resized, info);
            return resized;
        }

        var source = CopyResizeSourcePixels();
        var sourceScaleX = (float)_width / info.Width;
        var sourceScaleY = (float)_height / info.Height;
        unsafe
        {
            var destination = (byte*)resized.GetPixels();
            for (var y = 0; y < info.Height; y++)
            {
                var sourceY = ((y + 0.5f) * sourceScaleY) - 0.5f;
                var destinationRow = destination + y * resized.RowBytes;
                for (var x = 0; x < info.Width; x++)
                {
                    var sourceX = ((x + 0.5f) * sourceScaleX) - 0.5f;
                    var color = SampleResizePixel(
                        source,
                        _width,
                        _height,
                        sourceX,
                        sourceY,
                        sampling);
                    WriteResizePixel(
                        destinationRow + x * info.BytesPerPixel,
                        info,
                        color,
                        AlphaType == SKAlphaType.Premul);
                }
            }
        }

        return resized;
    }

    private static bool SupportsResizeColorType(SKColorType colorType) =>
        colorType is SKColorType.Rgb565
            or SKColorType.Rgba8888
            or SKColorType.Rgb888x
            or SKColorType.Bgra8888;

    private unsafe Vector4[] CopyResizeSourcePixels()
    {
        var result = new Vector4[checked(_width * _height)];
        var source = (byte*)_pixels;
        for (var y = 0; y < _height; y++)
        {
            var sourceRow = source + y * RowBytes;
            for (var x = 0; x < _width; x++)
            {
                var pixel = sourceRow + x * _info.BytesPerPixel;
                result[y * _width + x] = ReadResizeSourcePixel(pixel);
            }
        }

        return result;
    }

    private unsafe void ResizeNearestPixels(SKBitmap resized, SKImageInfo info)
    {
        var source = (byte*)_pixels;
        var destination = (byte*)resized.GetPixels();
        var sourceScaleX = (float)_width / info.Width;
        var sourceScaleY = (float)_height / info.Height;
        var canCopyStoredPixel = ColorType == info.ColorType
            && AlphaType == info.AlphaType
            && (AlphaType != SKAlphaType.Opaque || ColorType == SKColorType.Rgb565);
        for (var y = 0; y < info.Height; y++)
        {
            var sourceY = Math.Clamp(
                (int)MathF.Ceiling(((y + 0.5f) * sourceScaleY) - 1f),
                0,
                _height - 1);
            var sourceRow = source + sourceY * RowBytes;
            var destinationRow = destination + y * resized.RowBytes;
            for (var x = 0; x < info.Width; x++)
            {
                var sourceX = Math.Clamp(
                    (int)MathF.Ceiling(((x + 0.5f) * sourceScaleX) - 1f),
                    0,
                    _width - 1);
                var sourcePixel = sourceRow + sourceX * _info.BytesPerPixel;
                var destinationPixel = destinationRow + x * info.BytesPerPixel;
                if (canCopyStoredPixel)
                {
                    System.Buffer.MemoryCopy(
                        sourcePixel,
                        destinationPixel,
                        info.BytesPerPixel,
                        info.BytesPerPixel);
                    continue;
                }

                var color = ReadResizeSourcePixel(sourcePixel);
                WriteResizePixel(
                    destinationPixel,
                    info,
                    color,
                    AlphaType == SKAlphaType.Premul);
            }
        }
    }

    private unsafe Vector4 ReadResizeSourcePixel(byte* pixel)
    {
        if (ColorType == SKColorType.Rgb565)
        {
            var packed = (ushort)(pixel[0] | (pixel[1] << 8));
            return new Vector4(
                ((packed >> 11) & 0x1f) / 31f,
                ((packed >> 5) & 0x3f) / 63f,
                (packed & 0x1f) / 31f,
                1f);
        }

        var alpha = AlphaType == SKAlphaType.Opaque || ColorType == SKColorType.Rgb888x
            ? 1f
            : pixel[3] / 255f;
        return ColorType == SKColorType.Bgra8888
            ? new Vector4(pixel[2] / 255f, pixel[1] / 255f, pixel[0] / 255f, alpha)
            : new Vector4(pixel[0] / 255f, pixel[1] / 255f, pixel[2] / 255f, alpha);
    }

    private static Vector4 SampleResizePixel(
        Vector4[] source,
        int width,
        int height,
        float x,
        float y,
        SKSamplingOptions sampling)
    {
        if (sampling.UseCubic)
        {
            return SampleResizeCubic(source, width, height, x, y, sampling.CubicResampler);
        }

        if (sampling.FilterMode == SKFilterMode.Linear)
        {
            var x0 = (int)MathF.Floor(x);
            var y0 = (int)MathF.Floor(y);
            var fractionX = x - x0;
            var fractionY = y - y0;
            var top = Vector4.Lerp(
                GetResizePixel(source, width, height, x0, y0),
                GetResizePixel(source, width, height, x0 + 1, y0),
                fractionX);
            var bottom = Vector4.Lerp(
                GetResizePixel(source, width, height, x0, y0 + 1),
                GetResizePixel(source, width, height, x0 + 1, y0 + 1),
                fractionX);
            return Vector4.Lerp(top, bottom, fractionY);
        }

        var nearestX = (int)MathF.Ceiling(x - 0.5f);
        var nearestY = (int)MathF.Ceiling(y - 0.5f);
        return GetResizePixel(source, width, height, nearestX, nearestY);
    }

    private static Vector4 SampleResizeCubic(
        Vector4[] source,
        int width,
        int height,
        float x,
        float y,
        SKCubicResampler resampler)
    {
        var baseX = (int)MathF.Floor(x);
        var baseY = (int)MathF.Floor(y);
        var fractionX = x - baseX;
        var fractionY = y - baseY;
        var color = Vector4.Zero;
        var totalWeight = 0f;
        for (var tapY = -1; tapY <= 2; tapY++)
        {
            var weightY = ResizeCubicWeight(fractionY - tapY, resampler.B, resampler.C);
            for (var tapX = -1; tapX <= 2; tapX++)
            {
                var weight = ResizeCubicWeight(fractionX - tapX, resampler.B, resampler.C) * weightY;
                color += GetResizePixel(source, width, height, baseX + tapX, baseY + tapY) * weight;
                totalWeight += weight;
            }
        }

        return MathF.Abs(totalWeight) > 0.0001f
            ? color / totalWeight
            : GetResizePixel(source, width, height, baseX, baseY);
    }

    private static float ResizeCubicWeight(float value, float b, float c)
    {
        var x = MathF.Abs(value);
        var x2 = x * x;
        var x3 = x2 * x;
        if (b == 0f && c == 0.5f)
        {
            const float a = -0.5f;
            if (x <= 1f)
            {
                return ((a + 2f) * x3) - ((a + 3f) * x2) + 1f;
            }

            return x < 2f
                ? (a * x3) - (5f * a * x2) + (8f * a * x) - (4f * a)
                : 0f;
        }

        if (x <= 1f)
        {
            return ((12f - 9f * b - 6f * c) * x3
                + (-18f + 12f * b + 6f * c) * x2
                + (6f - 2f * b)) / 6f;
        }

        return x < 2f
            ? ((-b - 6f * c) * x3
                + (6f * b + 30f * c) * x2
                + (-12f * b - 48f * c) * x
                + (8f * b + 24f * c)) / 6f
            : 0f;
    }

    private static Vector4 GetResizePixel(Vector4[] source, int width, int height, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        return source[y * width + x];
    }

    private static unsafe void WriteResizePixel(
        byte* destination,
        SKImageInfo info,
        Vector4 color,
        bool sourceIsPremultiplied)
    {
        var alpha = Math.Clamp(color.W, 0f, 1f);
        var targetHasAlpha = (info.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888)
            && info.AlphaType != SKAlphaType.Opaque;
        var targetIsPremultiplied = targetHasAlpha && info.AlphaType == SKAlphaType.Premul;
        var rgb = new Vector3(color.X, color.Y, color.Z);
        if (sourceIsPremultiplied && !targetIsPremultiplied)
        {
            rgb = alpha > 0f ? rgb / alpha : Vector3.Zero;
        }
        else if (!sourceIsPremultiplied && targetIsPremultiplied)
        {
            rgb *= alpha;
        }

        var red = ResizeChannelToByte(rgb.X);
        var green = ResizeChannelToByte(rgb.Y);
        var blue = ResizeChannelToByte(rgb.Z);
        var alphaByte = targetHasAlpha ? ResizeChannelToByte(alpha) : (byte)255;
        if (info.ColorType == SKColorType.Rgb565)
        {
            var packed = PackRgb565(red, green, blue);
            destination[0] = (byte)packed;
            destination[1] = (byte)(packed >> 8);
            return;
        }

        if (info.ColorType == SKColorType.Bgra8888)
        {
            destination[0] = blue;
            destination[1] = green;
            destination[2] = red;
        }
        else
        {
            destination[0] = red;
            destination[1] = green;
            destination[2] = blue;
        }

        destination[3] = alphaByte;
    }

    private static byte ResizeChannelToByte(float value) =>
        (byte)Math.Clamp(MathF.Floor(Math.Clamp(value, 0f, 1f) * 255f + 0.5f), 0f, 255f);

    public SKData Encode(SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(this);
        return image.Encode(format, quality);
    }

    internal byte[] CopyRgba8888Rows()
    {
        FlushAttachedCanvas();

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

                    if (ColorType == SKColorType.Rgb565)
                    {
                        for (int x = 0; x < _width; x++)
                        {
                            int srcIdx = x * 2;
                            int dstIdx = x * 4;
                            ushort value = (ushort)(srcRow[srcIdx] | (srcRow[srcIdx + 1] << 8));
                            dstRow[dstIdx] = (byte)(((value >> 11) & 0x1f) * 255 / 31);
                            dstRow[dstIdx + 1] = (byte)(((value >> 5) & 0x3f) * 255 / 63);
                            dstRow[dstIdx + 2] = (byte)((value & 0x1f) * 255 / 31);
                            dstRow[dstIdx + 3] = 255;
                        }
                    }
                    else if (ColorType == SKColorType.Bgra8888)
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

    private static unsafe void CopyRows(
        IntPtr source,
        int sourceRowBytes,
        IntPtr destination,
        int destinationRowBytes,
        int copyRowBytes,
        int height)
    {
        byte* src = (byte*)source;
        byte* dst = (byte*)destination;
        for (int y = 0; y < height; y++)
        {
            System.Buffer.MemoryCopy(
                src + y * sourceRowBytes,
                dst + y * destinationRowBytes,
                destinationRowBytes,
                copyRowBytes);
        }
    }

    private static ushort PackRgb565(byte red, byte green, byte blue)
    {
        return (ushort)(
            ((red * 31 + 127) / 255 << 11) |
            ((green * 63 + 127) / 255 << 5) |
            ((blue * 31 + 127) / 255));
    }

    private static byte Premultiply(byte color, byte alpha)
    {
        return (byte)((color * alpha + 127) / 255);
    }

    private static byte Unpremultiply(byte color, byte alpha)
    {
        return alpha == 0
            ? (byte)0
            : (byte)Math.Min(255, (color * 255 + alpha / 2) / alpha);
    }

    public void Dispose()
    {
        ReleasePixels();
    }

    private void ReleasePixels()
    {
        if (_pixels == IntPtr.Zero)
        {
            return;
        }

        if (_ownsPixels)
        {
            Marshal.FreeHGlobal(_pixels);
        }
        else if (_releaseDelegate != null && _releaseContext != null)
        {
            _releaseDelegate(_pixels, _releaseContext);
        }

        _pixels = IntPtr.Zero;
        _ownsPixels = false;
        _releaseDelegate = null;
        _releaseContext = null;
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
