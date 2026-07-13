using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkSurfacePropertiesCompatibilityTests
{
    [Fact]
    public void SurfaceFlagsMatchNativeContract()
    {
        Assert.NotNull(typeof(SKSurfacePropsFlags).GetCustomAttribute<FlagsAttribute>());
        Assert.Equal(0, (int)SKSurfacePropsFlags.None);
        Assert.Equal(1, (int)SKSurfacePropsFlags.UseDeviceIndependentFonts);
    }

    [Fact]
    public void PixelGeometryConstructorUsesNoFlags()
    {
        using var properties = new SKSurfaceProperties(SKPixelGeometry.BgrVertical);

        Assert.Equal(SKSurfacePropsFlags.None, properties.Flags);
        Assert.Equal(SKPixelGeometry.BgrVertical, properties.PixelGeometry);
        Assert.False(properties.IsUseDeviceIndependentFonts);
    }

    [Fact]
    public void UnsignedConstructorPreservesUnknownFlagBits()
    {
        using var properties = new SKSurfaceProperties(0x80000001u, (SKPixelGeometry)37);

        Assert.Equal(unchecked((SKSurfacePropsFlags)0x80000001u), properties.Flags);
        Assert.Equal((SKPixelGeometry)37, properties.PixelGeometry);
        Assert.True(properties.IsUseDeviceIndependentFonts);
    }

    [Fact]
    public void EnumConstructorPreservesNativeValues()
    {
        using var properties = new SKSurfaceProperties(
            SKSurfacePropsFlags.UseDeviceIndependentFonts,
            SKPixelGeometry.RgbHorizontal);

        Assert.Equal(SKSurfacePropsFlags.UseDeviceIndependentFonts, properties.Flags);
        Assert.Equal(SKPixelGeometry.RgbHorizontal, properties.PixelGeometry);
        Assert.True(properties.IsUseDeviceIndependentFonts);
    }

    [Fact]
    public void OwnedSurfacePropertiesUseInheritedLifetime()
    {
        var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);

        Assert.NotEqual(IntPtr.Zero, properties.Handle);
        properties.Dispose();
        Assert.Equal(IntPtr.Zero, properties.Handle);
        Assert.Null(typeof(SKSurfaceProperties).GetMethod(
            nameof(IDisposable.Dispose),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }
}
