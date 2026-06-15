using System;
using ProGPU.Backend;

namespace SkiaSharp;

public class GRContextOptions
{
    public bool AvoidStencilBuffers { get; set; }
}

public enum GRSurfaceOrigin
{
    TopLeft = 0,
    BottomLeft = 1,
}

public struct GRGlFramebufferInfo
{
    public uint FramebufferId { get; set; }
    public uint Format { get; set; }

    public GRGlFramebufferInfo(uint framebufferId, uint format)
    {
        FramebufferId = framebufferId;
        Format = format;
    }
}

public struct GRMtlTextureInfo
{
    public IntPtr Texture { get; set; }

    public GRMtlTextureInfo(IntPtr texture)
    {
        Texture = texture;
    }
}

public struct GRVkAlloc
{
    public ulong Memory { get; set; }
    public ulong Size { get; set; }
    public uint Flags { get; set; }
}

public struct GRVkImageInfo
{
    public uint CurrentQueueFamily { get; set; }
    public uint Format { get; set; }
    public ulong Image { get; set; }
    public uint ImageLayout { get; set; }
    public uint ImageTiling { get; set; }
    public uint ImageUsageFlags { get; set; }
    public uint LevelCount { get; set; }
    public uint SampleCount { get; set; }
    public bool Protected { get; set; }
    public GRVkAlloc Alloc { get; set; }
}

public delegate IntPtr GRVkGetProcDelegate(string name, IntPtr instance, IntPtr device);

public class GRVkBackendContext
{
    public IntPtr VkInstance { get; set; }
    public IntPtr VkPhysicalDevice { get; set; }
    public IntPtr VkDevice { get; set; }
    public IntPtr VkQueue { get; set; }
    public uint GraphicsQueueIndex { get; set; }
    public GRVkGetProcDelegate? GetProcedureAddress { get; set; }
}

public class GRMtlBackendContext
{
    public IntPtr DeviceHandle { get; set; }
    public IntPtr QueueHandle { get; set; }
}

public class GRGlInterface : IDisposable
{
    public static GRGlInterface Create() => new();
    public static GRGlInterface CreateOpenGl(Func<string, IntPtr> getProcAddress) => new();
    public static GRGlInterface CreateGles(Func<string, IntPtr> getProcAddress) => new();
    public void Dispose() { }
}

public class GRBackendRenderTarget : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int SampleCount { get; }
    public int StencilBits { get; }
    public GpuTexture? BackendTexture { get; }
    
    public GRGlFramebufferInfo GlFramebufferInfo { get; }
    public GRMtlTextureInfo MtlTextureInfo { get; }
    public GRVkImageInfo VkImageInfo { get; }

    public GRBackendRenderTarget(int width, int height, GpuTexture texture)
        : this(width, height, (int)texture.SampleCount, texture)
    {
    }

    public GRBackendRenderTarget(int width, int height, int sampleCount, GpuTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render target width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Render target height must be positive.");
        }

        if (texture.Width != (uint)width || texture.Height != (uint)height)
        {
            throw new ArgumentException("Backend texture dimensions must match the render target dimensions.", nameof(texture));
        }

        Width = width;
        Height = height;
        SampleCount = sampleCount;
        BackendTexture = texture;
    }

    public GRBackendRenderTarget(int width, int height, int sampleCount, int stencilBits, GRGlFramebufferInfo glInfo)
    {
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        StencilBits = stencilBits;
        GlFramebufferInfo = glInfo;
    }

    public GRBackendRenderTarget(int width, int height, int sampleCount, GRVkImageInfo vkImageInfo)
    {
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        VkImageInfo = vkImageInfo;
    }

    public GRBackendRenderTarget(int width, int height, GRMtlTextureInfo mtlTextureInfo)
    {
        Width = width;
        Height = height;
        SampleCount = 1;
        MtlTextureInfo = mtlTextureInfo;
    }

    public GRBackendRenderTarget(int width, int height, int sampleCount, GRMtlTextureInfo mtlTextureInfo)
    {
        Width = width;
        Height = height;
        SampleCount = sampleCount;
        MtlTextureInfo = mtlTextureInfo;
    }

    public void Dispose() { }
}

public class GRContext : IDisposable
{
    public WgpuContext Context { get; }

    public GRContext(WgpuContext context)
    {
        Context = context;
    }

    public static GRContext? CreateGl(object? interfaceObj = null, GRContextOptions? options = null)
    {
        return new GRContext(SKContextHelper.GetContext());
    }

    public static GRContext? CreateMetal(object? backendContext, GRContextOptions? options = null)
    {
        return new GRContext(SKContextHelper.GetContext());
    }

    public static GRContext? CreateVulkan(object? backendContext, GRContextOptions? options = null)
    {
        return new GRContext(SKContextHelper.GetContext());
    }

    public void Flush(bool submit = true, bool finish = false)
    {
        Context.WaitIdle();
    }

    public void ResetContext(uint flags = 0)
    {
        // No-op
    }

    public void AbandonContext()
    {
        // No-op
    }

    public void SetResourceCacheLimit(long maxResourceBytes)
    {
        // No-op
    }

    public int GetMaxSurfaceSampleCount(SKColorType colorType)
    {
        return 1;
    }

    public void Dispose()
    {
        // No-op
    }
}
