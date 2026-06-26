using ProGPU.Backend;
using System.Runtime.InteropServices;

namespace ProGPU.DirectX;

public abstract class ProGpuDirectXResource : IDisposable
{
    private bool _isDisposed;

    protected ProGpuDirectXResource(ProGpuDirectXDevice device, string label)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Label = label;
    }

    public ProGpuDirectXDevice Device { get; }

    public string Label { get; }

    public bool IsDisposed => _isDisposed;

    protected void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    protected virtual void DisposeCore()
    {
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        DisposeCore();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

public sealed class ProGpuDirectXBuffer : ProGpuDirectXResource
{
    private readonly GpuBuffer? _backendBuffer;
    private readonly byte[]? _cpuShadow;
    private readonly byte[] _writeShadow;
    private ProGpuDirectXMappedSubresource? _activeMapping;

    internal ProGpuDirectXBuffer(ProGpuDirectXDevice device, DxBufferDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        ValidateDescriptor(descriptor);

        Descriptor = descriptor;
        if ((descriptor.CpuAccess & DxCpuAccessFlags.Read) != 0 ||
            (descriptor.CpuAccess & DxCpuAccessFlags.Write) != 0)
        {
            _cpuShadow = new byte[descriptor.SizeInBytes];
        }

        _writeShadow = _cpuShadow ?? new byte[descriptor.SizeInBytes];

        if (device.Context is { } context && device.IsGpuBacked)
        {
            _backendBuffer = new GpuBuffer(
                context,
                descriptor.SizeInBytes,
                ProGpuDirectXFormatConverter.ToBufferUsage(descriptor.Usage, descriptor.CpuAccess),
                descriptor.Label);
        }
    }

    public DxBufferDescriptor Descriptor { get; }

    public GpuBuffer? BackendBuffer => _backendBuffer;

    public uint LastWriteSizeInBytes { get; private set; }

    public uint LastWriteOffsetInBytes { get; private set; }

    public ulong Generation { get; private set; }

    public bool IsMapped => _activeMapping is not null;

    public unsafe void Write<T>(ReadOnlySpan<T> data, uint offsetBytes = 0) where T : unmanaged
    {
        ThrowIfDisposed();
        var dataSize = checked((uint)(data.Length * sizeof(T)));
        if (offsetBytes + dataSize > Descriptor.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Buffer write exceeds the DirectX buffer bounds.");
        }

        _backendBuffer?.Write(data, offsetBytes);
        var bytes = MemoryMarshal.AsBytes(data);
        bytes.CopyTo(_writeShadow.AsSpan(checked((int)offsetBytes), checked((int)dataSize)));
        if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
        {
            bytes.CopyTo(_cpuShadow.AsSpan(checked((int)offsetBytes), checked((int)dataSize)));
        }

        LastWriteSizeInBytes = dataSize;
        LastWriteOffsetInBytes = offsetBytes;
        Generation++;
    }

    public ProGpuDirectXMappedSubresource Map(
        DxMapMode mode,
        DxMapFlags flags = DxMapFlags.None,
        uint offsetBytes = 0,
        uint? sizeInBytes = null)
    {
        ThrowIfDisposed();
        ValidateMapMode(mode);
        if (_activeMapping is not null)
        {
            throw new InvalidOperationException("DirectX buffer is already mapped.");
        }

        var mapSize = sizeInBytes ?? (Descriptor.SizeInBytes - offsetBytes);
        ValidateReadRange(offsetBytes, mapSize);
        if (mapSize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Mapped DirectX buffer ranges must be non-empty.");
        }

        var requiresRead = RequiresCpuRead(mode);
        var requiresWrite = RequiresCpuWrite(mode);
        if (requiresRead && (Descriptor.CpuAccess & DxCpuAccessFlags.Read) == 0)
        {
            throw new InvalidOperationException("DirectX buffer was not created with CPU read access.");
        }

        if (requiresWrite && (Descriptor.CpuAccess & DxCpuAccessFlags.Write) == 0)
        {
            throw new InvalidOperationException("DirectX buffer was not created with CPU write access.");
        }

        if (requiresRead)
        {
            SynchronizeShadowForRead(offsetBytes, mapSize);
        }
        else if (mode == DxMapMode.WriteDiscard)
        {
            _writeShadow.AsSpan(checked((int)offsetBytes), checked((int)mapSize)).Clear();
        }

        _activeMapping = new ProGpuDirectXMappedSubresource(
            this,
            mode,
            flags,
            offsetBytes,
            mapSize,
            _writeShadow,
            uploadOnUnmap: requiresWrite);

        return _activeMapping;
    }

    public void Unmap(ProGpuDirectXMappedSubresource mapping)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mapping);
        if (!ReferenceEquals(mapping.Buffer, this))
        {
            throw new ArgumentException("Mapped DirectX subresource belongs to a different buffer.", nameof(mapping));
        }

        mapping.Unmap();
    }

    public byte[] ReadBytes(uint offsetBytes = 0, uint? sizeInBytes = null)
    {
        ThrowIfDisposed();
        if ((Descriptor.CpuAccess & DxCpuAccessFlags.Read) == 0)
        {
            throw new InvalidOperationException("Buffer was not created with CPU read access.");
        }

        var readSize = sizeInBytes ?? (Descriptor.SizeInBytes - offsetBytes);
        ValidateReadRange(offsetBytes, readSize);

        if (_backendBuffer is { BufferPtr: not null })
        {
            return _backendBuffer.ReadBytes(offsetBytes, readSize);
        }

        if (_cpuShadow is null)
        {
            throw new InvalidOperationException("Buffer does not have readable CPU storage.");
        }

        return _cpuShadow.AsSpan(checked((int)offsetBytes), checked((int)readSize)).ToArray();
    }

    public unsafe T[] Read<T>(uint elementCount, uint offsetBytes = 0) where T : unmanaged
    {
        var bytes = ReadBytes(offsetBytes, checked((uint)(elementCount * sizeof(T))));
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }

    internal void CopyCpuShadowFrom(ProGpuDirectXBuffer source)
    {
        var copySize = checked((int)Math.Min(Descriptor.SizeInBytes, source.Descriptor.SizeInBytes));
        source._writeShadow.AsSpan(0, copySize).CopyTo(_writeShadow);
        if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
        {
            _writeShadow.AsSpan(0, copySize).CopyTo(_cpuShadow);
        }

        LastWriteSizeInBytes = Math.Min(source.LastWriteSizeInBytes, Descriptor.SizeInBytes);
        LastWriteOffsetInBytes = 0;
        Generation++;
    }

    internal byte[] ReadWriteShadowBytes(uint offsetBytes, uint sizeInBytes)
    {
        ValidateReadRange(offsetBytes, sizeInBytes);
        return _writeShadow.AsSpan(checked((int)offsetBytes), checked((int)sizeInBytes)).ToArray();
    }

    internal void CompleteMappedSubresource(ProGpuDirectXMappedSubresource mapping)
    {
        if (!ReferenceEquals(_activeMapping, mapping))
        {
            throw new InvalidOperationException("DirectX buffer mapping is not active.");
        }

        if (mapping.UploadOnUnmap)
        {
            var mappedBytes = _writeShadow.AsSpan(
                checked((int)mapping.OffsetBytes),
                checked((int)mapping.SizeInBytes));

            _backendBuffer?.Write(mappedBytes, mapping.OffsetBytes);
            if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
            {
                mappedBytes.CopyTo(_cpuShadow.AsSpan(
                    checked((int)mapping.OffsetBytes),
                    checked((int)mapping.SizeInBytes)));
            }

            LastWriteOffsetInBytes = mapping.OffsetBytes;
            LastWriteSizeInBytes = mapping.SizeInBytes;
            Generation++;
        }

        _activeMapping = null;
    }

    private void SynchronizeShadowForRead(uint offsetBytes, uint sizeInBytes)
    {
        if (_backendBuffer is not { BufferPtr: not null })
        {
            return;
        }

        var bytes = _backendBuffer.ReadBytes(offsetBytes, sizeInBytes);
        bytes.CopyTo(_writeShadow.AsSpan(checked((int)offsetBytes), checked((int)sizeInBytes)));
        if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
        {
            bytes.CopyTo(_cpuShadow.AsSpan(checked((int)offsetBytes), checked((int)sizeInBytes)));
        }
    }

    private static void ValidateDescriptor(DxBufferDescriptor descriptor)
    {
        if (descriptor.SizeInBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX buffers must have a non-zero size.");
        }

        if ((descriptor.CpuAccess & DxCpuAccessFlags.Read) != 0 &&
            (descriptor.Usage & ~(DxBufferUsage.CopySource | DxBufferUsage.CopyDestination)) != 0)
        {
            throw new ArgumentException("CPU-readable DirectX buffers must be staging/copy resources without bind flags.", nameof(descriptor));
        }
    }

    private static void ValidateMapMode(DxMapMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown DirectX map mode.");
        }
    }

    private static bool RequiresCpuRead(DxMapMode mode)
    {
        return mode is DxMapMode.Read or DxMapMode.ReadWrite;
    }

    private static bool RequiresCpuWrite(DxMapMode mode)
    {
        return mode is DxMapMode.Write or DxMapMode.ReadWrite or DxMapMode.WriteDiscard or DxMapMode.WriteNoOverwrite;
    }

    private void ValidateReadRange(uint offsetBytes, uint sizeInBytes)
    {
        if (offsetBytes > Descriptor.SizeInBytes || sizeInBytes > Descriptor.SizeInBytes - offsetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Buffer read exceeds the DirectX buffer bounds.");
        }
    }

    protected override void DisposeCore()
    {
        _activeMapping?.Dispose();
        _activeMapping = null;
        _backendBuffer?.Dispose();
    }
}

