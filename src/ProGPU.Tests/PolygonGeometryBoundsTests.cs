using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class PolygonGeometryBoundsTests
{
    [Fact]
    public void TryGetBoundsAppliesGeometryAndWorldTransforms()
    {
        Vector2[] points =
        {
            new(0f, 0f),
            new(10f, 0f),
            new(10f, 5f)
        };
        Matrix4x4 geometryTransform = Matrix4x4.CreateScale(2f, 3f, 1f);
        Matrix4x4 worldTransform = Matrix4x4.CreateTranslation(4f, 7f, 0f);

        Assert.True(PolygonGeometryBounds.TryGetBounds(
            points,
            geometryTransform,
            worldTransform,
            strokeThickness: 0f,
            out Vector2 min,
            out Vector2 max));

        Assert.Equal(new Vector2(4f, 7f), min);
        Assert.Equal(new Vector2(24f, 22f), max);
    }

    [Fact]
    public void TryGetBoundsInflatesStrokeByTransformScale()
    {
        Vector2[] points =
        {
            new(0f, 0f),
            new(10f, 0f)
        };
        Matrix4x4 transform = Matrix4x4.CreateScale(2f, 3f, 1f);

        Assert.True(PolygonGeometryBounds.TryGetBounds(
            points,
            transform,
            strokeThickness: 4f,
            out Vector2 min,
            out Vector2 max));

        Assert.Equal(new Vector2(-6f, -6f), min);
        Assert.Equal(new Vector2(26f, 6f), max);
    }

    [Fact]
    public void TryGetBoundsRejectsInvalidPoints()
    {
        Vector2[] points =
        {
            new(0f, 0f),
            new(float.NaN, 1f)
        };

        Assert.False(PolygonGeometryBounds.TryGetBounds(
            points,
            Matrix4x4.Identity,
            strokeThickness: 0f,
            out _,
            out _));
    }
}
