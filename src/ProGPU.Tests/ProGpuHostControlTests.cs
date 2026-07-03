using System.IO;
using System.Reflection;
using Avalonia.Rendering.Composition;
using ProGPU.Avalonia;
using ProGPU.Backend;
using Xunit;

namespace ProGPU.Tests;

public class ProGpuHostControlTests
{
    [Fact]
    public void ZeroCopyCompositionIsOptInByDefault()
    {
        var control = new ProGpuHostControl();

        Assert.False(control.EnableZeroCopy);
    }

    [Fact]
    public void HostControlCanFallbackWhenSharedImageImportFails()
    {
        var resizeMethod = typeof(ProGpuHostControl).GetMethod(
            "ResizeSharedResources",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var fallbackMethod = typeof(ProGpuHostControl).GetMethod(
            "TryUseCustomVisualFallback",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resizeMethod);
        Assert.Equal(typeof(bool), resizeMethod.ReturnType);
        Assert.NotNull(fallbackMethod);
        Assert.Equal(typeof(bool), fallbackMethod.ReturnType);
    }

    [Fact]
    public void HostControlRechecksZeroCopyFrameAfterAsyncMap()
    {
        var guardMethod = typeof(ProGpuHostControl).GetMethod(
            "IsCurrentZeroCopyFrame",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var unmapMethod = typeof(ProGpuHostControl).GetMethod(
            "TryUnmapStagingBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var copyMethod = typeof(ProGpuHostControl).GetMethod(
            "CopyMappedToSharedTexture",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(guardMethod);
        Assert.Equal(typeof(bool), guardMethod.ReturnType);

        var guardParameters = guardMethod.GetParameters();
        Assert.Equal(6, guardParameters.Length);
        Assert.Equal("swapchainImages", guardParameters[0].Name);
        Assert.Equal("imageIndex", guardParameters[1].Name);
        Assert.Equal("image", guardParameters[2].Name);
        Assert.Equal("importedImage", guardParameters[3].Name);
        Assert.Equal(typeof(ICompositionImportedGpuImage), guardParameters[3].ParameterType);
        Assert.Equal("drawingSurface", guardParameters[4].Name);
        Assert.Equal(typeof(CompositionDrawingSurface), guardParameters[4].ParameterType);
        Assert.Equal("context", guardParameters[5].Name);
        Assert.Equal(typeof(WgpuContext), guardParameters[5].ParameterType);

        Assert.NotNull(unmapMethod);
        var unmapParameters = unmapMethod.GetParameters();
        Assert.Equal(2, unmapParameters.Length);
        Assert.Equal(typeof(WgpuContext), unmapParameters[0].ParameterType);

        Assert.NotNull(copyMethod);
        var copyParameters = copyMethod.GetParameters();
        Assert.Equal(4, copyParameters.Length);
        Assert.Equal(typeof(WgpuContext), copyParameters[0].ParameterType);
    }

    [Fact]
    public void HostControlRechecksGraphicsStateBeforeCreatingFallbackVisual()
    {
        string source = File.ReadAllText(FindProGpuHostControlSource()).Replace("\r\n", "\n");

        Assert.Contains("var expectedContext = _wgpuContext;", source, StringComparison.Ordinal);
        Assert.Contains("var expectedCompositor = _compositor;", source, StringComparison.Ordinal);
        Assert.Contains("IsCompositionSurfaceSetupCurrent(compositor, expectedContext, expectedCompositor)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_wgpuContext, context)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_compositor, compositor)", source, StringComparison.Ordinal);
        Assert.Contains("_wgpuContext == null || _compositor == null || _wgpuContext.IsDisposed", source, StringComparison.Ordinal);
        Assert.Contains("!_customVisualHandler.Matches(_wgpuContext, _compositor)", source, StringComparison.Ordinal);
        Assert.Contains("internal bool Matches(WgpuContext context, WinuiCompositor compositor)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseSharedResourcesIgnoresPartiallyInitializedSwapchainImages()
    {
        var control = new ProGpuHostControl();
        var swapchainImageType = typeof(ProGpuHostControl).GetNestedType(
            "SwapchainImage",
            BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(ProGpuHostControl).FullName, "SwapchainImage");
        var field = typeof(ProGpuHostControl).GetField(
            "_swapchainImages",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ProGpuHostControl).FullName, "_swapchainImages");
        var releaseMethod = typeof(ProGpuHostControl).GetMethod(
            "ReleaseSharedResources",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ProGpuHostControl).FullName, "ReleaseSharedResources");

        field.SetValue(control, Array.CreateInstance(swapchainImageType, 2));

        releaseMethod.Invoke(control, null);

        Assert.Null(field.GetValue(control));
    }

    [Fact]
    public void SwapchainImageDisposesStagingBufferThroughDeferredQueue()
    {
        var context = new WgpuContext();
        var swapchainImageType = typeof(ProGpuHostControl).GetNestedType(
            "SwapchainImage",
            BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(ProGpuHostControl).FullName, "SwapchainImage");
        var stagingBufferField = swapchainImageType.GetField(
            "StagingBuffer",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(swapchainImageType.FullName, "StagingBuffer");
        var stagingBufferSizeField = swapchainImageType.GetField(
            "StagingBufferSize",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(swapchainImageType.FullName, "StagingBufferSize");
        var bytesPerRowField = swapchainImageType.GetField(
            "BytesPerRow",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException(swapchainImageType.FullName, "BytesPerRow");
        var image = Activator.CreateInstance(
            swapchainImageType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [context],
            culture: null)
            ?? throw new InvalidOperationException("Expected SwapchainImage instance.");
        var stagingBuffer = new IntPtr(0x1234);

        stagingBufferField.SetValue(image, stagingBuffer);
        stagingBufferSizeField.SetValue(image, 256u);
        bytesPerRowField.SetValue(image, 64u);

        try
        {
            ((IDisposable)image).Dispose();

            Assert.Contains(stagingBuffer, context.PendingBuffers);
            Assert.Equal(IntPtr.Zero, stagingBufferField.GetValue(image));
            Assert.Equal(0u, stagingBufferSizeField.GetValue(image));
            Assert.Equal(0u, bytesPerRowField.GetValue(image));
        }
        finally
        {
            context.PendingBuffers.Clear();
        }
    }

    [Fact]
    public void SwapchainImageDisposeUnmapsActiveStagingBufferBeforeQueueingDisposal()
    {
        string source = File.ReadAllText(FindProGpuHostControlSource()).Replace("\r\n", "\n");

        int unmapIndex = source.IndexOf(
            "_context.Wgpu.BufferUnmap((GpuBuffer*)StagingBuffer)",
            StringComparison.Ordinal);
        int queueIndex = source.IndexOf(
            "_context.QueueBufferDisposal(StagingBuffer)",
            StringComparison.Ordinal);

        Assert.Contains("public bool IsStagingBufferMapActive;", source, StringComparison.Ordinal);
        Assert.True(unmapIndex >= 0, "SwapchainImage.Dispose must unmap an active staging-buffer map.");
        Assert.True(queueIndex >= 0, "SwapchainImage.Dispose must queue staging-buffer disposal.");
        Assert.True(unmapIndex < queueIndex, "Mapped staging buffers must be unmapped before disposal is queued.");
        Assert.Contains("image.IsStagingBufferMapActive = true;", source, StringComparison.Ordinal);
        Assert.Contains("!image.IsStagingBufferMapActive", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaHostStagingBuffersUseDeferredDisposalQueue()
    {
        string source = File.ReadAllText(FindProGpuHostControlSource()).Replace("\r\n", "\n");

        Assert.Contains("_context.QueueBufferDisposal(StagingBuffer)", source, StringComparison.Ordinal);
        Assert.Contains("_wgpuContext.QueueBufferDisposal((IntPtr)_stagingBuffer)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wgpu.BufferDestroy((GpuBuffer*)StagingBuffer)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wgpu.BufferRelease((GpuBuffer*)StagingBuffer)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wgpu.BufferDestroy(_stagingBuffer)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wgpu.BufferRelease(_stagingBuffer)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaCustomVisualFallbackUnmapsMappedStagingBuffers()
    {
        string source = File.ReadAllText(FindProGpuHostControlSource()).Replace("\r\n", "\n");

        Assert.Contains("private bool _isStagingBufferMapActive;", source, StringComparison.Ordinal);
        Assert.Contains("private BufferMapAsyncStatus _lastMapStatus = BufferMapAsyncStatus.ValidationError;", source, StringComparison.Ordinal);
        Assert.Contains("_lastMapStatus = status;", source, StringComparison.Ordinal);
        Assert.Contains("if (_lastMapStatus == BufferMapAsyncStatus.Success)", source, StringComparison.Ordinal);
        Assert.Contains("finally\n                {\n                    UnmapActiveStagingBuffer();\n                }", source, StringComparison.Ordinal);
        Assert.Contains("_wgpuContext.Wgpu.BufferUnmap(_stagingBuffer);", source, StringComparison.Ordinal);

        int disposalHelperIndex = source.IndexOf(
            "private void QueueStagingBufferDisposal()",
            StringComparison.Ordinal);
        int unmapCallIndex = source.IndexOf(
            "UnmapActiveStagingBuffer();",
            disposalHelperIndex,
            StringComparison.Ordinal);
        int queueDisposalIndex = source.IndexOf(
            "_wgpuContext.QueueBufferDisposal((IntPtr)_stagingBuffer)",
            disposalHelperIndex,
            StringComparison.Ordinal);

        Assert.True(disposalHelperIndex >= 0, "Expected staging-buffer disposal helper.");
        Assert.True(unmapCallIndex > disposalHelperIndex, "Expected disposal helper to unmap active staging buffer.");
        Assert.True(queueDisposalIndex > unmapCallIndex, "Mapped staging buffers must be unmapped before queued disposal.");
    }

    private static string FindProGpuHostControlSource()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine(directory.FullName, "ProGPU.Avalonia", "ProGpuHostControl.cs"),
                         Path.Combine(directory.FullName, "src", "ProGPU.Avalonia", "ProGpuHostControl.cs")
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not locate ProGPU.Avalonia ProGpuHostControl.cs.");
    }
}
