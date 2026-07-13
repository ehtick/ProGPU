using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkTextBlobPathPositionedCompatibilityTests
{
    [Fact]
    public void StraightPathProducesNativeTangentPlacement()
    {
        using var font = new SKFont { Size = 20f };
        using var path = CreateHorizontalPath(200f);
        using var blob = SKTextBlob.CreatePathPositioned("AB", font, path);

        Assert.NotNull(blob);
        var run = Assert.Single(blob!.Runs);
        var matrices = Assert.IsType<SKRotationScaleMatrix[]>(run.RotationScaleMatrices);
        Assert.Equal(2, matrices.Length);
        Assert.All(matrices, matrix =>
        {
            Assert.Equal(1f, matrix.SCos);
            Assert.Equal(0f, matrix.SSin);
            Assert.Equal(0f, matrix.TY);
        });
        AssertNear(0f, matrices[0].TX);
        AssertNear(font.GetGlyphWidths(run.GlyphIndices)[0], matrices[1].TX);
    }

    [Fact]
    public void AlignmentAndNormalOffsetFollowNativePathAlgorithm()
    {
        using var font = new SKFont { Size = 18f };
        using var path = CreateHorizontalPath(240f);
        var origin = new SKPoint(0f, 7f);
        using var blob = SKTextBlob.CreatePathPositioned("GPU", font, path, SKTextAlign.Center, origin);

        Assert.NotNull(blob);
        var run = Assert.Single(blob!.Runs);
        var matrices = run.RotationScaleMatrices!;
        var widths = font.GetGlyphWidths(run.GlyphIndices);
        var offsets = font.GetGlyphPositions(run.GlyphIndices, origin);
        var textWidth = offsets[^1].X + widths[^1];
        var expectedStart = (240f - textWidth) * 0.5f;

        Assert.Equal(expectedStart, matrices[0].TX);
        Assert.Equal(7f, matrices[0].TY);
    }

    [Fact]
    public void CharacterByteAndPointerInputsProduceEquivalentRuns()
    {
        using var font = new SKFont { Size = 16f };
        using var path = CreateHorizontalPath(200f);
        const string text = "Path";
        var utf8 = Encoding.UTF8.GetBytes(text);
        var pointer = Marshal.AllocHGlobal(utf8.Length);
        try
        {
            Marshal.Copy(utf8, 0, pointer, utf8.Length);
            using var characters = SKTextBlob.CreatePathPositioned(text.AsSpan(), font, path);
            using var bytes = SKTextBlob.CreatePathPositioned(utf8, SKTextEncoding.Utf8, font, path);
            using var pointerBlob = SKTextBlob.CreatePathPositioned(
                pointer,
                utf8.Length,
                SKTextEncoding.Utf8,
                font,
                path);

            Assert.Equal(characters!.GlyphIndices, bytes!.GlyphIndices);
            Assert.Equal(characters.GlyphIndices, pointerBlob!.GlyphIndices);
            Assert.Equal(
                characters.Runs[0].RotationScaleMatrices,
                bytes.Runs[0].RotationScaleMatrices);
            Assert.Equal(
                characters.Runs[0].RotationScaleMatrices,
                pointerBlob.Runs[0].RotationScaleMatrices);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static SKPath CreateHorizontalPath(float length)
    {
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.LineTo(length, 0f);
        return path;
    }

    private static void AssertNear(float expected, float actual) =>
        Assert.InRange(MathF.Abs(expected - actual), 0f, 0.00001f);
}
