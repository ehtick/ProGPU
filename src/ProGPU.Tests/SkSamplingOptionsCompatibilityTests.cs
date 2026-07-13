using System.Reflection;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkSamplingOptionsCompatibilityTests
{
    [Fact]
    public void PublicContractMatchesNativeReadonlyValueShape()
    {
        var type = typeof(SKSamplingOptions);

        Assert.True(type.IsValueType);
        Assert.True(type.IsAssignableTo(typeof(IEquatable<SKSamplingOptions>)));
        Assert.NotNull(type.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>());
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void DefaultUsesNearestNonMipmappedSampling()
    {
        var sampling = SKSamplingOptions.Default;

        Assert.False(sampling.IsAniso);
        Assert.Equal(0, sampling.MaxAniso);
        Assert.False(sampling.UseCubic);
        Assert.Equal(default, sampling.Cubic);
        Assert.Equal(SKFilterMode.Nearest, sampling.Filter);
        Assert.Equal(SKMipmapMode.None, sampling.Mipmap);
    }

    [Fact]
    public void FilterConstructorsPreserveNativeDiscriminants()
    {
        var filterOnly = new SKSamplingOptions(SKFilterMode.Linear);
        var mipmapped = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest);

        Assert.Equal(SKFilterMode.Linear, filterOnly.Filter);
        Assert.Equal(SKMipmapMode.None, filterOnly.Mipmap);
        Assert.False(filterOnly.UseCubic);
        Assert.False(filterOnly.IsAniso);
        Assert.Equal(SKFilterMode.Linear, mipmapped.Filter);
        Assert.Equal(SKMipmapMode.Nearest, mipmapped.Mipmap);
    }

    [Fact]
    public void CubicConstructorPreservesNativeDiscriminants()
    {
        var cubic = new SKCubicResampler(0.2f, 0.4f);
        var sampling = new SKSamplingOptions(cubic);

        Assert.True(sampling.UseCubic);
        Assert.Equal(cubic, sampling.Cubic);
        Assert.False(sampling.IsAniso);
        Assert.Equal(SKFilterMode.Nearest, sampling.Filter);
        Assert.Equal(SKMipmapMode.None, sampling.Mipmap);
    }

    [Fact]
    public void AnisotropicConstructorClampsOnlyNativeLowerBound()
    {
        Assert.Equal(1, new SKSamplingOptions(int.MinValue).MaxAniso);
        Assert.Equal(1, new SKSamplingOptions(0).MaxAniso);
        Assert.Equal(8, new SKSamplingOptions(8).MaxAniso);
        Assert.Equal(int.MaxValue, new SKSamplingOptions(int.MaxValue).MaxAniso);
        Assert.True(new SKSamplingOptions(1).IsAniso);
    }

    [Fact]
    public void EqualityAndHashingIncludeEveryDiscriminant()
    {
        var left = new SKSamplingOptions(new SKCubicResampler(-0f, 0.4f));
        var right = new SKSamplingOptions(new SKCubicResampler(0f, 0.4f));

        Assert.True(left == right);
        Assert.False(left != right);
        Assert.True(left.Equals((object)right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new SKSamplingOptions(SKFilterMode.Linear));
        Assert.NotEqual(new SKSamplingOptions(4), new SKSamplingOptions(8));
    }

    [Fact]
    public void RendererMapsAndCachesEffectiveAnisotropy()
    {
        var mapSampling = typeof(SKCanvas).GetMethod(
            "MapSampling",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var mapMaxAnisotropy = typeof(SKCanvas).GetMethod(
            "MapMaxAnisotropy",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var requested = new SKSamplingOptions(int.MaxValue);

        Assert.Equal(
            TextureSamplingMode.LinearMipmap,
            mapSampling.Invoke(null, new object[] { requested }));
        Assert.Equal((byte)16, mapMaxAnisotropy.Invoke(null, new object[] { requested }));
        Assert.Equal(
            (byte)1,
            mapMaxAnisotropy.Invoke(null, new object[] { SKSamplingOptions.Default }));

        var fourTap = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.LinearMipmap,
            maxAnisotropy: 4);
        var eightTap = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.LinearMipmap,
            maxAnisotropy: 8);
        Assert.NotEqual(fourTap, eightTap);

        var clampedHigh = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.LinearMipmap,
            maxAnisotropy: byte.MaxValue);
        var sixteenTap = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.LinearMipmap,
            maxAnisotropy: 16);
        Assert.Equal(sixteenTap, clampedHigh);

        var ordinaryZero = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.Linear,
            maxAnisotropy: 0);
        var ordinarySixteen = new Compositor.TextureCacheKey(
            textureId: 1,
            generation: 2,
            isOffscreen: false,
            TextureSamplingMode.Linear,
            maxAnisotropy: 16);
        Assert.Equal(ordinaryZero, ordinarySixteen);
    }
}