public sealed class ProGpuDirectXMappedSubresource : IDisposable
{
    private ProGpuDirectXResource? _resource;
    private readonly Action<ProGpuDirectXMappedSubresource> _completeMapping;
    private readonly byte[] _data;

    internal ProGpuDirectXMappedSubresource(
        ProGpuDirectXBuffer buffer,
        DxMapMode mode,
        DxMapFlags flags,
        uint offsetBytes,
        uint sizeInBytes,
        byte[] data,
        bool uploadOnUnmap)
        : this(buffer, buffer.CompleteMappedSubresource, mode, flags, offsetBytes, sizeInBytes, sizeInBytes, sizeInBytes, data, uploadOnUnmap)
    {
    }

    internal ProGpuDirectXMappedSubresource(
        ProGpuDirectXTexture2D texture,
        DxMapMode mode,
        DxMapFlags flags,
        uint offsetBytes,
        uint sizeInBytes,
        uint rowPitch,
        uint depthPitch,
        byte[] data,
        bool uploadOnUnmap)
        : this(texture, texture.CompleteMappedSubresource, mode, flags, offsetBytes, sizeInBytes, rowPitch, depthPitch, data, uploadOnUnmap)
    {
    }

    private ProGpuDirectXMappedSubresource(
        ProGpuDirectXResource resource,
        Action<ProGpuDirectXMappedSubresource> completeMapping,
        DxMapMode mode,
        DxMapFlags flags,
        uint offsetBytes,
        uint sizeInBytes,
        uint rowPitch,
        uint depthPitch,
        byte[] data,
        bool uploadOnUnmap)
    {
        _resource = resource;
        _completeMapping = completeMapping;
        Mode = mode;
        Flags = flags;
        OffsetBytes = offsetBytes;
        SizeInBytes = sizeInBytes;
        RowPitch = rowPitch;
        DepthPitch = depthPitch;
        _data = data;
        UploadOnUnmap = uploadOnUnmap;
    }

