using ProGPU.Backend;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using System.Reflection;
using Xunit;

namespace ProGPU.Tests;

public sealed class WgpuContextTests
{
    [Fact]
    public void VsyncOffUsesImmediateWhenSurfaceAdvertisesIt()
    {
        var selected = WgpuContext.ChoosePresentMode(
            vsync: false,
            [PresentMode.Fifo, PresentMode.Immediate]);

        Assert.Equal(PresentMode.Immediate, selected);
    }

    [Fact]
    public void VsyncOffFallsBackToAdvertisedPresentModeWhenImmediateIsAbsent()
    {
        var selected = WgpuContext.ChoosePresentMode(
            vsync: false,
            [PresentMode.Fifo]);

        Assert.Equal(PresentMode.Fifo, selected);
    }

    [Fact]
    public void VsyncOnPrefersFifoWhenSurfaceAdvertisesIt()
    {
        var selected = WgpuContext.ChoosePresentMode(
            vsync: true,
            [PresentMode.Immediate, PresentMode.Fifo]);

        Assert.Equal(PresentMode.Fifo, selected);
    }

    [Theory]
    [InlineData(15, 16u, 16u, 4u, true)]
    [InlineData(16, 16u, 16u, 4u, false)]
    [InlineData(16, 17u, 17u, 4u, true)]
    [InlineData(16, 17u, 16u, 4u, false)]
    [InlineData(16, 17u, 17u, 3u, false)]
    public void WpfShaderEffectMaskBindingFollowsDeviceLimits(
        int activeSamplerRegisterCount,
        uint maxSampledTexturesPerShaderStage,
        uint maxSamplersPerShaderStage,
        uint maxBindGroups,
        bool expected)
    {
        var canBind = WgpuContext.CanBindWpfShaderEffectMask(
            activeSamplerRegisterCount,
            maxSampledTexturesPerShaderStage,
            maxSamplersPerShaderStage,
            maxBindGroups);

        Assert.Equal(expected, canBind);
    }

    [Fact]
    public void PendingResourceSnapshotDropsDuplicateAndZeroPointers()
    {
        var method = typeof(WgpuContext).GetMethod(
            "SnapshotPendingResourcePointers",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var pending = new List<IntPtr>
        {
            new(1),
            new(2),
            IntPtr.Zero,
            new(1),
            new(3),
            new(2)
        };

        var snapshot = Assert.IsType<IntPtr[]>(method.Invoke(null, new object[] { pending }));

        Assert.Equal(new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3) }, snapshot);
    }

    [Fact]
    public unsafe void VerifyShaderModuleFailsClosedWhenNativeCompilationInfoIsUnavailable()
    {
        using var context = new WgpuContext();
        context.Initialize(null);

        var codePtr = SilkMarshal.StringToPtr(
            """
            @vertex
            fn vs_main() -> @builtin(position) vec4<f32> {
                return vec4<f32>(0.0, 0.0, 0.0, 1.0);
            }

            @fragment
            fn fs_main() -> @location(0) vec4<f32> {
                return vec4<f32>(missing_symbol, 0.0, 0.0, 1.0);
            }
            """);
        var labelPtr = SilkMarshal.StringToPtr("InvalidWgslVerificationTest");
        ShaderModule* module = null;

        try
        {
            var wgslDesc = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct
                {
                    Next = null,
                    SType = SType.ShaderModuleWgslDescriptor
                },
                Code = (byte*)codePtr
            };

            var desc = new ShaderModuleDescriptor
            {
                NextInChain = (ChainedStruct*)&wgslDesc,
                Label = (byte*)labelPtr
            };

            module = context.Wgpu.DeviceCreateShaderModule(context.Device, &desc);
            Assert.True(module != null, "Expected WebGPU to create an invalid shader module so verification can exercise the unsupported-diagnostics path.");

            Assert.Equal(
                ShaderModuleVerificationStatus.Unavailable,
                context.GetShaderModuleVerificationStatus(module, out string errors));
            Assert.Contains("verification is unavailable", errors, StringComparison.Ordinal);
            Assert.False(context.VerifyShaderModule(module, out errors));
            Assert.Contains("verification is unavailable", errors, StringComparison.Ordinal);
        }
        finally
        {
            if (module != null)
            {
                context.Wgpu.ShaderModuleRelease(module);
            }

            SilkMarshal.Free(codePtr);
            SilkMarshal.Free(labelPtr);
        }
    }
}
