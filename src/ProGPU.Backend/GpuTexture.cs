using System;
using System.Threading;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace ProGPU.Backend;

public enum GpuTextureAlphaMode
{
    Straight = 0,
    Premultiplied = 1
}

public enum GpuTextureDimension
{
    Dimension2D = 0,
    Dimension3D = 1
}

public unsafe class GpuTexture : IDisposable
{
    private static long s_idCounter = 0;
    public static event Action<ulong>? OnDisposedWithId;

    private readonly WgpuContext _context;
    private string _label;

    public ulong Id { get; }
    public WgpuContext Context => _context;
    public uint Generation { get; private set; }

    public Texture* TexturePtr { get; private set; }
    public TextureView* ViewPtr { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint DepthOrArrayLayers { get; private set; } = 1;
    public uint MipLevelCount { get; private set; } = 1;
    public GpuTextureDimension Dimension { get; private set; } = GpuTextureDimension.Dimension2D;
    public TextureFormat Format { get; private set; }
    public TextureUsage Usage { get; private set; }
    public uint SampleCount { get; private set; } = 1;
    public GpuTextureAlphaMode AlphaMode { get; set; }

    private bool _isDisposed;
    public bool IsDisposed => _isDisposed;

    public GpuTexture(
        WgpuContext context,
        uint width,
        uint height,
        TextureFormat format,
        TextureUsage usage,
        string label = "GpuTexture",
        uint sampleCount = 1,
        GpuTextureAlphaMode alphaMode = GpuTextureAlphaMode.Straight,
        uint depthOrArrayLayers = 1,
        uint mipLevelCount = 1,
        GpuTextureDimension dimension = GpuTextureDimension.Dimension2D)
    {
        Id = (ulong)Interlocked.Increment(ref s_idCounter);
        _context = context;
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        DepthOrArrayLayers = depthOrArrayLayers > 0 ? depthOrArrayLayers : 1;
        MipLevelCount = mipLevelCount > 0 ? mipLevelCount : 1;
        Dimension = dimension;
        Format = format;
        Usage = usage;
        _label = label;
        SampleCount = sampleCount;
        AlphaMode = alphaMode;

        if (SampleCount != 1 && MipLevelCount != 1)
        {
            throw new NotSupportedException("Multisampled GPU textures cannot have mip levels.");
        }

        Allocate();
    }

    private void Allocate()
    {
        Generation++;
        var labelPtr = SilkMarshal.StringToPtr(_label);
        
        var desc = new TextureDescriptor
        {
            Label = (byte*)labelPtr,
            Usage = Usage,
            Dimension = Dimension == GpuTextureDimension.Dimension3D
                ? TextureDimension.Dimension3D
                : TextureDimension.Dimension2D,
            Size = new Extent3D { Width = Width, Height = Height, DepthOrArrayLayers = DepthOrArrayLayers },
            Format = Format,
            MipLevelCount = MipLevelCount,
            SampleCount = SampleCount,
            ViewFormatCount = 0,
            ViewFormats = null
        };

        TexturePtr = _context.Wgpu.DeviceCreateTexture(_context.Device, &desc);
        SilkMarshal.Free(labelPtr);

        if (TexturePtr == null)
        {
            throw new InvalidOperationException($"Failed to allocate GPU Texture {Width}x{Height}.");
        }

        // Automatically create a default texture view
        var viewDesc = new TextureViewDescriptor
        {
            Format = Format,
            Dimension = Dimension == GpuTextureDimension.Dimension3D
                ? TextureViewDimension.Dimension3D
                : DepthOrArrayLayers > 1
                ? TextureViewDimension.Dimension2DArray
                : TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = MipLevelCount,
            BaseArrayLayer = 0,
            ArrayLayerCount = DepthOrArrayLayers,
            Aspect = TextureAspect.All
        };

        ViewPtr = _context.Wgpu.TextureCreateView(TexturePtr, &viewDesc);
        if (ViewPtr == null)
        {
            throw new InvalidOperationException($"Failed to create TextureView for GPU Texture {Width}x{Height}.");
        }
    }

    public void Resize(uint width, uint height)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        if (Width == width && Height == height) return;

        // Release old texture and view
        ReleaseResources();

        // Reallocate with new dimensions
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        Allocate();
    }

    public void WritePixels<T>(ReadOnlySpan<T> pixels, uint mipLevel = 0) where T : unmanaged
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        ValidateMipLevel(mipLevel);