    public ProGpuDirectXResource? Resource => _resource;

    public ProGpuDirectXBuffer? Buffer => _resource as ProGpuDirectXBuffer;

    public ProGpuDirectXTexture2D? Texture => _resource as ProGpuDirectXTexture2D;

    public DxMapMode Mode { get; }

    public DxMapFlags Flags { get; }

    public uint OffsetBytes { get; }

    public uint SizeInBytes { get; }

    public uint RowPitch { get; }

    public uint DepthPitch { get; }

    public bool IsMapped => _resource is not null;

    public Memory<byte> Data
    {
        get
        {
            ThrowIfUnmapped();
            return _data.AsMemory(checked((int)OffsetBytes), checked((int)SizeInBytes));
        }
    }

    public Span<byte> Span
    {
        get
        {
            ThrowIfUnmapped();
            return _data.AsSpan(checked((int)OffsetBytes), checked((int)SizeInBytes));
        }
    }

    internal bool UploadOnUnmap { get; }

    public unsafe void Write<T>(ReadOnlySpan<T> data, uint offsetBytes = 0) where T : unmanaged
    {
        ThrowIfUnmapped();
        var dataSize = checked((uint)(data.Length * sizeof(T)));
        ValidateMappedRange(offsetBytes, dataSize);
        MemoryMarshal.AsBytes(data).CopyTo(Span.Slice(checked((int)offsetBytes), checked((int)dataSize)));
    }

