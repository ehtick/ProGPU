using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using ProGPU.Backend;

namespace ProGPU.Compute;

public unsafe class ComputeAccelerator : IDisposable
{
    private readonly WgpuContext _context;
    private readonly RenderPipelineCache _cache;

    private ComputePipeline* _blurHorizPipeline;
    private ComputePipeline* _blurVertPipeline;
    private ComputePipeline* _shadowPipeline;

    private bool _isDisposed;

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ShadowParams
    {
        [FieldOffset(0)] public Vector2 Offset;
        [FieldOffset(16)] public Vector4 Color;
        [FieldOffset(32)] public float BlurRadius;
        [FieldOffset(36)] private float _padding;
        [FieldOffset(40)] private float _pad0;
        [FieldOffset(44)] private float _pad1;
        [FieldOffset(48)] private float _pad2;
        [FieldOffset(52)] private float _pad3;
        [FieldOffset(56)] private float _pad4;
        [FieldOffset(60)] private float _pad5;

        public ShadowParams(Vector2 offset, Vector4 color, float blurRadius)
        {
            Offset = offset;
            Color = color;
            BlurRadius = blurRadius;
            _padding = 0f;
            _pad0 = 0f;
            _pad1 = 0f;
            _pad2 = 0f;
            _pad3 = 0f;
            _pad4 = 0f;
            _pad5 = 0f;
        }
    }

    public ComputeAccelerator(WgpuContext context)
    {
        _context = context;
        _cache = new RenderPipelineCache(_context);

        InitializePipelines();
    }

    private void InitializePipelines()
    {
        var shBlurH = _cache.GetOrCreateShader("BlurH", ComputeShaders.GaussianBlurHorizontal, "BlurHShader");
        _blurHorizPipeline = _cache.GetOrCreateComputePipeline("BlurH", shBlurH);

        var shBlurV = _cache.GetOrCreateShader("BlurV", ComputeShaders.GaussianBlurVertical, "BlurVShader");
        _blurVertPipeline = _cache.GetOrCreateComputePipeline("BlurV", shBlurV);

        var shShadow = _cache.GetOrCreateShader("Shadow", ComputeShaders.DropShadow, "ShadowShader");
        _shadowPipeline = _cache.GetOrCreateComputePipeline("Shadow", shShadow);
    }

