using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkScalarValueApiCompatibilityTests
{
    [Fact]
    public void ScalarAndImageInfoFamiliesExposeNativeParameterNames()
    {
        AssertParameterNames(
            typeof(SKColor).GetConstructor([typeof(byte), typeof(byte), typeof(byte), typeof(byte)]),
            "red",
            "green",
            "blue",
            "alpha");
        AssertParameterNames(
            typeof(SKRotationScaleMatrix).GetMethod(
                nameof(SKRotationScaleMatrix.CreateScale)),
            "s");
        AssertParameterNames(
            typeof(SKSamplingOptions).GetConstructor([typeof(SKFilterMode), typeof(SKMipmapMode)]),
            "filter",
            "mipmap");
        AssertParameterNames(
            typeof(SKSamplingOptions).GetConstructor([typeof(SKCubicResampler)]),
            "resampler");
        AssertParameterNames(
            typeof(SKImageInfo).GetConstructor(
            [
                typeof(int),
                typeof(int),
                typeof(SKColorType),
                typeof(SKAlphaType),
                typeof(SKColorSpace),
            ]),
            "width",
            "height",
            "colorType",
            "alphaType",
            "colorspace");
        AssertParameterNames(
            typeof(SKImageInfo).GetMethod(nameof(SKImageInfo.WithColorType)),
            "newColorType");
        AssertParameterNames(
            typeof(SKImageInfo).GetMethod(nameof(SKImageInfo.WithColorSpace)),
            "newColorSpace");
        AssertParameterNames(
            typeof(SKImageInfo).GetMethod(nameof(SKImageInfo.WithAlphaType)),
            "newAlphaType");
    }

    private static void AssertParameterNames(MethodBase? method, params string[] expected)
    {
        Assert.NotNull(method);
        Assert.Equal(expected, method!.GetParameters().Select(parameter => parameter.Name));
    }
}
