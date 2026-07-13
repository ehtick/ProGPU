using System.Numerics;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkVerticesCompatibilityTests
{
    [Fact]
    public void VerticesCopyInputAndValidateParallelArrays()
    {
        Assert.Equal(typeof(SKObject), typeof(SKVertices).BaseType);
        Assert.Throws<ArgumentNullException>(() =>
            SKVertices.CreateCopy(SKVertexMode.Triangles, null!, null));
        Assert.Throws<ArgumentException>(() =>
            SKVertices.CreateCopy(
                SKVertexMode.Triangles,
                new SKPoint[3],
                new SKPoint[2],
                null));
        Assert.Throws<ArgumentException>(() =>
            SKVertices.CreateCopy(
                SKVertexMode.Triangles,
                new SKPoint[3],
                null,
                new SKColor[2]));
        using var invalidIndices = SKVertices.CreateCopy(
            SKVertexMode.Triangles,
            new SKPoint[3],
            null,
            null,
            new ushort[] { 0, 1, 3 });
        Assert.NotNull(invalidIndices);

        var positions = new[]
        {
            new SKPoint(1f, 2f),
            new SKPoint(3f, 4f),
            new SKPoint(5f, 6f),
        };
        var textureCoordinates = new[]
        {
            new SKPoint(7f, 8f),
            new SKPoint(9f, 10f),
            new SKPoint(11f, 12f),
        };
        var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue };
        var indices = new ushort[] { 2, 1, 0 };
        using var vertices = SKVertices.CreateCopy(
            SKVertexMode.Triangles,
            positions,
            textureCoordinates,
            colors,
            indices);

        positions[0] = new SKPoint(100f, 100f);
        textureCoordinates[0] = new SKPoint(100f, 100f);
        colors[0] = SKColors.White;
        indices[0] = 0;

        Assert.Equal(VertexMeshTopology.Triangles, vertices.Mesh.Topology);
        Assert.Equal(new Vector2(1f, 2f), vertices.Mesh.Positions.Span[0]);
        Assert.Equal(new Vector2(7f, 8f), vertices.Mesh.TextureCoordinates.Span[0]);
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), vertices.Mesh.Colors.Span[0]);
        Assert.Equal((ushort)2, vertices.Mesh.Indices.Span[0]);
    }

    [Theory]
    [InlineData(SKVertexMode.Triangles, VertexMeshTopology.Triangles)]
    [InlineData(SKVertexMode.TriangleStrip, VertexMeshTopology.TriangleStrip)]
    [InlineData(SKVertexMode.TriangleFan, VertexMeshTopology.TriangleFan)]
    public void CanvasQueuesOneBatchedMeshWithTransformAndBlendMode(
        SKVertexMode mode,
        VertexMeshTopology expectedTopology)
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 32f, 32f);
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            IsAntialias = false,
        };
        canvas.Translate(3f, 4f);
        canvas.DrawVertices(
            mode,
            new[]
            {
                new SKPoint(0f, 0f),
                new SKPoint(8f, 0f),
                new SKPoint(0f, 8f),
                new SKPoint(8f, 8f),
            },
            null!,
            new[] { SKColors.Blue, SKColors.Blue, SKColors.Blue, SKColors.Blue },
            SKBlendMode.Dst,
            null!,
            paint);

        var command = Assert.Single(context.Commands);
        Assert.Equal(RenderCommandType.DrawVertexMesh, command.Type);
        Assert.Equal(expectedTopology, command.VertexMesh!.Topology);
        Assert.Equal(VertexColorBlendMode.Dst, command.VertexColorBlendMode);
        Assert.True(command.IsEdgeAliased);
        Assert.Equal(3f, command.Transform.M41);
        Assert.Equal(4f, command.Transform.M42);
    }

    [Fact]
    public void AppendingContextTranslatesMeshThroughCommandTransform()
    {
        var source = new DrawingContext();
        source.DrawVertexMesh(
            new ProGPU.Vector.SolidColorBrush(Vector4.One),
            new VertexMesh2D(
                VertexMeshTopology.Triangles,
                new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY }));
        var target = new DrawingContext();

        target.Append(source, new Vector2(5f, 6f));

        var command = Assert.Single(target.Commands);
        Assert.Equal(RenderCommandType.DrawVertexMesh, command.Type);
        Assert.Equal(5f, command.Transform.M41);
        Assert.Equal(6f, command.Transform.M42);
        Assert.Equal(Vector2.Zero, command.VertexMesh!.Positions.Span[0]);
    }
}