    public void ApplyGaussianBlur(GpuTexture source, GpuTexture temp, GpuTexture destination)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ComputeAccelerator));

        uint width = source.Width;
        uint height = source.Height;

        // Ensure temp and destination are resized to match source
        temp.Resize(width, height);
        destination.Resize(width, height);

        // 1. Create horizontal pass command encoder and bind group
        var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Compute Blur Encoder") };
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);
        SilkMarshal.Free((nint)encoderDesc.Label);

        // Get bind group layout from pipeline
        var blurHLayout = _context.Wgpu.ComputePipelineGetBindGroupLayout(_blurHorizPipeline, 0);

        var entriesH = stackalloc BindGroupEntry[2];
        entriesH[0] = new BindGroupEntry { Binding = 0, TextureView = source.ViewPtr };
        entriesH[1] = new BindGroupEntry { Binding = 1, TextureView = temp.ViewPtr };

        var bgDescH = new BindGroupDescriptor
        {
            Layout = blurHLayout,
            EntryCount = 2,
            Entries = entriesH
        };
        var bgH = _context.Wgpu.DeviceCreateBindGroup(_context.Device, &bgDescH);

        // Begin compute pass
        var passDesc = new ComputePassDescriptor();
        var pass = _context.Wgpu.CommandEncoderBeginComputePass(encoder, &passDesc);

        // Record horizontal pass
        _context.Wgpu.ComputePassEncoderSetPipeline(pass, _blurHorizPipeline);
        _context.Wgpu.ComputePassEncoderSetBindGroup(pass, 0, bgH, 0, null);

        // Workgroups calculation (ceil division by 16)
        uint workgroupX = (width + 15) / 16;
        uint workgroupY = (height + 15) / 16;
        _context.Wgpu.ComputePassEncoderDispatchWorkgroups(pass, workgroupX, workgroupY, 1);

        _context.Wgpu.ComputePassEncoderEnd(pass);
        _context.Wgpu.ComputePassEncoderRelease(pass);

        // 2. Create vertical pass bind group
        var blurVLayout = _context.Wgpu.ComputePipelineGetBindGroupLayout(_blurVertPipeline, 0);

        var entriesV = stackalloc BindGroupEntry[2];
        entriesV[0] = new BindGroupEntry { Binding = 0, TextureView = temp.ViewPtr };
        entriesV[1] = new BindGroupEntry { Binding = 1, TextureView = destination.ViewPtr };

        var bgDescV = new BindGroupDescriptor
        {
            Layout = blurVLayout,
            EntryCount = 2,
            Entries = entriesV
        };
        var bgV = _context.Wgpu.DeviceCreateBindGroup(_context.Device, &bgDescV);

        var pass2 = _context.Wgpu.CommandEncoderBeginComputePass(encoder, &passDesc);
        _context.Wgpu.ComputePassEncoderSetPipeline(pass2, _blurVertPipeline);
        _context.Wgpu.ComputePassEncoderSetBindGroup(pass2, 0, bgV, 0, null);
        _context.Wgpu.ComputePassEncoderDispatchWorkgroups(pass2, workgroupX, workgroupY, 1);

        _context.Wgpu.ComputePassEncoderEnd(pass2);
        _context.Wgpu.ComputePassEncoderRelease(pass2);

        // Submit commands to queue
        var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Compute Blur Buffer") };
        var cmdBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &cmdDesc);
        SilkMarshal.Free((nint)cmdDesc.Label);

        _context.Wgpu.QueueSubmit(_context.Queue, 1, &cmdBuffer);

        // Release resources
        _context.Wgpu.CommandBufferRelease(cmdBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);
        _context.Wgpu.BindGroupRelease(bgH);
        _context.Wgpu.BindGroupRelease(bgV);
        _context.Wgpu.BindGroupLayoutRelease(blurHLayout);
        _context.Wgpu.BindGroupLayoutRelease(blurVLayout);
    }

    public void ApplyDropShadow(GpuTexture source, GpuTexture destination, Vector2 offset, Vector4 shadowColor, float blurRadius)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ComputeAccelerator));

        uint width = source.Width;
        uint height = source.Height;

        destination.Resize(width, height);

        // 1. Write params to uniform buffer
        var paramsBuffer = new GpuBuffer(
            _context,
            (uint)Marshal.SizeOf<ShadowParams>(),
            BufferUsage.Uniform | BufferUsage.CopyDst,
            "Shadow Params Buffer"
        );
        paramsBuffer.WriteSingle(new ShadowParams(offset, shadowColor, blurRadius));

        // 2. Encode compute commands
        var encoderDesc = new CommandEncoderDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Compute Shadow Encoder") };
        var encoder = _context.Wgpu.DeviceCreateCommandEncoder(_context.Device, &encoderDesc);
        SilkMarshal.Free((nint)encoderDesc.Label);

        var shadowLayout = _context.Wgpu.ComputePipelineGetBindGroupLayout(_shadowPipeline, 0);

        var entries = stackalloc BindGroupEntry[3];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = source.ViewPtr };
        entries[1] = new BindGroupEntry { Binding = 1, TextureView = destination.ViewPtr };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = paramsBuffer.BufferPtr, Offset = 0, Size = paramsBuffer.Size };

        var bgDesc = new BindGroupDescriptor
        {
            Layout = shadowLayout,
            EntryCount = 3,
            Entries = entries
        };
        var bg = _context.Wgpu.DeviceCreateBindGroup(_context.Device, &bgDesc);

        var passDesc = new ComputePassDescriptor();
        var pass = _context.Wgpu.CommandEncoderBeginComputePass(encoder, &passDesc);

        _context.Wgpu.ComputePassEncoderSetPipeline(pass, _shadowPipeline);
        _context.Wgpu.ComputePassEncoderSetBindGroup(pass, 0, bg, 0, null);

        uint workgroupX = (width + 15) / 16;
        uint workgroupY = (height + 15) / 16;
        _context.Wgpu.ComputePassEncoderDispatchWorkgroups(pass, workgroupX, workgroupY, 1);

        _context.Wgpu.ComputePassEncoderEnd(pass);
        _context.Wgpu.ComputePassEncoderRelease(pass);

        var cmdDesc = new CommandBufferDescriptor { Label = (byte*)SilkMarshal.StringToPtr("Compute Shadow Buffer") };
        var cmdBuffer = _context.Wgpu.CommandEncoderFinish(encoder, &cmdDesc);
        SilkMarshal.Free((nint)cmdDesc.Label);

        _context.Wgpu.QueueSubmit(_context.Queue, 1, &cmdBuffer);

        // Cleanup
        _context.Wgpu.CommandBufferRelease(cmdBuffer);
        _context.Wgpu.CommandEncoderRelease(encoder);
        _context.Wgpu.BindGroupRelease(bg);
        _context.Wgpu.BindGroupLayoutRelease(shadowLayout);
        paramsBuffer.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _cache.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    ~ComputeAccelerator()
    {
        Dispose();
    }
}
