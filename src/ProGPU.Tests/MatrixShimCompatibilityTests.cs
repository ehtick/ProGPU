using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class MatrixShimCompatibilityTests
{
    [Fact]
    public void SkMatrixConcatMatchesNativeSkiaOrder()
    {
        var scale = SKMatrix.CreateScale(2f, 3f);
        var translate = SKMatrix.CreateTranslation(10f, 20f);

        var result = SKMatrix.Concat(scale, translate);

        Assert.Equal(2f, result.ScaleX);
        Assert.Equal(3f, result.ScaleY);
        Assert.Equal(20f, result.TransX);
        Assert.Equal(60f, result.TransY);
    }

    [Fact]
    public void SkMatrixExposesNativePropertyAndValueContracts()
    {
        var publicFields = typeof(SKMatrix).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicFields);
        foreach (var propertyName in new[]
        {
            nameof(SKMatrix.ScaleX),
            nameof(SKMatrix.SkewX),
            nameof(SKMatrix.TransX),
            nameof(SKMatrix.SkewY),
            nameof(SKMatrix.ScaleY),
            nameof(SKMatrix.TransY),
            nameof(SKMatrix.Persp0),
            nameof(SKMatrix.Persp1),
            nameof(SKMatrix.Persp2),
        })
        {
            var property = typeof(SKMatrix).GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.True(property!.CanRead);
            Assert.True(property.CanWrite);
        }

        var values = new[] { 2f, 0.5f, 10f, -0.25f, 3f, 20f, 0.01f, -0.02f, 1f };
        var matrix = new SKMatrix(values);
        Assert.Equal(values, matrix.Values);
        var destination = new float[9];
        matrix.GetValues(destination);
        Assert.Equal(values, destination);

        Assert.Throws<ArgumentNullException>(() => new SKMatrix(null!));
        Assert.Throws<ArgumentException>(() => new SKMatrix(new float[8]));
        Assert.Throws<ArgumentException>(() => matrix.Values = new float[10]);
    }

    [Fact]
    public void SkMatrixProjectiveMappingMatchesNativeSkia()
    {
        var matrix = new SKMatrix(2f, 0.5f, 10f, -0.25f, 3f, 20f, 0.01f, -0.02f, 1f);

        AssertPoint(matrix.MapPoint(4f, 5f), 21.80851f, 36.17021f);
        AssertPoint(matrix.MapVector(4f, 5f), 11.80851f, 16.170212f);
        AssertNear(18.997923f, matrix.MapRadius(7f));
        AssertRect(
            matrix.MapRect(new SKRect(1f, 2f, 6f, 8f)),
            13.402061f,
            24.019608f,
            28.88889f,
            51.470585f);

        var points = new[] { new SKPoint(4f, 5f), new SKPoint(1f, 2f) };
        var mapped = matrix.MapPoints(points);
        AssertPoint(mapped[0], 21.80851f, 36.17021f);
        matrix.MapPoints(points, points);
        Assert.Equal(mapped, points);

        var vectors = new[] { new SKPoint(4f, 5f), new SKPoint(1f, 2f) };
        var mappedVectors = matrix.MapVectors(vectors);
        AssertPoint(mappedVectors[0], 11.80851f, 16.170212f);
        matrix.MapVectors(vectors, vectors);
        Assert.Equal(mappedVectors, vectors);

        var zeroW = new SKMatrix(1f, 0f, 0f, 0f, 1f, 0f, 1f, 0f, -1f);
        Assert.Equal(SKPoint.Empty, zeroW.MapPoint(1f, 2f));
    }

    [Fact]
    public void SkMatrixInversionAndCreationMatchNativeSkia()
    {
        var matrix = new SKMatrix(2f, 0.5f, 10f, -0.25f, 3f, 20f, 0.01f, -0.02f, 1f);

        Assert.True(matrix.IsInvertible);
        Assert.True(matrix.TryInvert(out var inverse));
        AssertValues(
            inverse,
            0.50184506f,
            -0.10332103f,
            -2.9520295f,
            0.06642066f,
            0.2804428f,
            -6.2730627f,
            -0.0036900367f,
            0.006642066f,
            0.90405905f);
        AssertValues(SKMatrix.Concat(matrix, inverse), 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);

        var singular = new SKMatrix(1f, 2f, 3f, 2f, 4f, 6f, 0f, 0f, 1f);
        Assert.False(singular.IsInvertible);
        Assert.False(singular.TryInvert(out var failedInverse));
        Assert.Equal(SKMatrix.Empty, failedInverse);
        Assert.Equal(SKMatrix.Empty, singular.Invert());

        Assert.Equal(SKMatrix.Identity, SKMatrix.CreateScaleTranslation(0f, 0f, 0f, 0f));
        Assert.Equal(SKMatrix.Identity, SKMatrix.CreateScaleTranslation(1f, 1f, 0f, 0f));
        AssertValues(SKMatrix.CreateSkew(2f, 3f), 1f, 2f, 0f, 3f, 1f, 0f, 0f, 0f, 1f);
        AssertValuesNear(
            SKMatrix.CreateRotationDegrees(37f, 4f, 5f),
            SKMatrix.CreateRotation(37f * MathF.PI / 180f, 4f, 5f));
    }

    private static void AssertValues(SKMatrix matrix, params float[] expected)
    {
        var actual = matrix.Values;
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            AssertNear(expected[index], actual[index]);
        }
    }

    private static void AssertValuesNear(SKMatrix expected, SKMatrix actual)
    {
        var expectedValues = expected.Values;
        var actualValues = actual.Values;
        for (var index = 0; index < expectedValues.Length; index++)
        {
            AssertNear(expectedValues[index], actualValues[index]);
        }
    }

    private static void AssertPoint(SKPoint point, float x, float y)
    {
        AssertNear(x, point.X);
        AssertNear(y, point.Y);
    }

    private static void AssertRect(SKRect rect, float left, float top, float right, float bottom)
    {
        AssertNear(left, rect.Left);
        AssertNear(top, rect.Top);
        AssertNear(right, rect.Right);
        AssertNear(bottom, rect.Bottom);
    }

    private static void AssertNear(float expected, float actual) =>
        Assert.InRange(actual, expected - 0.0001f, expected + 0.0001f);
}
