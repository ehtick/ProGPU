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
            if (dataSize % 4 == 0)
            {
                _context.Wgpu.QueueWriteBuffer(_context.Queue, BufferPtr, offsetBytes, ptr, dataSize);
            }
            else
            {
                uint paddedSize = (dataSize + 3) & ~3u;
                byte* temp = stackalloc byte[(int)paddedSize];
                System.Buffer.MemoryCopy(ptr, temp, paddedSize, dataSize);
                for (uint i = dataSize; i < paddedSize; i++)
                {
                    temp[i] = 0;
                }
                _context.Wgpu.QueueWriteBuffer(_context.Queue, BufferPtr, offsetBytes, temp, paddedSize);
            }
        }
    }

    public void WriteSingle<T>(T value, uint offsetBytes = 0) where T : unmanaged
    {
        Write(new ReadOnlySpan<T>(&value, 1), offsetBytes);
    }

    public byte[] ReadBytes(uint offsetBytes = 0, uint? sizeBytes = null)
    {
        if (_isDisposed || BufferPtr == null) throw new ObjectDisposedException(nameof(GpuBuffer));

        var readSize = sizeBytes ?? (Size - offsetBytes);
        ValidateReadRange(offsetBytes, readSize);

        if (Usage.HasFlag(BufferUsage.MapRead))
        {
            return MapReadBuffer(BufferPtr, offsetBytes, readSize, destroyAfterRead: false);
        }

        if (!Usage.HasFlag(BufferUsage.CopySrc))
        {
            throw new InvalidOperationException("Buffer was not created with CopySrc or MapRead usage.");
        }

        var readbackDesc = new BufferDescriptor
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = readSize,
            MappedAtCreation = false
        };
        var readbackBuffer = _context.Wgpu.DeviceCreateBuffer(_context.Device, &readbackDesc);
        if (readbackBuffer == null)
        {
            throw new InvalidOperationException("Failed to create readback buffer.");
        }

        var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Buffer Readback Encoder") };
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);
        SilkMarshal.Free((nint)encoderDesc.Label);
        if (encoder == null)
        {
            _context.Wgpu.BufferDestroy(readbackBuffer);
            _context.Wgpu.BufferRelease(readbackBuffer);
            throw new InvalidOperationException("Failed to create command encoder for buffer readback.");
        }

        _context.Wgpu.CommandEncoderCopyBufferToBuffer(
            encoder,
            BufferPtr,
            offsetBytes,
            readbackBuffer,
            0,
            readSize);

        var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Buffer Readback Command Buffer") };
        var commandBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &cmdDesc);
        SilkMarshal.Free((nint)cmdDesc.Label);
        if (commandBuffer == null)
        {
            _context.Wgpu.CommandEncoderRelease(encoder);
            _context.Wgpu.BufferDestroy(readbackBuffer);
            _context.Wgpu.BufferRelease(readbackBuffer);
            throw new InvalidOperationException("Failed to finish buffer readback command encoder.");
        }

        _context.Wgpu.QueueSubmit(_context.Queue, 1, &commandBuffer);
        _context.Wgpu.CommandBufferRelease(commandBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);

        return MapReadBuffer(readbackBuffer, 0, readSize, destroyAfterRead: true);
    }

    private byte[] MapReadBuffer(Buffer* buffer, uint offsetBytes, uint sizeBytes, bool destroyAfterRead)
    {
        var mapSignal = new System.Threading.ManualResetEventSlim(false);
        var mapStatus = BufferMapAsyncStatus.ValidationError;
        var onMapped = PfnBufferMapCallback.From((status, userData) =>
        {
            mapStatus = status;
            mapSignal.Set();
        });

        _context.Wgpu.BufferMapAsync(buffer, MapMode.Read, offsetBytes, (nuint)sizeBytes, onMapped, null);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!mapSignal.IsSet)
        {
            wgpuDevicePoll(_context.Device, false, null);
            System.Threading.Thread.Sleep(1);
            if (stopwatch.ElapsedMilliseconds > 5000)
            {
                CleanupMappedReadBuffer(buffer, destroyAfterRead);
                throw new TimeoutException("WebGPU BufferMapAsync timed out after 5 seconds during buffer readback.");
            }
        }

        if (mapStatus != BufferMapAsyncStatus.Success)
        {
            CleanupMappedReadBuffer(buffer, destroyAfterRead);
            throw new InvalidOperationException($"Failed to map readback buffer. WebGPU Status: {mapStatus}");
        }

        var bytes = new byte[sizeBytes];
        var mappedPtr = _context.Wgpu.BufferGetConstMappedRange(buffer, offsetBytes, (nuint)sizeBytes);
        if (mappedPtr != null)
        {
            Marshal.Copy((nint)mappedPtr, bytes, 0, checked((int)sizeBytes));
        }

        _context.Wgpu.BufferUnmap(buffer);
        if (destroyAfterRead)
        {
            _context.Wgpu.BufferDestroy(buffer);
            _context.Wgpu.BufferRelease(buffer);
        }

        return bytes;
    }

    private void CleanupMappedReadBuffer(Buffer* buffer, bool destroyAfterRead)
    {
        if (destroyAfterRead)
        {
            _context.Wgpu.BufferDestroy(buffer);
            _context.Wgpu.BufferRelease(buffer);
        }
    }

    private void ValidateReadRange(uint offsetBytes, uint sizeBytes)
    {
        if (offsetBytes > Size || sizeBytes > Size - offsetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Buffer read exceeds the buffer bounds.");
        }

        if ((offsetBytes % 4) != 0 || (sizeBytes % 4) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "GPU buffer readback requires 4-byte aligned offset and size.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_context.RenderLock)
        {
            if (BufferPtr != null)
            {
                if (!_context.IsDisposed)
                {
                    _context.QueueBufferDisposal((IntPtr)BufferPtr);
                }

                BufferPtr = null;
            }
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~GpuBuffer()
    {
        if (BufferPtr != null)
        {
            try
            {
                _context.QueueBufferDisposal((IntPtr)BufferPtr);
            }
            catch {}
        }
    }

    [DllImport("wgpu_native", EntryPoint = "wgpuDevicePoll")]
    private static extern bool wgpuDevicePoll(Device* device, bool wait, void* wrappedSubmissionIndex);
}
