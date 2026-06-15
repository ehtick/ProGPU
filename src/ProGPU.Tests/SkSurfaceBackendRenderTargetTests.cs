using System;
using ProGPU.Backend;
using Silk.NET.WebGPU;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkSurfaceBackendRenderTargetTests
{
    [Fact]
    public void CreateFromProGpuBackendRenderTargetFlushesIntoWrappedTexture()
    {
        using var grContext = GRContext.CreateGl() ?? throw new InvalidOperationException("Failed to create GRContext.");
        using var texture = new GpuTexture(
            grContext.Context,
            4,
            4,
            TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            "SKSurface wrapped render target test");
        using var renderTarget = new GRBackendRenderTarget(4, 4, texture);
        using var surface = SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);

        surface.Canvas.Clear(SKColors.Red);
        surface.Flush();

        var pixels = texture.ReadPixels();
        Assert.Equal(255, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.Equal(0, pixels[2]);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public void CreateFromUnsupportedNativeBackendRenderTargetFailsExplicitly()
    {
        using var grContext = GRContext.CreateGl() ?? throw new InvalidOperationException("Failed to create GRContext.");
        using var renderTarget = new GRBackendRenderTarget(4, 4, 1, 0, new GRGlFramebufferInfo(123, 0));

        var exception = Assert.Throws<NotSupportedException>(
            () => SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888));
        Assert.Contains("GpuTexture", exception.Message);
    }
}