    public unsafe T[] Read<T>(uint elementCount, uint offsetBytes = 0) where T : unmanaged
    {
        ThrowIfUnmapped();
        var dataSize = checked((uint)(elementCount * sizeof(T)));
        ValidateMappedRange(offsetBytes, dataSize);
        return MemoryMarshal.Cast<byte, T>(Span.Slice(checked((int)offsetBytes), checked((int)dataSize))).ToArray();
    }

    public void Unmap()
    {
        if (_resource is null)
        {
            return;
        }

        _completeMapping(this);
        _resource = null;
    }

    public void Dispose()
    {
        Unmap();
    }

    private void ValidateMappedRange(uint offsetBytes, uint sizeInBytes)
    {
        if (offsetBytes > SizeInBytes || sizeInBytes > SizeInBytes - offsetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Mapped DirectX buffer access exceeds the mapped range.");
        }
    }

    private void ThrowIfUnmapped()
    {
        if (_resource is null)
        {
            throw new ObjectDisposedException(nameof(ProGpuDirectXMappedSubresource));
        }
    }
}

public sealed class ProGpuDirectXTexture2D : ProGpuDirectXResource
{
    private GpuTexture? _backendTexture;
    private byte[]? _cpuShadow;
    private byte[] _writeShadow = [];
    private ProGpuDirectXMappedSubresource? _activeMapping;

    internal ProGpuDirectXTexture2D(ProGpuDirectXDevice device, DxTexture2DDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        ValidateDescriptor(descriptor);
        Descriptor = descriptor;
        AllocateCpuStorage(descriptor);
        AllocateBackendTexture();
    }

    public DxTexture2DDescriptor Descriptor { get; private set; }

    public GpuTexture? BackendTexture => _backendTexture;

    public uint Width => Descriptor.Width;

    public uint Height => Descriptor.Height;

    public uint LastWriteSizeInBytes { get; private set; }

    public uint Generation { get; private set; }

    public bool IsMapped => _activeMapping is not null;

    internal void MarkBackendContentsChanged()
    {
        Generation++;
    }

    public unsafe void WritePixels<T>(ReadOnlySpan<T> pixels) where T : unmanaged
    {
        ThrowIfDisposed();
        var expectedSize = GetTextureSizeInBytes(Descriptor);
        var bytes = MemoryMarshal.AsBytes(pixels);
        if (bytes.Length < expectedSize)
        {
            throw new ArgumentException($"Pixel span is too small ({bytes.Length} bytes, expected {expectedSize} bytes).", nameof(pixels));
        }

        _backendTexture?.WritePixels(pixels);
        if (_writeShadow.Length > 0)
        {
            bytes.Slice(0, checked((int)expectedSize)).CopyTo(_writeShadow);
            if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
            {
                _writeShadow.CopyTo(_cpuShadow, 0);
            }
        }

        LastWriteSizeInBytes = expectedSize;
        Generation++;
    }

    public byte[] ReadPixels()
    {
        ThrowIfDisposed();
        if ((Descriptor.CpuAccess & DxCpuAccessFlags.Read) == 0)
        {
            throw new InvalidOperationException("Texture was not created with CPU read access.");
        }

        if (_backendTexture is { IsDisposed: false } texture)
        {
            var pixels = texture.ReadPixels();
            pixels.CopyTo(_writeShadow.AsSpan(0, pixels.Length));
            if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
            {
                pixels.CopyTo(_cpuShadow.AsSpan(0, pixels.Length));
            }

            return pixels;
        }

        if (_cpuShadow is null)
        {
            throw new InvalidOperationException("Texture readback requires a GPU-backed texture.");
        }

        return _cpuShadow.ToArray();
    }

