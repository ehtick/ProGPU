using System;
using System.Diagnostics;
using System.Threading;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ProGPU.Backend;

public unsafe sealed class GpuTextureReadbackBuffer : IDisposable
{
    public const int DefaultMapTimeoutMilliseconds = 5000;

    private readonly WgpuContext _context;
    private readonly PfnBufferMapCallback _mapCallback;
    private WgpuBuffer* _buffer;
    private ManualResetEventSlim? _mapSignal;
    private bool _isMapActive;

    public GpuTextureReadbackBuffer(WgpuContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mapCallback = PfnBufferMapCallback.From(OnBufferMapped);
    }

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public uint BytesPerPixel { get; private set; }

    public uint BytesPerRow { get; private set; }

    public uint BufferSize { get; private set; }

    public BufferMapAsyncStatus LastMapStatus { get; private set; } = BufferMapAsyncStatus.ValidationError;

    public bool LastMapTimedOut { get; private set; }

    public static uint AlignBytesPerRow(uint width, uint bytesPerPixel)
    {
        if (bytesPerPixel == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), "Bytes per pixel must be greater than zero.");
        }

        ulong unaligned = (ulong)width * bytesPerPixel;
        ulong aligned = (unaligned + 255UL) & ~255UL;
        if (aligned > uint.MaxValue)
        {
            throw new OverflowException("The aligned WebGPU readback row pitch exceeds UInt32.MaxValue.");
        }

        return (uint)aligned;
    }

    public void EnsureCapacity(uint width, uint height, uint bytesPerPixel = 4)
    {
        ThrowIfContextDisposed();

        width = Math.Max(1u, width);
        height = Math.Max(1u, height);
        uint bytesPerRow = AlignBytesPerRow(width, bytesPerPixel);
        uint bufferSize = checked(bytesPerRow * height);

        Width = width;
        Height = height;
        BytesPerPixel = bytesPerPixel;
        BytesPerRow = bytesPerRow;

        if (_buffer != null && BufferSize >= bufferSize)
        {
            BufferSize = bufferSize;
            return;
        }

        QueueBufferDisposal();

        var bufferDesc = new BufferDescriptor
        {
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
            Size = bufferSize,
            MappedAtCreation = false
        };

        _buffer = _context.Wgpu.DeviceCreateBuffer(_context.Device, &bufferDesc);
        if (_buffer == null)
        {
            BufferSize = 0;
            throw new InvalidOperationException("Failed to create a WebGPU staging buffer for texture readback.");
        }

        BufferSize = bufferSize;
    }

    public bool TryReadTextureRows(
        GpuTexture texture,
        uint width,
        uint height,
        void* destination,
        uint destinationBytesPerRow,
        uint bytesPerPixel = 4,
        int timeoutMilliseconds = DefaultMapTimeoutMilliseconds)
    {
        if (texture == null)
        {
            throw new ArgumentNullException(nameof(texture));
        }

        if (!ReferenceEquals(texture.Context, _context))
        {
            throw new InvalidOperationException("Texture readback requires the source texture and readback buffer to use the same WebGPU context.");
        }

        if (texture.IsDisposed || _context.IsDisposed || destination == null)
        {
            return false;
        }

        if (!texture.Usage.HasFlag(TextureUsage.CopySrc))
        {
            throw new InvalidOperationException("Texture readback requires a texture created with CopySrc usage.");
        }

        width = width == 0 ? texture.Width : width;
        height = height == 0 ? texture.Height : height;

        uint rowBytes = checked(width * bytesPerPixel);
        if (rowBytes > destinationBytesPerRow)
        {
            return false;
        }

        EnsureCapacity(width, height, bytesPerPixel);
        if (_buffer == null)
        {
            return false;
        }

        CopyTextureToBuffer(texture, width, height);
        return TryMapAndCopyRows(destination, destinationBytesPerRow, rowBytes, height, timeoutMilliseconds);
    }

    public void Dispose()
    {
        QueueBufferDisposal();
        _mapSignal = null;
        GC.SuppressFinalize(this);
    }

    private void CopyTextureToBuffer(GpuTexture texture, uint width, uint height)
    {
        ThrowIfContextDisposed();

        var encoderDesc = new CommandEncoderDescriptor();
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);

        var copySrc = new ImageCopyTexture
        {
            Texture = texture.TexturePtr,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All
        };

        var copyDst = new ImageCopyBuffer
        {
            Buffer = _buffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = BytesPerRow,
                RowsPerImage = height
            }
        };

        var copySize = new Extent3D
        {
            Width = width,
            Height = height,
            DepthOrArrayLayers = 1
        };

        _context.Wgpu.CommandEncoderCopyTextureToBuffer(encoder, &copySrc, &copyDst, &copySize);

        var commandBufferDesc = new CommandBufferDescriptor();
        var commandBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &commandBufferDesc);

        _context.Wgpu.QueueSubmit(_context.Queue, 1, &commandBuffer);
        _context.Wgpu.CommandBufferRelease(commandBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);
    }

    private bool TryMapAndCopyRows(
        void* destination,
        uint destinationBytesPerRow,
        uint rowBytes,
        uint rows,
        int timeoutMilliseconds)
    {
        LastMapStatus = BufferMapAsyncStatus.ValidationError;
        LastMapTimedOut = false;

        using var signal = new ManualResetEventSlim(false);
        _mapSignal = signal;
        _context.Wgpu.BufferMapAsync(_buffer, MapMode.Read, 0, (nuint)BufferSize, _mapCallback, null);

        var stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet)
        {
            _context.PollDevice(wait: false);
            Thread.Sleep(1);
            if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
            {
                LastMapTimedOut = true;
                if (ReferenceEquals(_mapSignal, signal))
                {
                    _mapSignal = null;
                }

                QueueBufferDisposal();
                return false;
            }
        }

        if (ReferenceEquals(_mapSignal, signal))
        {
            _mapSignal = null;
        }

        if (LastMapStatus != BufferMapAsyncStatus.Success)
        {
            QueueBufferDisposal();
            return false;
        }

        _isMapActive = true;
        try
        {
            void* mappedPtr = _context.Wgpu.BufferGetConstMappedRange(_buffer, 0, (nuint)BufferSize);
            if (mappedPtr == null)
            {
                return false;
            }

            byte* sourceBytes = (byte*)mappedPtr;
            byte* destinationBytes = (byte*)destination;
            for (uint y = 0; y < rows; y++)
            {
                byte* sourceRow = sourceBytes + (y * BytesPerRow);
                byte* destinationRow = destinationBytes + (y * destinationBytesPerRow);
                System.Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
            }

            return true;
        }
        finally
        {
            UnmapActiveBuffer();
        }
    }

    private void OnBufferMapped(BufferMapAsyncStatus status, void* userData)
    {
        LastMapStatus = status;
        _mapSignal?.Set();
    }

    private void QueueBufferDisposal()
    {
        if (_buffer == null)
        {
            return;
        }

        UnmapActiveBuffer();

        if (!_context.IsDisposed)
        {
            _context.QueueBufferDisposal((IntPtr)_buffer);
        }

        _buffer = null;
        Width = 0;
        Height = 0;
        BytesPerPixel = 0;
        BytesPerRow = 0;
        BufferSize = 0;
    }

    private void UnmapActiveBuffer()
    {
        if (_buffer == null || !_isMapActive)
        {
            _isMapActive = false;
            return;
        }

        if (!_context.IsDisposed)
        {
            _context.Wgpu.BufferUnmap(_buffer);
        }

        _isMapActive = false;
    }

    private void ThrowIfContextDisposed()
    {
        if (_context.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(WgpuContext));
        }
    }
}
