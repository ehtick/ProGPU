using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkTextBlobFactoryApiCompatibilityTests
{
    [Fact]
    public void TextBlobFactoriesExposeNativeSignatures()
    {
        Assert.Equal(4, GetFactoryOverloads(nameof(SKTextBlob.Create)).Length);
        Assert.Equal(4, GetFactoryOverloads(nameof(SKTextBlob.CreateHorizontal)).Length);
        Assert.Equal(4, GetFactoryOverloads(nameof(SKTextBlob.CreatePositioned)).Length);
        Assert.Equal(4, GetFactoryOverloads(nameof(SKTextBlob.CreateRotationScale)).Length);

        AssertParameterNames(
            GetFactory(nameof(SKTextBlob.Create), typeof(string), typeof(SKFont), typeof(SKPoint)),
            "text",
            "font",
            "origin");
        AssertParameterNames(
            GetFactory(
                nameof(SKTextBlob.CreateHorizontal),
                typeof(ReadOnlySpan<byte>),
                typeof(SKTextEncoding),
                typeof(SKFont),
                typeof(ReadOnlySpan<float>),
                typeof(float)),
            "text",
            "encoding",
            "font",
            "positions",
            "y");
        AssertParameterNames(
            GetFactory(
                nameof(SKTextBlob.CreatePositioned),
                typeof(IntPtr),
                typeof(int),
                typeof(SKTextEncoding),
                typeof(SKFont),
                typeof(ReadOnlySpan<SKPoint>)),
            "text",
            "length",
            "encoding",
            "font",
            "positions");
        AssertParameterNames(
            GetFactory(
                nameof(SKTextBlob.CreateRotationScale),
                typeof(ReadOnlySpan<char>),
                typeof(SKFont),
                typeof(ReadOnlySpan<SKRotationScaleMatrix>)),
            "text",
            "font",
            "positions");
        AssertParameterNames(
            typeof(SKTextBlob).GetMethod(
                nameof(SKTextBlob.GetIntercepts),
                [typeof(float), typeof(float), typeof(SKPaint)]),
            "upperBounds",
            "lowerBounds",
            "paint");
    }

    [Fact]
    public void TextBlobFactoriesPreserveDecodedGlyphPlacement()
    {
        using var font = new SKFont(SKTypeface.Default, 24f);
        var origin = new SKPoint(7f, 11f);
        var glyphs = font.GetGlyphs("Ab");
        var expectedPositions = font.GetGlyphPositions(glyphs, origin);

        using var sequential = SKTextBlob.Create("Ab", font, origin);
        using var horizontal = SKTextBlob.CreateHorizontal(
            "Ab",
            font,
            new[] { 3f, 17f },
            9f);
        var encoded = Encoding.UTF8.GetBytes("Ab");
        using var positioned = SKTextBlob.CreatePositioned(
            encoded,
            SKTextEncoding.Utf8,
            font,
            new[] { new SKPoint(2f, 4f), new SKPoint(8f, 16f) });
        var matrices = new[]
        {
            SKRotationScaleMatrix.CreateTranslation(5f, 6f),
            SKRotationScaleMatrix.CreateDegrees(1.25f, 20f, 12f, 14f, 0f, 0f),
        };
        using var rotationScale = SKTextBlob.CreateRotationScale("Ab", font, matrices);

        Assert.NotNull(sequential);
        Assert.Equal(glyphs, sequential.GlyphIndices);
        Assert.Equal(expectedPositions, sequential.GlyphPositions);
        Assert.Equal(new[] { new SKPoint(3f, 9f), new SKPoint(17f, 9f) }, horizontal!.GlyphPositions);
        Assert.Equal(new[] { new SKPoint(2f, 4f), new SKPoint(8f, 16f) }, positioned!.GlyphPositions);
        Assert.Equal(matrices, Assert.Single(rotationScale!.Runs).RotationScaleMatrices);
        Assert.Null(SKTextBlob.Create(string.Empty, font));
    }

    [Fact]
    public void PointerFactoryUsesRequestedEncoding()
    {
        using var font = new SKFont(SKTypeface.Default, 24f);
        var encoded = Encoding.UTF8.GetBytes("Az");
        var pointer = Marshal.AllocHGlobal(encoded.Length);
        try
        {
            Marshal.Copy(encoded, 0, pointer, encoded.Length);
            using var blob = SKTextBlob.Create(
                pointer,
                encoded.Length,
                SKTextEncoding.Utf8,
                font,
                new SKPoint(4f, 6f));

            Assert.NotNull(blob);
            Assert.Equal(font.GetGlyphs(encoded, SKTextEncoding.Utf8), blob.GlyphIndices);
            Assert.Equal(
                font.GetGlyphPositions(encoded, SKTextEncoding.Utf8, new SKPoint(4f, 6f)),
                blob.GlyphPositions);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Fact]
    public void TextBlobBoundsAndUniqueIdsReflectRetainedGlyphs()
    {
        using var font = new SKFont(SKTypeface.Default, 36f);
        var position = new SKPoint(13f, 19f);
        using var positioned = SKTextBlob.CreatePositioned("A", font, new[] { position });
        using var second = SKTextBlob.CreatePositioned("A", font, new[] { position });
        var glyph = Assert.Single(positioned!.GlyphIndices);
        using var glyphPath = font.GetGlyphPath(glyph);
        var expected = glyphPath!.Bounds;
        expected.Offset(position.X, position.Y);

        AssertRectNear(expected, positioned.Bounds);
        Assert.NotEqual(0u, positioned.UniqueId);
        Assert.NotEqual(positioned.UniqueId, second!.UniqueId);

        var placement = SKRotationScaleMatrix.CreateDegrees(1.1f, 32f, 21f, 27f, 0f, 0f);
        using var rotated = SKTextBlob.CreateRotationScale("A", font, new[] { placement });
        using var transformed = new SKPath(glyphPath);
        transformed.Transform(placement.ToMatrix());
        AssertRectNear(transformed.Bounds, rotated!.Bounds);
    }

    private static MethodInfo[] GetFactoryOverloads(string name) =>
        typeof(SKTextBlob)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == name)
            .ToArray();

    private static MethodInfo? GetFactory(string name, params Type[] parameterTypes) =>
        typeof(SKTextBlob).GetMethod(name, parameterTypes);

    private static void AssertParameterNames(MethodBase? method, params string[] expected)
    {
        Assert.NotNull(method);
        Assert.Equal(expected, method!.GetParameters().Select(static parameter => parameter.Name));
    }

    private static void AssertRectNear(SKRect expected, SKRect actual)
    {
        Assert.InRange(actual.Left, expected.Left - 0.001f, expected.Left + 0.001f);
        Assert.InRange(actual.Top, expected.Top - 0.001f, expected.Top + 0.001f);
        Assert.InRange(actual.Right, expected.Right - 0.001f, expected.Right + 0.001f);
        Assert.InRange(actual.Bottom, expected.Bottom - 0.001f, expected.Bottom + 0.001f);
    }
}