        uint bytesPerPixel = GetBytesPerPixel(Format);
        uint mipWidth = GetMipDimension(Width, mipLevel);
        uint mipHeight = GetMipDimension(Height, mipLevel);
        uint mipDepthOrLayers = GetMipDepthOrArrayLayers(mipLevel);

        uint expectedSize = mipWidth * mipHeight * mipDepthOrLayers * bytesPerPixel;
        uint passedSize = (uint)(pixels.Length * sizeof(T));
        if (passedSize < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small ({passedSize} bytes, expected {expectedSize} bytes).");
        }

        var destination = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = mipLevel,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All
        };

        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = mipWidth * bytesPerPixel,
            RowsPerImage = mipHeight
        };

        var extent = new Extent3D
        {
            Width = mipWidth,
            Height = mipHeight,
            DepthOrArrayLayers = mipDepthOrLayers
        };

        fixed (T* ptr = pixels)
        {
            _context.Wgpu.QueueWriteTexture(_context.Queue, &destination, ptr, passedSize, &layout, &extent);
        }

        Generation++;
    }

    public void WritePbgra32(Pbgra32PixelBuffer pixels)
    {
        if (Width > int.MaxValue
            || Height > int.MaxValue
            || pixels.Width != (int)Width
            || pixels.Height != (int)Height)
        {
            throw new ArgumentException("PBgra32 pixel buffer dimensions must match the texture dimensions.", nameof(pixels));
        }

        WritePbgra32SubRect(pixels, 0, 0);
    }

    public void WritePbgra32SubRect(Pbgra32PixelBuffer pixels, uint x, uint y)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        if (!pixels.IsValid)
        {
            throw new ArgumentException("PBgra32 pixel buffer is not valid.", nameof(pixels));
        }

        EnsurePbgra32CompatibleFormat();

        var subWidth = (uint)pixels.Width;
        var subHeight = (uint)pixels.Height;
        if (x > Width
            || y > Height
            || subWidth > Width - x
            || subHeight > Height - y)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), "PBgra32 pixel buffer does not fit inside the texture bounds.");
        }

        WritePixelsSubRect(pixels.CopyCompactRows(), x, y, subWidth, subHeight);
        AlphaMode = GpuTextureAlphaMode.Premultiplied;
    }

    public void WritePixelsSubRect<T>(
        ReadOnlySpan<T> pixels,
        uint x,
        uint y,
        uint subWidth,
        uint subHeight,
        uint arrayLayer = 0,
        uint mipLevel = 0)
        where T : unmanaged
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        ValidateMipLevel(mipLevel);
        if (subWidth == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subWidth), "Pixel sub-rect width must be greater than zero.");
        }

        if (subHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subHeight), "Pixel sub-rect height must be greater than zero.");
        }

        var mipWidth = GetMipDimension(Width, mipLevel);
        var mipHeight = GetMipDimension(Height, mipLevel);
        if (x > mipWidth
            || y > mipHeight
            || subWidth > mipWidth - x
            || subHeight > mipHeight - y)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), "Pixel sub-rect does not fit inside the texture bounds.");
        }

        if (arrayLayer >= GetMipDepthOrArrayLayers(mipLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(arrayLayer), "Pixel sub-rect array layer or depth slice is outside the texture.");
        }

        uint bytesPerPixel = GetBytesPerPixel(Format);

        uint expectedSize = subWidth * subHeight * bytesPerPixel;
        uint passedSize = (uint)(pixels.Length * sizeof(T));
        if (passedSize < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small for sub-rect ({passedSize} bytes, expected {expectedSize} bytes).");
        }

        var destination = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = mipLevel,
            Origin = new Origin3D { X = x, Y = y, Z = arrayLayer },
            Aspect = TextureAspect.All
        };

        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = subWidth * bytesPerPixel,
            RowsPerImage = subHeight
        };

        var extent = new Extent3D
        {
            Width = subWidth,
            Height = subHeight,
            DepthOrArrayLayers = 1
        };

        fixed (T* ptr = pixels)
        {
            _context.Wgpu.QueueWriteTexture(_context.Queue, &destination, ptr, passedSize, &layout, &extent);
        }

        Generation++;
    }

    public void WritePixelsVolume<T>(
        ReadOnlySpan<T> pixels,
        uint x,
        uint y,
        uint z,
        uint subWidth,
        uint subHeight,
        uint subDepth,
        uint mipLevel = 0)
        where T : unmanaged
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        ValidateMipLevel(mipLevel);
        if (Dimension != GpuTextureDimension.Dimension3D)
        {
            throw new InvalidOperationException("Volume uploads require a 3D GPU texture.");
        }

        if (subWidth == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subWidth), "Pixel volume width must be greater than zero.");
        }

        if (subHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subHeight), "Pixel volume height must be greater than zero.");
        }

        if (subDepth == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subDepth), "Pixel volume depth must be greater than zero.");
        }

        var mipWidth = GetMipDimension(Width, mipLevel);
        var mipHeight = GetMipDimension(Height, mipLevel);
        var mipDepth = GetMipDepthOrArrayLayers(mipLevel);
        if (x > mipWidth
            || y > mipHeight
            || z > mipDepth
            || subWidth > mipWidth - x
            || subHeight > mipHeight - y
            || subDepth > mipDepth - z)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), "Pixel volume does not fit inside the texture bounds.");
        }

        uint bytesPerPixel = GetBytesPerPixel(Format);
        uint expectedSize = subWidth * subHeight * subDepth * bytesPerPixel;
        uint passedSize = (uint)(pixels.Length * sizeof(T));
        if (passedSize < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small for volume ({passedSize} bytes, expected {expectedSize} bytes).");
        }

        var destination = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = mipLevel,
            Origin = new Origin3D { X = x, Y = y, Z = z },
            Aspect = TextureAspect.All
        };

        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = subWidth * bytesPerPixel,
            RowsPerImage = subHeight
        };

        var extent = new Extent3D
        {
            Width = subWidth,
            Height = subHeight,
            DepthOrArrayLayers = subDepth
        };

        fixed (T* ptr = pixels)
        {
            _context.Wgpu.QueueWriteTexture(_context.Queue, &destination, ptr, passedSize, &layout, &extent);
        }

        Generation++;
    }

    public void MarkContentsDirty()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));

        Generation++;
    }

    public void CopyFrom(GpuTexture source)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        ArgumentNullException.ThrowIfNull(source);
        if (source._isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        if (source.Context != _context)
        {
            throw new ArgumentException("Source texture must belong to the same WebGPU context.", nameof(source));
        }

        if (source.Width != Width
            || source.Height != Height
            || source.DepthOrArrayLayers != DepthOrArrayLayers
            || source.MipLevelCount != MipLevelCount
            || source.Dimension != Dimension
            || source.Format != Format
            || source.SampleCount != SampleCount)
        {
            throw new ArgumentException("Source texture dimensions, format, and sample count must match the destination texture.", nameof(source));
        }

        if (!source.Usage.HasFlag(TextureUsage.CopySrc))
        {
            throw new InvalidOperationException("Source texture was not created with CopySrc usage.");
        }

        if (!Usage.HasFlag(TextureUsage.CopyDst))
        {
            throw new InvalidOperationException("Destination texture was not created with CopyDst usage.");
        }

        var encoderDesc = new CommandEncoderDescriptor();
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);
        if (encoder == null)
        {
            throw new InvalidOperationException("Failed to create command encoder for texture copy.");
        }

        for (uint mipLevel = 0; mipLevel < MipLevelCount; mipLevel++)
        {
            var copySource = new ImageCopyTexture
            {
                Texture = source.TexturePtr,
                MipLevel = mipLevel,
                Origin = new Origin3D(),
                Aspect = TextureAspect.All
            };

            var copyDestination = new ImageCopyTexture
            {
                Texture = TexturePtr,
                MipLevel = mipLevel,
                Origin = new Origin3D(),
                Aspect = TextureAspect.All
            };

            var copySize = new Extent3D
            {
                Width = GetMipDimension(Width, mipLevel),
                Height = GetMipDimension(Height, mipLevel),
                DepthOrArrayLayers = GetMipDepthOrArrayLayers(mipLevel)
            };

            _context.Wgpu.CommandEncoderCopyTextureToTexture(encoder, &copySource, &copyDestination, &copySize);
        }

        var commandBufferDesc = new CommandBufferDescriptor();
        var commandBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &commandBufferDesc);
        _context.Wgpu.QueueSubmit(_context.Queue, 1, &commandBuffer);
        _context.Wgpu.CommandBufferRelease(commandBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);

        AlphaMode = source.AlphaMode;
        Generation++;
    }

    private void EnsurePbgra32CompatibleFormat()
    {
        if (Format is not (TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb))
        {
            throw new InvalidOperationException($"PBgra32 uploads require a BGRA8 texture format. Actual format: {Format}.");
        }
    }

    private static uint GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm or
            TextureFormat.R8Snorm or
            TextureFormat.R8Uint or
            TextureFormat.R8Sint => 1,

            TextureFormat.R16Uint or
            TextureFormat.R16Sint or
            TextureFormat.R16float or
            TextureFormat.Depth16Unorm => 2,

            TextureFormat.RG8Unorm or
            TextureFormat.RG8Snorm or
            TextureFormat.RG8Uint or
            TextureFormat.RG8Sint => 2,

            TextureFormat.R32Uint or
            TextureFormat.R32Sint or
            TextureFormat.R32float or
            TextureFormat.RG16Uint or
            TextureFormat.RG16Sint or
            TextureFormat.RG16float or
            TextureFormat.Rgba8Unorm or
            TextureFormat.Rgba8UnormSrgb or
            TextureFormat.Rgba8Snorm or
            TextureFormat.Rgba8Uint or
            TextureFormat.Rgba8Sint or
            TextureFormat.Bgra8Unorm or
            TextureFormat.Bgra8UnormSrgb or
            TextureFormat.Rgb10A2Unorm or
            TextureFormat.Rgb10A2Uint or
            TextureFormat.Depth24Plus or
            TextureFormat.Depth24PlusStencil8 or
            TextureFormat.Depth32float => 4,

            TextureFormat.RG32Uint or
            TextureFormat.RG32Sint or
            TextureFormat.RG32float or
            TextureFormat.Rgba16Uint or
            TextureFormat.Rgba16Sint or
            TextureFormat.Rgba16float => 8,

            TextureFormat.Rgba32Uint or
            TextureFormat.Rgba32Sint or
            TextureFormat.Rgba32float => 16,

            _ => throw new NotSupportedException($"Texture format {format} does not have a supported compact pixel size.")
        };
    }

    private void ValidateMipLevel(uint mipLevel)
    {
        if (mipLevel >= MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), "Texture mip level is outside the texture mip chain.");
        }
    }

    private static uint GetMipDimension(uint dimension, uint mipLevel)
    {
        if (mipLevel >= 31)
        {
            return 1;
        }

        var shifted = dimension >> checked((int)mipLevel);
        return Math.Max(1u, shifted);
    }

    public static void CleanupPendingResources(WgpuContext context)
    {
        context.CleanupPendingResources();
    }

    [System.Runtime.InteropServices.DllImport("wgpu_native", EntryPoint = "wgpuDevicePoll")]
    private static extern unsafe bool wgpuDevicePoll(Device* device, bool wait, void* wrappedSubmissionIndex);

    public byte[] ReadPixels(uint mipLevel = 0)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));
        if (!Usage.HasFlag(TextureUsage.CopySrc))
        {
            throw new InvalidOperationException("Texture was not created with CopySrc usage.");
        }

        ValidateMipLevel(mipLevel);
        var wgpu = _context.Wgpu;
        var device = _context.Device;
        var queue = _context.Queue;

        uint bytesPerPixel = GetBytesPerPixel(Format);
        uint mipWidth = GetMipDimension(Width, mipLevel);
        uint mipHeight = GetMipDimension(Height, mipLevel);
        uint mipDepthOrLayers = GetMipDepthOrArrayLayers(mipLevel);

        // Align row pitch to 256 bytes per WebGPU requirements
        uint bytesPerRow = mipWidth * bytesPerPixel;
        uint alignedBytesPerRow = (bytesPerRow + 255) & ~255u;
        uint bufferSize = alignedBytesPerRow * mipHeight * mipDepthOrLayers;

        var bufferDesc = new BufferDescriptor
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = bufferSize,
            MappedAtCreation = false
        };
        var readbackBuffer = wgpu.DeviceCreateBuffer(device, &bufferDesc);
        if (readbackBuffer == null)
        {
            throw new InvalidOperationException("Failed to create readback buffer for texture ReadPixels.");
        }

        // 1. Create a command encoder for the copy operation
        var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Texture Readback Encoder") };
        var encoder = wgpu.DeviceCreateCommandEncoder(device, &encoderDesc);
        SilkMarshal.Free((nint)encoderDesc.Label);

        // 2. Define source and destination copy descriptors
        var source = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = mipLevel,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All
        };

        var destination = new ImageCopyBuffer
        {
            Buffer = readbackBuffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = alignedBytesPerRow,
                RowsPerImage = mipHeight
            }
        };

        var copySize = new Extent3D
        {
            Width = mipWidth,
            Height = mipHeight,
            DepthOrArrayLayers = mipDepthOrLayers
        };

        wgpu.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &copySize);

        // 3. Submit copy command to Queue
        var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Texture Readback Command Buffer") };
        var cmdBuffer = wgpu.CommandEncoderFinish(encoder, &cmdDesc);
        SilkMarshal.Free((nint)cmdDesc.Label);

        wgpu.QueueSubmit(queue, 1, &cmdBuffer);

        wgpu.CommandBufferRelease(cmdBuffer);
        wgpu.CommandEncoderRelease(encoder);

        // 4. Map the buffer asynchronously to copy memory to CPU
        var mapSignal = new System.Threading.ManualResetEventSlim(false);
        BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.ValidationError;

        var onMapped = PfnBufferMapCallback.From((status, userData) =>
        {
            mapStatus = status;
            mapSignal.Set();
        });

        wgpu.BufferMapAsync(readbackBuffer, MapMode.Read, 0, (nuint)bufferSize, onMapped, null);

        // Poll the device to process events and fire the callback synchronously
        var swTimeout = System.Diagnostics.Stopwatch.StartNew();
        while (!mapSignal.IsSet)
        {
            wgpuDevicePoll(_context.Device, false, null);
            System.Threading.Thread.Sleep(1);
            if (swTimeout.ElapsedMilliseconds > 5000)
            {
                wgpu.BufferDestroy(readbackBuffer);
                wgpu.BufferRelease(readbackBuffer);
                throw new TimeoutException("WebGPU BufferMapAsync timed out after 5 seconds during texture readback.");
            }
        }

        if (mapStatus != BufferMapAsyncStatus.Success)
        {
            wgpu.BufferDestroy(readbackBuffer);
            wgpu.BufferRelease(readbackBuffer);
            throw new InvalidOperationException($"Failed to map readback buffer. WebGPU Status: {mapStatus}");
        }

        // 5. Read out the mapped pixels, stripping the row-alignment padding
        byte[] unpaddedPixels = new byte[mipWidth * mipHeight * mipDepthOrLayers * bytesPerPixel];
        void* mappedPtr = wgpu.BufferGetConstMappedRange(readbackBuffer, 0, (nuint)bufferSize);
        if (mappedPtr != null)
        {
            byte* srcBytes = (byte*)mappedPtr;
            for (uint layer = 0; layer < mipDepthOrLayers; layer++)
            {
                for (uint y = 0; y < mipHeight; y++)
                {
                    long srcOffset = (layer * mipHeight * alignedBytesPerRow) + (y * alignedBytesPerRow);
                    long dstOffset = (layer * mipHeight * bytesPerRow) + (y * bytesPerRow);
                    System.Runtime.InteropServices.Marshal.Copy((nint)(srcBytes + srcOffset), unpaddedPixels, (int)dstOffset, (int)bytesPerRow);
                }
            }
        }

        // 6. Always unmap the buffer and clean up
        wgpu.BufferUnmap(readbackBuffer);
        wgpu.BufferDestroy(readbackBuffer);
        wgpu.BufferRelease(readbackBuffer);

        return unpaddedPixels;
    }

    private uint GetMipDepthOrArrayLayers(uint mipLevel)
    {
        return Dimension == GpuTextureDimension.Dimension3D
            ? GetMipDimension(DepthOrArrayLayers, mipLevel)
            : DepthOrArrayLayers;
    }

    private void ReleaseResources(bool immediate = false)
    {
        OnDisposedWithId?.Invoke(Id);

        lock (_context.RenderLock)
        {
            if (_context.IsDisposed)
            {
                ViewPtr = null;
                TexturePtr = null;
                return;
            }

            if (immediate)
            {
                if (ViewPtr != null)
                {
                    _context.Wgpu.TextureViewRelease(ViewPtr);
                    ViewPtr = null;
                }

                if (TexturePtr != null)
                {
                    _context.Wgpu.TextureDestroy(TexturePtr);
                    _context.Wgpu.TextureRelease(TexturePtr);
                    TexturePtr = null;
                }
            }
            else
            {
                if (ViewPtr != null)
                {
                    _context.QueueTextureViewDisposal((IntPtr)ViewPtr);
                    ViewPtr = null;
                }

                if (TexturePtr != null)
                {
                    _context.QueueTextureDisposal((IntPtr)TexturePtr);
                    TexturePtr = null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        if (TexturePtr != null || ViewPtr != null)
        {
            ReleaseResources(Environment.HasShutdownStarted || _context.IsDisposed);
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~GpuTexture()
    {
        if (TexturePtr != null || ViewPtr != null)
        {
            try
            {
                if (ViewPtr != null)
                {
                    _context.QueueTextureViewDisposal((IntPtr)ViewPtr);
                }
                if (TexturePtr != null)
                {
                    _context.QueueTextureDisposal((IntPtr)TexturePtr);
                }
            }
            catch {}
        }
    }
}
