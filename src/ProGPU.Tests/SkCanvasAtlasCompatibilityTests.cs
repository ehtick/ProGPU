using System.Numerics;
using System.Reflection;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasAtlasCompatibilityTests
{
    [Fact]
    public void AtlasSurfaceContainsNativeSamplingAndLegacyOverloads()
    {
        var methods = typeof(SKCanvas)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => method.Name == nameof(SKCanvas.DrawAtlas))
            .ToArray();

        Assert.Equal(6, methods.Length);
        Assert.Equal(3, methods.Count(static method => method.GetCustomAttribute<ObsoleteAttribute>() != null));
        Assert.Equal(3, methods.Count(static method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(SKSamplingOptions))));
    }

    [Fact]
    public void AtlasLayoutAppliesRotationScaleTransformsAndComputesBounds()
    {
        var patches = SKAtlasLayout.CreatePatches(
            [new SKRect(10f, 20f, 14f, 26f)],
            [new SKRotationScaleMatrix(0f, 2f, 30f, 40f)],
            colors: null,
            SKBlendMode.Dst,
            colorFilter: null,
            out var bounds);

        var patch = Assert.Single(patches);
        Assert.Equal(TexturePatchKind.Texture, patch.Kind);
        Assert.Equal(new Rect(10f, 20f, 4f, 6f), patch.Source);
        Assert.Equal(new Rect(0f, 0f, 4f, 6f), patch.Destination);
        Assert.True(patch.HasDestinationTransform);
        Assert.Equal(new Matrix3x2(0f, 2f, -2f, 0f, 30f, 40f), patch.DestinationTransform);
        Assert.Equal(new SKRect(18f, 40f, 30f, 48f), bounds);
    }

    [Fact]
    public void AtlasLayoutFiltersColorsAndRetainsBlendMode()
    {
        using var filter = SKColorFilter.CreateBlendMode(SKColors.Blue, SKBlendMode.Src);
        var patches = SKAtlasLayout.CreatePatches(
            [new SKRect(0f, 0f, 8f, 8f)],
            [SKRotationScaleMatrix.CreateTranslation(3f, 5f)],
            [SKColors.Red],
            SKBlendMode.SrcIn,
            filter,
            out var bounds);

        var patch = Assert.Single(patches);
        Assert.Equal(TexturePatchKind.AtlasColor, patch.Kind);
        Assert.Equal(VertexColorBlendMode.SrcIn, patch.ColorBlendMode);
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), patch.Color);
        Assert.Equal(new SKRect(3f, 5f, 11f, 13f), bounds);
    }

    [Fact]
    public void AtlasLayoutSkipsEmptySpritesWithoutChangingOrder()
    {
        var patches = SKAtlasLayout.CreatePatches(
            [
                SKRect.Empty,
                new SKRect(2f, 3f, 6f, 8f),
                new SKRect(4f, 4f, 4f, 9f)
            ],
            [
                SKRotationScaleMatrix.Identity,
                SKRotationScaleMatrix.CreateTranslation(10f, 20f),
                SKRotationScaleMatrix.Identity
            ],
            colors: null,
            SKBlendMode.Dst,
            colorFilter: null,
            out var bounds);

        var patch = Assert.Single(patches);
        Assert.Equal(new Rect(2f, 3f, 4f, 5f), patch.Source);
        Assert.Equal(new SKRect(10f, 20f, 14f, 25f), bounds);
    }
}
