using System.Reflection;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkCanvasTransformCompatibilityTests
{
    [Fact]
    public void ConvenienceTransformsMatchNativePreConcatOrder()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 32f, 32f);

        canvas.Scale(2f, 3f);
        canvas.Translate(4f, 5f);
        AssertMatrix(canvas.TotalMatrix, 2f, 0f, 8f, 0f, 3f, 15f);

        canvas.ResetMatrix();
        canvas.Translate(4f, 5f);
        canvas.Scale(2f, 3f);
        AssertMatrix(canvas.TotalMatrix, 2f, 0f, 4f, 0f, 3f, 5f);

        canvas.ResetMatrix();
        canvas.RotateDegrees(90f, 4f, 5f);
        AssertMatrix(canvas.TotalMatrix, 0f, -1f, 9f, 1f, 0f, 1f);

        canvas.ResetMatrix();
        canvas.Scale(2f, 3f, 4f, 5f);
        AssertMatrix(canvas.TotalMatrix, 2f, 0f, -4f, 0f, 3f, -10f);

        canvas.ResetMatrix();
        canvas.Skew(2f, 3f);
        AssertMatrix(canvas.TotalMatrix, 1f, 2f, 0f, 3f, 1f, 0f);
    }

    [Fact]
    public void EmptyPointTransformOverloadsPreserveNativeNoOpSemantics()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 32f, 32f);

        canvas.Translate(SKPoint.Empty);
        canvas.Scale(SKPoint.Empty);
        canvas.Skew(SKPoint.Empty);

        Assert.Equal(SKMatrix.Identity, canvas.TotalMatrix);
        canvas.Scale(0f, 0f);
        Assert.Equal(0f, canvas.TotalMatrix.ScaleX);
        Assert.Equal(0f, canvas.TotalMatrix.ScaleY);
    }

    [Fact]
    public void MatrixApisExposeNativeReadonlyReferenceContracts()
    {
        var methods = typeof(SKCanvas).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var concat = methods.Where(method => method.Name == nameof(SKCanvas.Concat)).ToArray();
        Assert.Equal(2, concat.Length);
        Assert.All(concat, method =>
        {
            var parameter = Assert.Single(method.GetParameters());
            Assert.Equal("m", parameter.Name);
            Assert.True(parameter.ParameterType.IsByRef);
            Assert.True(parameter.IsIn);
        });

        var setMatrix = methods.Where(method => method.Name == nameof(SKCanvas.SetMatrix)).ToArray();
        Assert.Equal(3, setMatrix.Length);
        Assert.Equal(2, setMatrix.Count(method => method.GetParameters()[0].ParameterType.IsByRef));
        Assert.All(
            setMatrix.Where(method => method.GetParameters()[0].ParameterType.IsByRef),
            method => Assert.True(method.GetParameters()[0].IsIn));
        Assert.Single(
            setMatrix,
            method => method.GetParameters()[0].ParameterType == typeof(SKMatrix));
        Assert.False(typeof(SKCanvas).GetProperty(nameof(SKCanvas.TotalMatrix))!.CanWrite);
    }

    [Fact]
    public void PrimitiveConvenienceOverloadsReuseRetainedCommands()
    {
        var context = new DrawingContext();
        using var canvas = new SKCanvas(context, 64f, 64f);
        using var paint = new SKPaint { Color = SKColors.Red };

        canvas.DrawLine(new SKPoint(1f, 2f), new SKPoint(3f, 4f), paint);
        canvas.DrawCircle(new SKPoint(8f, 9f), 3f, paint);
        canvas.DrawOval(new SKPoint(16f, 17f), new SKSize(4f, 5f), paint);
        canvas.DrawOval(24f, 25f, 6f, 7f, paint);
        canvas.DrawRoundRect(new SKRect(2f, 30f, 14f, 42f), new SKSize(2f, 3f), paint);
        canvas.DrawRoundRect(20f, 30f, 16f, 12f, 3f, 4f, paint);

        Assert.Collection(
            context.Commands,
            command => Assert.Equal(RenderCommandType.DrawPath, command.Type),
            command => Assert.Equal(RenderCommandType.DrawCircle, command.Type),
            command => Assert.Equal(RenderCommandType.DrawEllipse, command.Type),
            command => Assert.Equal(RenderCommandType.DrawEllipse, command.Type),
            command => Assert.Equal(RenderCommandType.DrawRoundedRect, command.Type),
            command => Assert.Equal(RenderCommandType.DrawRoundedRect, command.Type));
    }

    private static void AssertMatrix(
        SKMatrix matrix,
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY)
    {
        AssertNear(scaleX, matrix.ScaleX);
        AssertNear(skewX, matrix.SkewX);
        AssertNear(transX, matrix.TransX);
        AssertNear(skewY, matrix.SkewY);
        AssertNear(scaleY, matrix.ScaleY);
        AssertNear(transY, matrix.TransY);
        AssertNear(0f, matrix.Persp0);
        AssertNear(0f, matrix.Persp1);
        AssertNear(1f, matrix.Persp2);
    }

    private static void AssertNear(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.0001f, expected + 0.0001f);
}
