using System.Numerics;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkMatrix44CompatibilityTests
{
    private static readonly float[] s_values = Enumerable.Range(1, 16).Select(static value => (float)value).ToArray();

    [Fact]
    public void ConstructorsAndIdentityMatchNative()
    {
        Assert.Equal(new float[16], new SKMatrix44().ToRowMajor());
        Assert.Equal(new float[16], SKMatrix44.Empty.ToRowMajor());
        Assert.Equal(
            new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f },
            SKMatrix44.Identity.ToRowMajor());
        Assert.Equal(SKMatrix44.Identity, SKMatrix44.CreateIdentity());
        Assert.Equal(SKMatrix44.Identity, new SKMatrix44(SKMatrix44.Identity));
    }

    [Fact]
    public void SkMatrixConversionPreservesNativeLayout()
    {
        var source = new SKMatrix(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f);
        var matrix = new SKMatrix44(source);
        Assert.Equal(
            new[] { 1f, 4f, 0f, 7f, 2f, 5f, 0f, 8f, 0f, 0f, 1f, 0f, 3f, 6f, 0f, 9f },
            matrix.ToRowMajor());
        Assert.Equal(source, matrix.Matrix);
    }

    [Fact]
    public void IndexerReadsWritesAndValidatesCoordinates()
    {
        var matrix = SKMatrix44.FromRowMajor(s_values);
        Assert.Equal(1f, matrix[0, 0]);
        Assert.Equal(8f, matrix[1, 3]);
        Assert.Equal(16f, matrix[3, 3]);
        matrix[2, 1] = 99f;
        Assert.Equal(99f, matrix.M21);
        Assert.Equal("row", Assert.Throws<ArgumentOutOfRangeException>(() => _ = matrix[-1, 0]).ParamName);
        Assert.Equal("column", Assert.Throws<ArgumentOutOfRangeException>(() => _ = matrix[0, 4]).ParamName);
        Assert.Equal("row", Assert.Throws<ArgumentOutOfRangeException>(() => matrix[4, 0] = 1f).ParamName);
        Assert.Equal("column", Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, -1] = 1f).ParamName);
    }

    [Fact]
    public void RowAndColumnMajorConversionsMatchNative()
    {
        var rowMajor = SKMatrix44.FromRowMajor(s_values);
        Assert.Equal(s_values, rowMajor.ToRowMajor());
        Assert.Equal(
            new[] { 1f, 5f, 9f, 13f, 2f, 6f, 10f, 14f, 3f, 7f, 11f, 15f, 4f, 8f, 12f, 16f },
            rowMajor.ToColumnMajor());
        Assert.Equal(rowMajor, SKMatrix44.FromColumnMajor(rowMajor.ToColumnMajor()));
        Span<float> destination = stackalloc float[16];
        rowMajor.ToRowMajor(destination);
        Assert.Equal(s_values, destination.ToArray());
    }

    [Fact]
    public void MajorOrderConversionsRequireExactlySixteenValues()
    {
        Assert.Equal("src", Assert.Throws<ArgumentException>(() => SKMatrix44.FromRowMajor(new float[15])).ParamName);
        Assert.Equal("src", Assert.Throws<ArgumentException>(() => SKMatrix44.FromColumnMajor(new float[17])).ParamName);
        var matrix = SKMatrix44.Identity;
        Assert.Equal("dst", Assert.Throws<ArgumentException>(() => matrix.ToRowMajor(new float[15])).ParamName);
        Assert.Equal("dst", Assert.Throws<ArgumentException>(() => matrix.ToColumnMajor(new float[17])).ParamName);
    }

    [Fact]
    public void TranslationScaleAndRotationFactoriesMatchNumerics()
    {
        Assert.Equal(Matrix4x4.CreateTranslation(2f, 3f, 4f), (Matrix4x4)SKMatrix44.CreateTranslation(2f, 3f, 4f));
        Assert.Equal(Matrix4x4.CreateScale(2f, 3f, 4f), (Matrix4x4)SKMatrix44.CreateScale(2f, 3f, 4f));
        Assert.Equal(
            Matrix4x4.CreateScale(2f, 3f, 4f, new Vector3(5f, 6f, 7f)),
            (Matrix4x4)SKMatrix44.CreateScale(2f, 3f, 4f, 5f, 6f, 7f));
        Assert.Equal(
            Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f),
            (Matrix4x4)SKMatrix44.CreateRotation(0f, 0f, 1f, MathF.PI / 2f));
        Assert.Equal(
            Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, 90f * (MathF.PI / 180f)),
            (Matrix4x4)SKMatrix44.CreateRotationDegrees(0f, 0f, 1f, 90f));
    }

    [Fact]
    public void DeterminantAndInversionMatchNumerics()
    {
        var matrix = SKMatrix44.CreateScale(2f, 4f, 5f).PostConcat(SKMatrix44.CreateTranslation(3f, 6f, 9f));
        Assert.Equal(((Matrix4x4)matrix).GetDeterminant(), matrix.Determinant());
        Assert.True(matrix.IsInvertible);
        Assert.True(matrix.TryInvert(out var inverse));
        Assert.Equal(matrix.Invert(), inverse);
        Assert.Equal(SKMatrix44.Identity, SKMatrix44.Multiply(matrix, inverse));
    }

    [Fact]
    public void SingularInversionReturnsEmpty()
    {
        var singular = SKMatrix44.CreateScale(1f, 0f, 1f);
        Assert.False(singular.IsInvertible);
        Assert.False(singular.TryInvert(out var inverse));
        Assert.Equal(SKMatrix44.Empty, inverse);
        Assert.Equal(SKMatrix44.Empty, singular.Invert());
    }

    [Fact]
    public void TransposeAndPointMappingMatchNumerics()
    {
        var matrix = SKMatrix44.CreateScale(2f, 3f, 4f).PreConcat(SKMatrix44.CreateTranslation(5f, 6f, 7f));
        Assert.Equal(Matrix4x4.Transpose(matrix), (Matrix4x4)matrix.Transpose());
        var point2 = matrix.MapPoint(1f, 2f);
        var expected2 = Vector2.Transform(new Vector2(1f, 2f), matrix);
        Assert.Equal(new SKPoint(expected2.X, expected2.Y), point2);
        var point3 = matrix.MapPoint(new SKPoint3(1f, 2f, 3f));
        var expected3 = Vector3.Transform(new Vector3(1f, 2f, 3f), matrix);
        Assert.Equal(new SKPoint3(expected3.X, expected3.Y, expected3.Z), point3);
    }

    [Fact]
    public void ConcatPreAndPostOrderMatchNative()
    {
        var scale = SKMatrix44.CreateScale(2f, 3f, 4f);
        var translation = SKMatrix44.CreateTranslation(5f, 6f, 7f);
        Assert.Equal(SKMatrix44.Multiply(scale, translation), SKMatrix44.Concat(scale, translation));
        Assert.Equal(SKMatrix44.Multiply(scale, translation), scale.PreConcat(translation));
        Assert.Equal(SKMatrix44.Multiply(translation, scale), scale.PostConcat(translation));
        var target = SKMatrix44.Empty;
        SKMatrix44.Concat(ref target, scale, translation);
        Assert.Equal(SKMatrix44.Multiply(scale, translation), target);
    }

    [Fact]
    public void ArithmeticMatchesNumerics()
    {
        var left = SKMatrix44.FromRowMajor(s_values);
        var right = SKMatrix44.CreateScale(2f, 3f, 4f);
        Assert.Equal((Matrix4x4)left + (Matrix4x4)right, (Matrix4x4)SKMatrix44.Add(left, right));
        Assert.Equal((Matrix4x4)left - (Matrix4x4)right, (Matrix4x4)SKMatrix44.Subtract(left, right));
        Assert.Equal((Matrix4x4)left * (Matrix4x4)right, (Matrix4x4)SKMatrix44.Multiply(left, right));
        Assert.Equal((Matrix4x4)left * 2f, (Matrix4x4)SKMatrix44.Multiply(left, 2f));
        Assert.Equal(-(Matrix4x4)left, (Matrix4x4)SKMatrix44.Negate(left));
    }

    [Fact]
    public void EqualityAndHashUseEveryScalar()
    {
        var value = SKMatrix44.FromRowMajor(s_values);
        var equal = new SKMatrix44(value);
        Assert.True(value == equal);
        Assert.False(value != equal);
        Assert.Equal(value.GetHashCode(), equal.GetHashCode());
        equal.M33 = 99f;
        Assert.NotEqual(value, equal);
    }
}
