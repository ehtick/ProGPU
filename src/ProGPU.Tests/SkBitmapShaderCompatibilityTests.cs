using System.Reflection;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkBitmapShaderCompatibilityTests
{
    [Fact]
    public void BitmapShaderOverloadsAndObsoleteContractsMatchSkia()
    {
        var methods = typeof(SKBitmap)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == nameof(SKBitmap.ToShader))
            .ToArray();

        Assert.Equal(7, methods.Length);
        Assert.Contains(methods, static method => method.GetParameters().Length == 0);
        Assert.Contains(methods, static method => ParametersAre(
            method,
            typeof(SKShaderTileMode),
            typeof(SKShaderTileMode),
            typeof(SKSamplingOptions),
            typeof(SKMatrix)));

        var legacyQuality = typeof(SKBitmap).Assembly.GetType("SkiaSharp.SKFilterQuality", throwOnError: true)!;
        var legacy = methods.Single(method => ParametersAre(
            method,
            typeof(SKShaderTileMode),
            typeof(SKShaderTileMode),
            legacyQuality));
        var obsolete = legacy.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.True(obsolete.IsError);
    }

    [Fact]
    public void ImageShaderRetainsSamplingAndMapsCustomCubicCoefficients()
    {
        var sampling = new SKSamplingOptions(new SKCubicResampler(0.2f, 0.4f));
        var shader = new SKShader.ImageShaderData(
            null!,
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Mirror,
            SKMatrix.CreateTranslation(3f, 4f),
            sampling);

        Assert.Equal(SKShaderTileMode.Repeat, shader.TileModeX);
        Assert.Equal(SKShaderTileMode.Mirror, shader.TileModeY);
        Assert.True(shader.Sampling.UseCubic);
        Assert.Equal(0.2f, shader.Sampling.Cubic.B);
        Assert.Equal(0.4f, shader.Sampling.Cubic.C);

        var mapSampling = typeof(SKCanvas).GetMethod(
            "MapSampling",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var mapCubic = typeof(SKCanvas).GetMethod(
            "MapCubicSampling",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(TextureSamplingMode.Cubic, mapSampling.Invoke(null, new object[] { sampling }));
        var coefficients = Assert.IsType<System.Numerics.Vector2>(
            mapCubic.Invoke(null, new object[] { sampling }));
        Assert.Equal(0.2f, coefficients.X);
        Assert.Equal(0.4f, coefficients.Y);
    }

    private static bool ParametersAre(MethodInfo method, params Type[] expected) =>
        method.GetParameters().Select(static parameter => parameter.ParameterType).SequenceEqual(expected);
}