    public ProGpuDirectXMappedSubresource Map(
        DxMapMode mode,
        DxMapFlags flags = DxMapFlags.None,
        uint subresource = 0)
    {
        ThrowIfDisposed();
        ValidateMapMode(mode);
        ValidateMappableSubresource(subresource);
        if (_activeMapping is not null)
        {
            throw new InvalidOperationException("DirectX texture is already mapped.");
        }

        var requiresRead = RequiresCpuRead(mode);
        var requiresWrite = RequiresCpuWrite(mode);
        if (requiresRead && (Descriptor.CpuAccess & DxCpuAccessFlags.Read) == 0)
        {
            throw new InvalidOperationException("DirectX texture was not created with CPU read access.");
        }

        if (requiresWrite && (Descriptor.CpuAccess & DxCpuAccessFlags.Write) == 0)
        {
            throw new InvalidOperationException("DirectX texture was not created with CPU write access.");
        }

        var rowPitch = GetRowPitchInBytes(Descriptor);
        var depthPitch = GetSubresourceSizeInBytes(Descriptor);
        if (requiresRead)
        {
            SynchronizeShadowForRead();
        }
        else if (mode == DxMapMode.WriteDiscard)
        {
            _writeShadow.AsSpan(0, checked((int)depthPitch)).Clear();
        }

        _activeMapping = new ProGpuDirectXMappedSubresource(
            this,
            mode,
            flags,
            offsetBytes: 0,
            sizeInBytes: depthPitch,
            rowPitch,
            depthPitch,
            _writeShadow,
            uploadOnUnmap: requiresWrite);

        return _activeMapping;
    }

    public void Unmap(ProGpuDirectXMappedSubresource mapping)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(mapping);
        if (!ReferenceEquals(mapping.Texture, this))
        {
            throw new ArgumentException("Mapped DirectX subresource belongs to a different texture.", nameof(mapping));
        }

        mapping.Unmap();
    }

    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        if (width == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (Descriptor.Width == width && Descriptor.Height == height)
        {
            return;
        }

        Descriptor = Descriptor with { Width = width, Height = height };
        AllocateCpuStorage(Descriptor);
        if (_backendTexture != null)
        {
            _backendTexture.Resize(width, height);
        }

        Generation++;
    }

    internal void CompleteMappedSubresource(ProGpuDirectXMappedSubresource mapping)
    {
        if (!ReferenceEquals(_activeMapping, mapping))
        {
            throw new InvalidOperationException("DirectX texture mapping is not active.");
        }

        if (mapping.UploadOnUnmap)
        {
            var mappedBytes = _writeShadow.AsSpan(0, checked((int)mapping.SizeInBytes));
            _backendTexture?.WritePixels(mappedBytes);
            if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
            {
                mappedBytes.CopyTo(_cpuShadow.AsSpan(0, checked((int)mapping.SizeInBytes)));
            }

            LastWriteSizeInBytes = mapping.SizeInBytes;
            Generation++;
        }

        _activeMapping = null;
    }

    private void AllocateCpuStorage(DxTexture2DDescriptor descriptor)
    {
        if ((descriptor.CpuAccess & (DxCpuAccessFlags.Read | DxCpuAccessFlags.Write)) == 0)
        {
            _cpuShadow = null;
            _writeShadow = [];
            LastWriteSizeInBytes = 0;
            return;
        }

        var byteSize = GetTextureSizeInBytes(descriptor);
        _cpuShadow = new byte[byteSize];
        _writeShadow = _cpuShadow ?? new byte[byteSize];
        LastWriteSizeInBytes = 0;
    }

    private void AllocateBackendTexture()
    {
        if (Device.Context is not { } context || !Device.IsGpuBacked)
        {
            return;
        }

        _backendTexture = new GpuTexture(
            context,
            Descriptor.Width,
            Descriptor.Height,
            ProGpuDirectXFormatConverter.ToTextureFormat(Descriptor.Format),
            ProGpuDirectXFormatConverter.ToTextureUsage(Descriptor.Usage),
            Descriptor.Label,
            Descriptor.SampleCount,
            ProGpuDirectXFormatConverter.ToTextureAlphaMode(Descriptor.Format),
            Descriptor.ArraySize);
    }

    private void SynchronizeShadowForRead()
    {
        if (_backendTexture is not { IsDisposed: false } texture)
        {
            return;
        }

        var pixels = texture.ReadPixels();
        pixels.CopyTo(_writeShadow.AsSpan(0, pixels.Length));
        if (_cpuShadow is not null && !ReferenceEquals(_cpuShadow, _writeShadow))
        {
            pixels.CopyTo(_cpuShadow.AsSpan(0, pixels.Length));
        }
    }

    private void ValidateMappableSubresource(uint subresource)
    {
        if (subresource != 0)
        {
            throw new NotSupportedException("DirectX texture mapping currently supports only subresource 0.");
        }

        if (Descriptor.MipLevels != 1 || Descriptor.ArraySize != 1)
        {
            throw new NotSupportedException("DirectX texture mapping currently supports only single-mip, single-array textures.");
        }

        if (Descriptor.SampleCount != 1)
        {
            throw new NotSupportedException("DirectX texture mapping currently supports only single-sample textures.");
        }

        _ = GetBytesPerPixel(Descriptor.Format);
    }

    private static void ValidateDescriptor(DxTexture2DDescriptor descriptor)
    {
        if (descriptor.Width == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX textures must have a non-zero width.");
        }

        if (descriptor.Height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX textures must have a non-zero height.");
        }

        if (descriptor.MipLevels == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX textures must have at least one mip level.");
        }

        if (descriptor.ArraySize == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX textures must have at least one array slice.");
        }

        if (descriptor.SampleCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "DirectX textures must have at least one sample.");
        }
    }

    private static void ValidateMapMode(DxMapMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown DirectX map mode.");
        }
    }

    private static bool RequiresCpuRead(DxMapMode mode)
    {
        return mode is DxMapMode.Read or DxMapMode.ReadWrite;
    }

    private static bool RequiresCpuWrite(DxMapMode mode)
    {
        return mode is DxMapMode.Write or DxMapMode.ReadWrite or DxMapMode.WriteDiscard or DxMapMode.WriteNoOverwrite;
    }

    private static uint GetSubresourceSizeInBytes(DxTexture2DDescriptor descriptor)
    {
        return checked(GetRowPitchInBytes(descriptor) * descriptor.Height);
    }

    private static uint GetTextureSizeInBytes(DxTexture2DDescriptor descriptor)
    {
        return checked(GetSubresourceSizeInBytes(descriptor) * descriptor.ArraySize);
    }

    private static uint GetRowPitchInBytes(DxTexture2DDescriptor descriptor)
    {
        return checked(descriptor.Width * GetBytesPerPixel(descriptor.Format));
    }

    private static uint GetBytesPerPixel(DxResourceFormat format)
    {
        return format switch
        {
            DxResourceFormat.R8Unorm => 1,
            DxResourceFormat.R32Float or
            DxResourceFormat.R32UInt or
            DxResourceFormat.R32SInt => 4,
            DxResourceFormat.R8G8B8A8Unorm or
            DxResourceFormat.R8G8B8A8UnormSrgb or
            DxResourceFormat.B8G8R8A8Unorm or
            DxResourceFormat.B8G8R8A8UnormSrgb => 4,
            _ => throw new NotSupportedException($"DirectX texture mapping does not support resource format {format}.")
        };
    }

    protected override void DisposeCore()
    {
        _activeMapping?.Dispose();
        _activeMapping = null;
        _backendTexture?.Dispose();
        _backendTexture = null;
    }
}

