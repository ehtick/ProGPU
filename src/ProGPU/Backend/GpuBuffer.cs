using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace ProGPU.Backend;

public unsafe class GpuBuffer : IDisposable
{
    private readonly WgpuContext _context;
    
    public Buffer* BufferPtr { get; private set; }
    public uint Size { get; private set; }
    public BufferUsage Usage { get; private set; }
    
    private bool _isDisposed;

    public GpuBuffer(WgpuContext context, uint size, BufferUsage usage, string label = "GpuBuffer")
    {
        _context = context;
        Size = size;
        Usage = usage;

        var labelPtr = SilkMarshal.StringToPtr(label);
        var desc = new BufferDescriptor
        {
            Label = (byte*)labelPtr,
            Size = size,
            Usage = usage,
            MappedAtCreation = false
        };

        BufferPtr = _context.Wgpu.DeviceCreateBuffer(_context.Device, &desc);
        SilkMarshal.Free(labelPtr);

        if (BufferPtr == null)
        {
            throw new InvalidOperationException($"Failed to allocate GPU Buffer of size {size} bytes.");
        }
    }

    public void Write<T>(ReadOnlySpan<T> data, uint offsetBytes = 0) where T : unmanaged
    {
        if (_isDisposed || BufferPtr == null) throw new ObjectDisposedException(nameof(GpuBuffer));
        
        uint dataSize = (uint)(data.Length * sizeof(T));
        if (offsetBytes + dataSize > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(data), $"Data size {dataSize} at offset {offsetBytes} exceeds buffer size {Size}.");
        }

        fixed (T* ptr = data)
        {
            _context.Wgpu.QueueWriteBuffer(_context.Queue, BufferPtr, offsetBytes, ptr, dataSize);
        }
    }

    public void WriteSingle<T>(T value, uint offsetBytes = 0) where T : unmanaged
    {
        Write(new ReadOnlySpan<T>(&value, 1), offsetBytes);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        if (BufferPtr != null)
        {
            _context.Wgpu.BufferDestroy(BufferPtr);
            _context.Wgpu.BufferRelease(BufferPtr);
            BufferPtr = null;
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~GpuBuffer()
    {
        Dispose();
    }
}
