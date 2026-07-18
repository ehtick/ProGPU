using System.Numerics;
using ProGPU.Compute;
using Xunit;

namespace ProGPU.Tests;

public sealed class GpuSceneVisibilityTests
{
    [Fact]
    public void CpuCompactionPreservesPainterOrderAcrossTransformsAndClips()
    {
        Matrix4x4[] transforms =
        [
            Matrix4x4.Identity,
            Matrix4x4.CreateTranslation(100f, 0f, 0f),
            Matrix4x4.CreateRotationZ(MathF.PI / 4f) * Matrix4x4.CreateTranslation(20f, 20f, 0f)
        ];
        GpuSceneDrawMetadata[] draws =
        [
            Draw(0, 10, 1, new Vector4(0f, 0f, 10f, 10f)),
            Draw(1, 11, 2, new Vector4(0f, 0f, 10f, 10f)),
            Draw(2, 12, 1, new Vector4(-2f, -2f, 2f, 2f)),
            Draw(
                0,
                13,
                3,
                new Vector4(5f, 5f, 15f, 15f),
                new Vector4(20f, 20f, 30f, 30f))
        ];
        uint[] sources = new uint[draws.Length];
        uint[] materials = new uint[draws.Length];

        int count = GpuSceneVisibility.CompactVisibleCpu(
            draws,
            transforms,
            new Vector4(0f, 0f, 50f, 50f),
            sources,
            materials);

        Assert.Equal(2, count);
        Assert.Equal([10u, 12u], sources.AsSpan(0, count).ToArray());
        Assert.Equal([1u, 1u], materials.AsSpan(0, count).ToArray());
    }

    [Fact]
    public void CpuCompactionRejectsInvalidTransformsAndNonFiniteBounds()
    {
        GpuSceneDrawMetadata[] draws =
        [
            Draw(4, 1, 0, new Vector4(0f, 0f, 1f, 1f)),
            Draw(0, 2, 0, new Vector4(float.NaN, 0f, 1f, 1f)),
            Draw(0, 3, 0, new Vector4(2f, 2f, 2f, 3f))
        ];
        uint[] sources = new uint[draws.Length];
        uint[] materials = new uint[draws.Length];

        int count = GpuSceneVisibility.CompactVisibleCpu(
            draws,
            [Matrix4x4.Identity],
            new Vector4(0f, 0f, 100f, 100f),
            sources,
            materials);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CpuCompactionComposesRetainedInstanceWithRootCameraTransform()
    {
        GpuSceneDrawMetadata[] draws =
        [
            Draw(0, 20, 1, new Vector4(0f, 0f, 10f, 10f)),
            Draw(1, 21, 1, new Vector4(0f, 0f, 10f, 10f))
        ];
        Matrix4x4[] instances =
        [
            Matrix4x4.CreateTranslation(10f, 0f, 0f),
            Matrix4x4.CreateTranslation(100f, 0f, 0f)
        ];
        uint[] sources = new uint[draws.Length];
        uint[] materials = new uint[draws.Length];

        int count = GpuSceneVisibility.CompactVisibleCpu(
            draws,
            instances,
            new Vector4(0f, 0f, 50f, 50f),
            Matrix4x4.CreateTranslation(5f, 10f, 0f),
            sources,
            materials);

        Assert.Equal(1, count);
        Assert.Equal(20u, sources[0]);
    }

    [Fact]
    public void CpuCompactionRequiresFullCapacityOutputs()
    {
        GpuSceneDrawMetadata[] draws =
        [
            Draw(0, 1, 0, new Vector4(0f, 0f, 1f, 1f)),
            Draw(0, 2, 0, new Vector4(0f, 0f, 1f, 1f))
        ];

        Assert.Throws<ArgumentException>(() => GpuSceneVisibility.CompactVisibleCpu(
            draws,
            [Matrix4x4.Identity],
            new Vector4(0f, 0f, 10f, 10f),
            new uint[1],
            new uint[2]));
    }

    private static GpuSceneDrawMetadata Draw(
        uint transformIndex,
        uint sourceIndex,
        uint materialKey,
        Vector4 bounds,
        Vector4? clip = null) => new()
    {
        Bounds = bounds,
        ClipBounds = clip ?? default,
        TransformIndex = transformIndex,
        SourceIndex = sourceIndex,
        MaterialKey = materialKey,
        Flags = clip.HasValue ? GpuSceneVisibility.HasClipFlag : 0u
    };
}
