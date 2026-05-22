using System;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace ProGPU.Backend;

public unsafe class GpuTexture : IDisposable
{
    private readonly WgpuContext _context;
    private string _label;

    public Texture* TexturePtr { get; private set; }
    public TextureView* ViewPtr { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureFormat Format { get; private set; }
    public TextureUsage Usage { get; private set; }

    private bool _isDisposed;

    public GpuTexture(WgpuContext context, uint width, uint height, TextureFormat format, TextureUsage usage, string label = "GpuTexture")
    {
        _context = context;
        Width = width > 0 ? width : 1;
        Height = height > 0 ? height : 1;
        Format = format;
        Usage = usage;
        _label = label;

        Allocate();
    }

    private void Allocate()
    {
        var labelPtr = SilkMarshal.StringToPtr(_label);
        
        var desc = new TextureDescriptor
        {
            Label = (byte*)labelPtr,
            Usage = Usage,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = Width, Height = Height, DepthOrArrayLayers = 1 },
            Format = Format,
            MipLevelCount = 1,
            SampleCount = 1,
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
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
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

    public void WritePixels<T>(ReadOnlySpan<T> pixels) where T : unmanaged
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));

        uint bytesPerPixel = Format switch
        {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb => 4,
            TextureFormat.R8Unorm => 1,
            _ => 4 // Default standard
        };

        uint expectedSize = Width * Height * bytesPerPixel;
        uint passedSize = (uint)(pixels.Length * sizeof(T));
        if (passedSize < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small ({passedSize} bytes, expected {expectedSize} bytes).");
        }

        var destination = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All
        };

        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = Width * bytesPerPixel,
            RowsPerImage = Height
        };

        var extent = new Extent3D
        {
            Width = Width,
            Height = Height,
            DepthOrArrayLayers = 1
        };

        fixed (T* ptr = pixels)
        {
            _context.Wgpu.QueueWriteTexture(_context.Queue, &destination, ptr, passedSize, &layout, &extent);
        }
    }

    public void WritePixelsSubRect<T>(ReadOnlySpan<T> pixels, uint x, uint y, uint subWidth, uint subHeight) where T : unmanaged
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(GpuTexture));

        uint bytesPerPixel = Format switch
        {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb => 4,
            TextureFormat.R8Unorm => 1,
            _ => 4
        };

        uint expectedSize = subWidth * subHeight * bytesPerPixel;
        uint passedSize = (uint)(pixels.Length * sizeof(T));
        if (passedSize < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small for sub-rect ({passedSize} bytes, expected {expectedSize} bytes).");
        }

        var destination = new ImageCopyTexture
        {
            Texture = TexturePtr,
            MipLevel = 0,
            Origin = new Origin3D { X = x, Y = y, Z = 0 },
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
    }

    private void ReleaseResources()
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

    public void Dispose()
    {
        if (_isDisposed) return;

        ReleaseResources();

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~GpuTexture()
    {
        Dispose();
    }
}
