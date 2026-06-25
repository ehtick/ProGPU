using ProGPU.Backend;
using ProGPU.DirectX;
using Xunit;

namespace ProGPU.Tests;

public sealed class DirectXShimTests
{
    private const string PassthroughWgsl = """
struct VertexIn {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
};

struct VertexOut {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
};

@vertex
fn vs_main(input: VertexIn) -> VertexOut {
    var output: VertexOut;
    output.position = vec4<f32>(input.position, 1.0);
    output.color = input.color;
    return output;
}

@fragment
fn fs_main(input: VertexOut) -> @location(0) vec4<f32> {
    return input.color;
}
""";

    private const string ComputeWgsl = """
@compute @workgroup_size(1)
fn cs_main() {
}
""";

    private const string SolidTriangleWgsl = """
@vertex
fn vs_main(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
    var positions = array<vec2<f32>, 3>(
        vec2<f32>(-0.9, -0.9),
        vec2<f32>(0.9, -0.9),
        vec2<f32>(0.0, 0.9));
    let p = positions[vertexIndex];
    return vec4<f32>(p, 0.0, 1.0);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
""";

    [Fact]
    public void MetadataDeviceAdvertisesSciChartFeatureLevelRange()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();

        Assert.False(device.IsGpuBacked);
        Assert.True(device.Capabilities.SupportsFeatureLevel(DxFeatureLevel.Direct3D9_3));
        Assert.True(device.Capabilities.SupportsFeatureLevel(DxFeatureLevel.Direct3D10_0));
        Assert.True(device.Capabilities.SupportsFeatureLevel(DxFeatureLevel.Direct3D11_0));
        Assert.Equal(DxFeatureLevel.Direct3D11_1, device.Capabilities.HighestFeatureLevel);
    }

    [Fact]
    public void RequireGpuBackedResourcesFailsClosedWithoutContext()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProGpuDirectXDevice.CreateMetadataDevice(new ProGpuDirectXDeviceOptions
            {
                RequireGpuBackedResources = true
            }));
    }

    [Fact]
    public void CanCreateResizeAndPresentSwapChain()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var swapChain = device.CreateSwapChain(new DxSwapChainDescriptor
        {
            Width = 640,
            Height = 480
        });

        Assert.Equal(640u, swapChain.BackBuffer.Width);
        Assert.Equal(480u, swapChain.BackBuffer.Height);

        swapChain.Resize(800, 600);
        swapChain.Present();

        Assert.Equal(800u, swapChain.BackBuffer.Width);
        Assert.Equal(600u, swapChain.BackBuffer.Height);
        Assert.Equal(1u, swapChain.PresentCount);
    }

    [Fact]
    public void ImmediateContextRecordsSciChartStyleRenderCommands()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var target = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 320,
            Height = 200,
            Usage = DxTextureUsage.RenderTarget | DxTextureUsage.ShaderResource | DxTextureUsage.CopyDestination
        });
        using var vertexBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = DxBufferUsage.Vertex | DxBufferUsage.CopyDestination,
            StrideInBytes = 16
        });
        using var context = device.CreateImmediateContext();

        context.SetRenderTargets(target);
        context.SetViewport(new DxViewport(0, 0, 320, 200));
        context.SetScissorRect(new DxRect(0, 0, 320, 200));
        context.SetPrimitiveTopology(DxPrimitiveTopology.TriangleList);
        context.SetVertexBuffer(vertexBuffer);
        context.ClearRenderTarget(target, new DxColor(0.1f, 0.2f, 0.3f, 1f));
        context.Draw(6);

        Assert.Equal(7, context.Commands.Count);
        Assert.Equal(ProGpuDirectXCommandKind.SetRenderTargets, context.Commands[0].Kind);
        Assert.Equal(ProGpuDirectXCommandKind.Draw, context.Commands[^1].Kind);
        Assert.Equal(6u, context.Commands[^1].Draw!.VertexCount);

        context.Flush();

        Assert.Empty(context.Commands);
    }

    [Fact]
    public void CanCreateSciChartStyleShaderInputLayoutAndPipelineMetadata()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var vertexShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Vertex,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = PassthroughWgsl
        });
        using var pixelShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Pixel,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = PassthroughWgsl
        });
        var inputLayout = device.CreateInputLayout(new DxInputLayoutDescriptor
        {
            Elements =
            [
                new DxInputElementDescriptor
                {
                    SemanticName = "POSITION",
                    Format = DxResourceFormat.R32G32B32Float,
                    AlignedByteOffset = 0,
                    ShaderLocation = 0
                },
                new DxInputElementDescriptor
                {
                    SemanticName = "COLOR",
                    Format = DxResourceFormat.R32G32B32A32Float,
                    AlignedByteOffset = 12,
                    ShaderLocation = 1
                }
            ]
        });
        using var pipeline = device.CreateGraphicsPipeline(new DxGraphicsPipelineDescriptor
        {
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            InputLayout = inputLayout,
            Topology = DxPrimitiveTopology.TriangleList,
            RenderTargetFormat = DxResourceFormat.B8G8R8A8Unorm,
            DepthStencilFormat = DxResourceFormat.D24UnormS8UInt,
            DepthStencilState = new DxDepthStencilStateDescriptor
            {
                DepthEnable = true,
                DepthWriteMask = DxDepthWriteMask.All,
                DepthFunction = DxComparisonFunction.LessEqual
            },
            RasterizerState = new DxRasterizerStateDescriptor
            {
                CullMode = DxCullMode.Back,
                FrontFace = DxFrontFace.CounterClockwise,
                ScissorEnable = true
            }
        });
        using var context = device.CreateImmediateContext();

        context.SetGraphicsPipeline(pipeline);
        context.DrawIndexed(36, indexFormat: DxIndexFormat.UInt16);

        Assert.False(vertexShader.HasBackendShaderModule);
        Assert.False(pipeline.HasBackendPipeline);
        Assert.Equal(28u, inputLayout.GetInferredStride(0));
        Assert.Contains(vertexShader.SourceHash, pipeline.PipelineKey, StringComparison.Ordinal);
        Assert.Same(pipeline, context.GraphicsPipeline);
        Assert.Equal(ProGpuDirectXCommandKind.SetGraphicsPipeline, context.Commands[0].Kind);
        Assert.Equal(ProGpuDirectXCommandKind.DrawIndexed, context.Commands[1].Kind);
        Assert.Same(pipeline, context.Commands[1].GraphicsPipeline);
        Assert.Equal(DxIndexFormat.UInt16, context.Commands[1].DrawIndexed!.IndexFormat);
    }

    [Fact]
    public void HlslBytecodeShadersRemainMetadataUntilTranslatorOrNativeFacadeIsConnected()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var shader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Vertex,
            SourceKind = DxShaderSourceKind.HlslBytecode,
            Bytecode = new byte[] { 0x44, 0x58, 0x42, 0x43 }
        });

        Assert.False(shader.HasBackendShaderModule);
        Assert.Equal("vs_main", shader.EntryPoint);
        Assert.NotEqual(string.Empty, shader.SourceHash);
    }

    [Fact]
    public void RejectsMismatchedPipelineShaderStages()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var pixelShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Pixel,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = PassthroughWgsl
        });

        Assert.Throws<ArgumentException>(() =>
            device.CreateGraphicsPipeline(new DxGraphicsPipelineDescriptor
            {
                VertexShader = pixelShader
            }));
    }

    [Fact]
    public void CanRecordComputePipelineAndDispatchMetadata()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var computeShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Compute,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = ComputeWgsl
        });
        using var pipeline = device.CreateComputePipeline(new DxComputePipelineDescriptor
        {
            ComputeShader = computeShader
        });
        using var context = device.CreateImmediateContext();

        context.SetComputePipeline(pipeline);
        context.Dispatch(8, 4, 1);

        Assert.False(pipeline.HasBackendPipeline);
        Assert.Same(pipeline, context.ComputePipeline);
        Assert.Equal(ProGpuDirectXCommandKind.Dispatch, context.Commands[1].Kind);
        Assert.Equal(new DxDispatchCall(8, 4, 1), context.Commands[1].Dispatch);
    }

    [Fact]
    public void DrawCommandsCaptureDeferredVertexAndIndexBufferState()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var vertexBuffer0 = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 128,
            Usage = DxBufferUsage.Vertex | DxBufferUsage.CopyDestination,
            StrideInBytes = 16
        });
        using var vertexBuffer1 = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 128,
            Usage = DxBufferUsage.Vertex | DxBufferUsage.CopyDestination,
            StrideInBytes = 16
        });
        using var indexBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 64,
            Usage = DxBufferUsage.Index | DxBufferUsage.CopyDestination
        });
        using var context = device.CreateImmediateContext();

        context.SetVertexBuffer(0, vertexBuffer0);
        context.SetVertexBuffer(1, vertexBuffer1);
        context.SetIndexBuffer(indexBuffer, DxIndexFormat.UInt16);
        context.DrawIndexed(6);

        var command = context.Commands[^1];

        Assert.Equal(ProGpuDirectXCommandKind.DrawIndexed, command.Kind);
        Assert.NotNull(command.VertexBuffers);
        Assert.Same(vertexBuffer0, command.VertexBuffers[0]);
        Assert.Same(vertexBuffer1, command.VertexBuffers[1]);
        Assert.Same(indexBuffer, command.IndexBuffer);
        Assert.Equal(DxIndexFormat.UInt16, command.DrawIndexed!.IndexFormat);
        Assert.Equal(DxIndexFormat.UInt16, command.IndexFormat);
    }

    [Fact]
    public void FlushSubmitsGpuBackedDrawCommands()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(wgpu);
        using var target = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 32,
            Height = 32,
            Format = DxResourceFormat.R8G8B8A8Unorm,
            Usage = DxTextureUsage.RenderTarget | DxTextureUsage.CopySource
        });
        using var vertexShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Vertex,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = SolidTriangleWgsl
        });
        using var pixelShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Pixel,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = SolidTriangleWgsl
        });
        using var pipeline = device.CreateGraphicsPipeline(new DxGraphicsPipelineDescriptor
        {
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            RenderTargetFormat = DxResourceFormat.R8G8B8A8Unorm,
            BlendState = new DxBlendStateDescriptor { EnableBlend = false },
            RasterizerState = new DxRasterizerStateDescriptor { CullMode = DxCullMode.None }
        });
        using var context = device.CreateImmediateContext();

        context.SetRenderTargets(target);
        context.SetViewport(new DxViewport(0, 0, 32, 32));
        context.ClearRenderTarget(target, DxColor.Black);
        context.SetGraphicsPipeline(pipeline);
        context.Draw(3);
        context.Flush();

        Assert.Equal(1ul, context.SubmittedClearCount);
        Assert.Equal(1ul, context.SubmittedDrawCount);
        Assert.Empty(context.Commands);

        var pixels = target.BackendTexture!.ReadPixels();
        var center = ReadRgbaPixel(pixels, 32, 16, 16);
        Assert.True(center.R > 200, $"Expected red center pixel after DirectX draw, actual: {center}");
        Assert.True(center.G < 50, $"Expected low green center pixel after DirectX draw, actual: {center}");
        Assert.True(center.B < 50, $"Expected low blue center pixel after DirectX draw, actual: {center}");
        Assert.True(center.A > 200, $"Expected opaque center pixel after DirectX draw, actual: {center}");
    }

    [Fact]
    public void FlushSubmitsGpuBackedDepthClearAndDepthDrawCommands()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(wgpu);
        using var target = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 32,
            Height = 32,
            Format = DxResourceFormat.R8G8B8A8Unorm,
            Usage = DxTextureUsage.RenderTarget | DxTextureUsage.CopySource
        });
        using var depth = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 32,
            Height = 32,
            Format = DxResourceFormat.D24UnormS8UInt,
            Usage = DxTextureUsage.DepthStencil
        });
        using var vertexShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Vertex,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = SolidTriangleWgsl
        });
        using var pixelShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Pixel,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = SolidTriangleWgsl
        });
        using var pipeline = device.CreateGraphicsPipeline(new DxGraphicsPipelineDescriptor
        {
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            RenderTargetFormat = DxResourceFormat.R8G8B8A8Unorm,
            DepthStencilFormat = DxResourceFormat.D24UnormS8UInt,
            BlendState = new DxBlendStateDescriptor { EnableBlend = false },
            RasterizerState = new DxRasterizerStateDescriptor { CullMode = DxCullMode.None },
            DepthStencilState = new DxDepthStencilStateDescriptor
            {
                DepthEnable = true,
                DepthWriteMask = DxDepthWriteMask.All,
                DepthFunction = DxComparisonFunction.LessEqual
            }
        });
        using var context = device.CreateImmediateContext();

        context.ClearDepthStencil(depth, DxDepthStencilClearFlags.DepthStencil, depth: 1f, stencil: 0);
        context.SetRenderTargets(target, depth);
        context.SetViewport(new DxViewport(0, 0, 32, 32));
        context.SetGraphicsPipeline(pipeline);
        context.Draw(3);
        context.Flush();

        Assert.Equal(1ul, context.SubmittedClearCount);
        Assert.Equal(1ul, context.SubmittedDrawCount);
        Assert.Empty(context.Commands);

        var pixels = target.BackendTexture!.ReadPixels();
        var center = ReadRgbaPixel(pixels, 32, 16, 16);
        Assert.True(center.R > 200, $"Expected red center pixel after DirectX depth draw, actual: {center}");
        Assert.True(center.G < 50, $"Expected low green center pixel after DirectX depth draw, actual: {center}");
        Assert.True(center.B < 50, $"Expected low blue center pixel after DirectX depth draw, actual: {center}");
        Assert.True(center.A > 200, $"Expected opaque center pixel after DirectX depth draw, actual: {center}");
    }

    [Fact]
    public void FlushSubmitsGpuBackedTextureCopyCommands()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(wgpu);
        using var source = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 16,
            Height = 16,
            Format = DxResourceFormat.R8G8B8A8Unorm,
            Usage = DxTextureUsage.RenderTarget | DxTextureUsage.CopySource
        });
        using var destination = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 16,
            Height = 16,
            Format = DxResourceFormat.R8G8B8A8Unorm,
            Usage = DxTextureUsage.CopyDestination | DxTextureUsage.CopySource,
            CpuAccess = DxCpuAccessFlags.Read
        });
        using var context = device.CreateImmediateContext();

        context.ClearRenderTarget(source, new DxColor(0f, 1f, 0f, 1f));
        context.CopyResource(destination, source);
        context.Flush();

        Assert.Equal(1ul, context.SubmittedClearCount);
        Assert.Equal(1ul, context.SubmittedCopyCount);
        Assert.Empty(context.Commands);

        var pixels = destination.ReadPixels();
        var center = ReadRgbaPixel(pixels, 16, 8, 8);
        Assert.True(center.R < 50, $"Expected low red copied pixel, actual: {center}");
        Assert.True(center.G > 200, $"Expected green copied pixel, actual: {center}");
        Assert.True(center.B < 50, $"Expected low blue copied pixel, actual: {center}");
        Assert.True(center.A > 200, $"Expected opaque copied pixel, actual: {center}");
    }

    [Fact]
    public void CpuReadableBuffersSupportMetadataShadowCopies()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var source = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 16,
            Usage = DxBufferUsage.CopySource | DxBufferUsage.CopyDestination,
            CpuAccess = DxCpuAccessFlags.Read
        });
        using var destination = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 16,
            Usage = DxBufferUsage.CopyDestination,
            CpuAccess = DxCpuAccessFlags.Read
        });
        using var context = device.CreateImmediateContext();

        source.Write<uint>([10, 20, 30, 40]);
        context.CopyResource(destination, source);
        context.Flush();

        Assert.Equal([10u, 20u, 30u, 40u], destination.Read<uint>(4));
    }

    [Fact]
    public void CpuReadableBuffersSupportGpuStagingCopies()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(wgpu);
        using var source = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 16,
            Usage = DxBufferUsage.CopySource | DxBufferUsage.CopyDestination
        });
        using var staging = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 16,
            Usage = DxBufferUsage.CopyDestination,
            CpuAccess = DxCpuAccessFlags.Read
        });
        using var context = device.CreateImmediateContext();

        source.Write<uint>([1, 3, 5, 7]);
        context.CopyResource(staging, source);
        context.Flush();

        Assert.Equal(1ul, context.SubmittedCopyCount);
        Assert.Equal([1u, 3u, 5u, 7u], staging.Read<uint>(4));
    }

    [Fact]
    public void FlushSubmitsGpuBackedComputeDispatchCommands()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(wgpu);
        using var computeShader = device.CreateShader(new DxShaderDescriptor
        {
            Stage = DxShaderStage.Compute,
            SourceKind = DxShaderSourceKind.Wgsl,
            Source = ComputeWgsl
        });
        using var pipeline = device.CreateComputePipeline(new DxComputePipelineDescriptor
        {
            ComputeShader = computeShader
        });
        using var context = device.CreateImmediateContext();

        context.SetComputePipeline(pipeline);
        context.Dispatch(1, 1, 1);
        context.Flush();

        Assert.Equal(1ul, context.SubmittedDispatchCount);
        Assert.Empty(context.Commands);
    }

    [Fact]
    public void CanCreateAndBindShaderResourcesAndSamplers()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var texture = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 256,
            Height = 128,
            Usage = DxTextureUsage.ShaderResource | DxTextureUsage.CopySource
        });
        using var structuredBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 4096,
            Usage = DxBufferUsage.ShaderResource | DxBufferUsage.CopyDestination,
            StrideInBytes = 32
        });
        using var textureView = device.CreateShaderResourceView(texture);
        using var bufferView = device.CreateShaderResourceView(
            structuredBuffer,
            new DxShaderResourceViewDescriptor
            {
                Dimension = DxResourceViewDimension.Buffer,
                FirstElement = 0,
                ElementCount = 128,
                ElementStrideInBytes = 32
            });
        using var sampler = device.CreateSamplerState(new DxSamplerDescriptor
        {
            Filter = DxFilter.Anisotropic,
            AddressU = DxTextureAddressMode.Wrap,
            AddressV = DxTextureAddressMode.Clamp,
            MaximumAnisotropy = 4
        });
        using var context = device.CreateImmediateContext();

        context.SetShaderResource(DxShaderStage.Pixel, 0, textureView);
        context.SetShaderResource(DxShaderStage.Vertex, 1, bufferView);
        context.SetSampler(DxShaderStage.Pixel, 0, sampler);

        var pixelSlot = new DxShaderResourceBinding(DxShaderStage.Pixel, 0);
        var vertexSlot = new DxShaderResourceBinding(DxShaderStage.Vertex, 1);

        Assert.False(textureView.HasBackendTextureView);
        Assert.False(sampler.HasBackendSampler);
        Assert.Same(textureView, context.ShaderResourceViews[pixelSlot]);
        Assert.Same(bufferView, context.ShaderResourceViews[vertexSlot]);
        Assert.Same(sampler, context.Samplers[pixelSlot]);
        Assert.Equal(ProGpuDirectXCommandKind.SetShaderResource, context.Commands[0].Kind);
        Assert.Equal(pixelSlot, context.Commands[0].ResourceBinding);
        Assert.Equal(ProGpuDirectXCommandKind.SetSampler, context.Commands[2].Kind);
        Assert.Equal(4, sampler.Descriptor.MaximumAnisotropy);
    }

    [Fact]
    public void DrawCommandsCaptureGraphicsBindingSnapshot()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var vertexConstants = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 256,
            Usage = DxBufferUsage.Constant | DxBufferUsage.CopyDestination
        });
        using var pixelConstants = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 128,
            Usage = DxBufferUsage.Constant | DxBufferUsage.CopyDestination
        });
        using var texture = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 64,
            Height = 64,
            Usage = DxTextureUsage.ShaderResource
        });
        using var textureView = device.CreateShaderResourceView(texture);
        using var sampler = device.CreateSamplerState(new DxSamplerDescriptor());
        using var context = device.CreateImmediateContext();

        context.SetConstantBuffer(DxShaderStage.Vertex, 0, vertexConstants);
        context.SetConstantBuffer(DxShaderStage.Pixel, 1, pixelConstants);
        context.SetShaderResource(DxShaderStage.Pixel, 0, textureView);
        context.SetSampler(DxShaderStage.Pixel, 0, sampler);
        context.Draw(3);

        var snapshot = context.Commands[^1].BindingSnapshot;

        Assert.NotNull(snapshot);
        Assert.False(snapshot.HasBackendBindGroup);
        Assert.Equal(DxShaderStageFlags.AllGraphics, snapshot.StageMask);
        Assert.Equal(4, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.ConstantBuffer &&
            entry.Stage == DxShaderStage.Vertex &&
            entry.Slot == 0 &&
            ReferenceEquals(entry.ConstantBuffer, vertexConstants));
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.ConstantBuffer &&
            entry.Stage == DxShaderStage.Pixel &&
            entry.Slot == 1 &&
            ReferenceEquals(entry.ConstantBuffer, pixelConstants));
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.ShaderResourceView &&
            entry.Stage == DxShaderStage.Pixel &&
            entry.Slot == 0 &&
            ReferenceEquals(entry.ShaderResourceView, textureView));
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.Sampler &&
            entry.Stage == DxShaderStage.Pixel &&
            entry.Slot == 0 &&
            ReferenceEquals(entry.Sampler, sampler));
        Assert.Contains("ConstantBuffer:Vertex:0", snapshot.BindingKey, StringComparison.Ordinal);
        Assert.Contains("ShaderResourceView:Pixel:0", snapshot.BindingKey, StringComparison.Ordinal);
        Assert.Contains("Sampler:Pixel:0", snapshot.BindingKey, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchCommandsCaptureComputeBindingSnapshotWithUavMetadata()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var constants = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 256,
            Usage = DxBufferUsage.Constant | DxBufferUsage.CopyDestination
        });
        using var input = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = DxBufferUsage.Structured | DxBufferUsage.ShaderResource | DxBufferUsage.CopyDestination,
            StrideInBytes = 16
        });
        using var output = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 1024,
            Usage = DxBufferUsage.Structured | DxBufferUsage.UnorderedAccess | DxBufferUsage.CopySource,
            StrideInBytes = 16
        });
        using var inputView = device.CreateShaderResourceView(
            input,
            new DxShaderResourceViewDescriptor
            {
                Dimension = DxResourceViewDimension.Buffer,
                ElementCount = 64,
                ElementStrideInBytes = 16
            });
        using var outputView = device.CreateUnorderedAccessView(
            output,
            new DxUnorderedAccessViewDescriptor
            {
                Dimension = DxResourceViewDimension.Buffer,
                ElementCount = 64,
                ElementStrideInBytes = 16
            });
        using var context = device.CreateImmediateContext();

        context.SetConstantBuffer(DxShaderStage.Compute, 0, constants);
        context.SetShaderResource(DxShaderStage.Compute, 1, inputView);
        context.SetUnorderedAccessView(2, outputView);
        context.Dispatch(4, 2, 1);

        var snapshot = context.Commands[^1].BindingSnapshot;

        Assert.NotNull(snapshot);
        Assert.Equal(DxShaderStageFlags.Compute, snapshot.StageMask);
        Assert.Equal(3, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.ConstantBuffer &&
            entry.Stage == DxShaderStage.Compute &&
            entry.Slot == 0 &&
            ReferenceEquals(entry.ConstantBuffer, constants));
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.ShaderResourceView &&
            entry.Stage == DxShaderStage.Compute &&
            entry.Slot == 1 &&
            ReferenceEquals(entry.ShaderResourceView, inputView));
        Assert.Contains(snapshot.Entries, entry =>
            entry.Kind == ProGpuDirectXBindingKind.UnorderedAccessView &&
            entry.Stage == DxShaderStage.Compute &&
            entry.Slot == 2 &&
            ReferenceEquals(entry.UnorderedAccessView, outputView));
        Assert.Contains("UnorderedAccessView:Compute:2", snapshot.BindingKey, StringComparison.Ordinal);
    }

    [Fact]
    public void CanCreateUnorderedAccessViewsAndRecordCopies()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();
        using var sourceTexture = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 64,
            Height = 64,
            Usage = DxTextureUsage.ShaderResource | DxTextureUsage.CopySource
        });
        using var destinationTexture = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 64,
            Height = 64,
            Usage = DxTextureUsage.UnorderedAccess | DxTextureUsage.CopyDestination | DxTextureUsage.ShaderResource
        });
        using var sourceBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 256,
            Usage = DxBufferUsage.CopySource
        });
        using var destinationBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 512,
            Usage = DxBufferUsage.CopyDestination
        });
        using var uav = device.CreateUnorderedAccessView(destinationTexture);
        using var context = device.CreateImmediateContext();

        context.SetUnorderedAccessView(0, uav);
        context.CopyResource(destinationTexture, sourceTexture);
        context.CopyResource(destinationBuffer, sourceBuffer);

        Assert.False(uav.HasBackendTextureView);
        Assert.Same(uav, context.UnorderedAccessViews[0]);
        Assert.Equal(ProGpuDirectXCommandKind.SetUnorderedAccessView, context.Commands[0].Kind);
        Assert.Equal(ProGpuDirectXCommandKind.CopyTexture, context.Commands[1].Kind);
        Assert.Same(sourceTexture, context.Commands[1].SourceTexture);
        Assert.Same(destinationTexture, context.Commands[1].DestinationTexture);
        Assert.Equal(ProGpuDirectXCommandKind.CopyBuffer, context.Commands[2].Kind);
        Assert.Same(sourceBuffer, context.Commands[2].SourceBuffer);
        Assert.Same(destinationBuffer, context.Commands[2].DestinationBuffer);
    }

    [Fact]
    public void InvalidDescriptorsAreRejected()
    {
        using var device = ProGpuDirectXDevice.CreateMetadataDevice();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.CreateTexture2D(new DxTexture2DDescriptor
            {
                Width = 0,
                Height = 1
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.CreateBuffer(new DxBufferDescriptor
            {
                SizeInBytes = 0
            }));

        Assert.Throws<ArgumentException>(() =>
            device.CreateBuffer(new DxBufferDescriptor
            {
                SizeInBytes = 16,
                Usage = DxBufferUsage.Vertex | DxBufferUsage.CopyDestination,
                CpuAccess = DxCpuAccessFlags.Read
            }));

        Assert.Throws<ArgumentException>(() =>
            device.CreateShader(new DxShaderDescriptor
            {
                Stage = DxShaderStage.Pixel,
                SourceKind = DxShaderSourceKind.Wgsl,
                Source = ""
            }));

        using var renderOnlyTexture = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 16,
            Height = 16,
            Usage = DxTextureUsage.RenderTarget
        });
        Assert.Throws<ArgumentException>(() => device.CreateShaderResourceView(renderOnlyTexture));
        Assert.Throws<InvalidOperationException>(() => renderOnlyTexture.ReadPixels());

        using var shaderBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 128,
            Usage = DxBufferUsage.ShaderResource
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.CreateShaderResourceView(
                shaderBuffer,
                new DxShaderResourceViewDescriptor
                {
                    Dimension = DxResourceViewDimension.Buffer,
                    ElementCount = 0
                }));

        using var copySource = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 16,
            Height = 16,
            Usage = DxTextureUsage.CopySource
        });
        using var mismatchedDestination = device.CreateTexture2D(new DxTexture2DDescriptor
        {
            Width = 32,
            Height = 16,
            Usage = DxTextureUsage.CopyDestination
        });
        using var context = device.CreateImmediateContext();
        Assert.Throws<ArgumentOutOfRangeException>(() => context.CopyResource(mismatchedDestination, copySource));

        Assert.Throws<ArgumentException>(() => context.ClearDepthStencil(renderOnlyTexture));

        using var vertexBuffer = device.CreateBuffer(new DxBufferDescriptor
        {
            SizeInBytes = 128,
            Usage = DxBufferUsage.Vertex
        });
        Assert.Throws<ArgumentException>(() =>
            context.SetConstantBuffer(DxShaderStage.Vertex, 0, vertexBuffer));
    }

    private static (byte R, byte G, byte B, byte A) ReadRgbaPixel(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }
}
