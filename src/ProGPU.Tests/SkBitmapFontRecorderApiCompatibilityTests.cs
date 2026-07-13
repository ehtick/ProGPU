using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkBitmapFontRecorderApiCompatibilityTests
{
    [Fact]
    public void BitmapFontAndRecorderExposeNativeSignatures()
    {
        AssertParameterNames(
            typeof(SKBitmap).GetConstructor(
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
            typeof(SKBitmap).GetMethod(
                nameof(SKBitmap.Decode),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(ReadOnlySpan<byte>), typeof(SKImageInfo)]),
            "buffer",
            "bitmapInfo");
        AssertParameterNames(
            typeof(SKBitmap).GetMethod(nameof(SKBitmap.CanCopyTo)),
            "colorType");
        AssertParameterNames(
            typeof(SKFont).GetMethod(nameof(SKFont.GetGlyphPath)),
            "glyph");
        AssertParameterNames(
            typeof(SKFont).GetMethod(
                nameof(SKFont.GetGlyphPositions),
                [typeof(string), typeof(Span<SKPoint>), typeof(SKPoint)]),
            "text",
            "offsets",
            "origin");
        AssertParameterNames(
            typeof(SKPictureRecorder).GetMethod(
                nameof(SKPictureRecorder.BeginRecording),
                [typeof(SKRect), typeof(bool)]),
            "cullRect",
            "useRTree");
    }

    [Fact]
    public void RecorderRTreeHintUsesRetainedPicturePipeline()
    {
        using var recorder = new SKPictureRecorder();
        using var paint = new SKPaint { Color = SKColors.Red };
        var cullRect = new SKRect(0f, 0f, 64f, 32f);

        var canvas = recorder.BeginRecording(cullRect, useRTree: true);
        canvas.DrawRect(cullRect, paint);
        using var picture = recorder.EndRecording();

        Assert.Equal(cullRect, picture.CullRect);
        Assert.Null(recorder.RecordingCanvas);
    }

    private static void AssertParameterNames(MethodBase? method, params string[] expected)
    {
        Assert.NotNull(method);
        Assert.Equal(expected, method!.GetParameters().Select(parameter => parameter.Name));
    }
}