public sealed class ProGpuDirectXSwapChain : IDisposable
{
    private readonly ProGpuDirectXDevice _device;
    private ProGpuDirectXTexture2D _backBuffer;
    private bool _isDisposed;

    internal ProGpuDirectXSwapChain(ProGpuDirectXDevice device, DxSwapChainDescriptor descriptor)
    {
        _device = device;
        Descriptor = descriptor;
        _backBuffer = CreateBackBuffer(device, descriptor);
    }

    public DxSwapChainDescriptor Descriptor { get; private set; }

    public ProGpuDirectXTexture2D BackBuffer => _backBuffer;

    public ulong PresentCount { get; private set; }

    public ProGpuDirectXTexture2D AcquireBackBuffer()
    {
        ThrowIfDisposed();
        return _backBuffer;
    }

    public void Resize(uint width, uint height)
    {
        ThrowIfDisposed();
        if (width == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Descriptor = Descriptor with { Width = width, Height = height };
        _backBuffer.Resize(width, height);
    }

    public void Present()
    {
        ThrowIfDisposed();
        PresentCount++;
    }

    private static ProGpuDirectXTexture2D CreateBackBuffer(ProGpuDirectXDevice device, DxSwapChainDescriptor descriptor)
    {
        return device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Format = descriptor.Format,
            Usage = DxTextureUsage.RenderTarget | DxTextureUsage.Present | DxTextureUsage.CopyDestination,
            Label = descriptor.Label + ".BackBuffer"
        });
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ProGpuDirectXSwapChain));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _backBuffer.Dispose();
        _isDisposed = true;
    }
}
