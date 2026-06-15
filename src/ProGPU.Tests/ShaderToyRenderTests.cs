using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Scene;
using ProGPU.Scene.Extensions;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class ShaderToyRenderTests
{
    private const string SolidRedShader = @"
fn mainImage(fragCoord: vec2<f32>) -> vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
";

    private const string HorizontalSplitShader = @"
fn mainImage(fragCoord: vec2<f32>) -> vec4<f32> {
    if (fragCoord.x < 40.0) {
        return vec4<f32>(1.0, 0.0, 0.0, 1.0);
    }

    return vec4<f32>(0.0, 1.0, 0.0, 1.0);
}
";

    [Fact]
    public void ShaderToy_HonorsActiveOpacityMask()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(160, 90);

        var unmasked = CreateSolidRedParams(new Rect(20f, 25f, 40f, 40f), "unmasked");
        var masked = CreateSolidRedParams(new Rect(90f, 25f, 40f, 40f), "masked");

        window.Content = new MaskedShaderToyVisual(unmasked, masked);

        try
        {
            window.Render();

            Assert.False(unmasked.IsFailed);
            Assert.False(masked.IsFailed);

            var pixels = window.ReadPixels();
            var background = ReadPixel(pixels, window.Width, x: 10, y: 10);
            var visible = ReadPixel(pixels, window.Width, x: 40, y: 45);
            var hidden = ReadPixel(pixels, window.Width, x: 110, y: 45);

            Assert.True(visible.R >= 220, $"Expected unmasked ShaderToy to render red, found {visible}.");
            Assert.True(visible.G <= 35, $"Expected unmasked ShaderToy to keep green low, found {visible}.");
            Assert.True(visible.B <= 35, $"Expected unmasked ShaderToy to keep blue low, found {visible}.");
            Assert.Equal(255, visible.A);

            AssertColorNear(background, hidden, tolerance: 12);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void ShaderToy_PreservesFragCoordinatesWhenClipped()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(160, 90);

        var shader = new ShaderToyParams
        {
            Rect = new Rect(20f, 25f, 80f, 40f),
            ShaderKey = $"test_shadertoy_clipped_coords_{Guid.NewGuid():N}",
            ShaderSource = HorizontalSplitShader,
            Resolution = new Vector3(80f, 40f, 1f),
            Time = 0f,
            TimeDelta = 0f,
            Frame = 0f,
            FrameRate = 60f,
            Mouse = Vector4.Zero,
            Date = Vector4.Zero
        };

        window.Content = new ClippedShaderToyVisual(shader);

        try
        {
            window.Render();

            Assert.False(shader.IsFailed);

            var pixels = window.ReadPixels();
            var clippedLeft = ReadPixel(pixels, window.Width, x: 65, y: 45);

            Assert.True(clippedLeft.G >= 180, $"Expected clipped ShaderToy to preserve right-half green fragCoord, found {clippedLeft}.");
            Assert.True(clippedLeft.R <= 80, $"Expected clipped ShaderToy not to compress left red half, found {clippedLeft}.");
            Assert.Equal(255, clippedLeft.A);
        }
        finally
        {
            window.Content = null;
        }
    }

    private static ShaderToyParams CreateSolidRedParams(Rect rect, string name)
    {
        return new ShaderToyParams
        {
            Rect = rect,
            ShaderKey = $"test_shadertoy_mask_{name}_{Guid.NewGuid():N}",
            ShaderSource = SolidRedShader,
            Resolution = new Vector3(rect.Width, rect.Height, 1f),
            Time = 0f,
            TimeDelta = 0f,
            Frame = 0f,
            FrameRate = 60f,
            Mouse = Vector4.Zero,
            Date = Vector4.Zero
        };
    }

    private static RgbaPixel ReadPixel(byte[] pixels, uint width, int x, int y)
    {
        var index = ((y * (int)width) + x) * 4;
        return new RgbaPixel(
            pixels[index + 0],
            pixels[index + 1],
            pixels[index + 2],
            pixels[index + 3]);
    }

    private static void AssertColorNear(RgbaPixel expected, RgbaPixel actual, int tolerance)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, tolerance);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, tolerance);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, tolerance);
        Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance);
    }

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class MaskedShaderToyVisual : FrameworkElement
    {
        private readonly ShaderToyParams _unmasked;
        private readonly ShaderToyParams _masked;

        public MaskedShaderToyVisual(ShaderToyParams unmasked, ShaderToyParams masked)
        {
            _unmasked = unmasked;
            _masked = masked;
            Width = 160f;
            Height = 90f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _unmasked);

            context.PushOpacityMask(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 1f)),
                new Rect(90f, 25f, 40f, 40f));
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _masked);
            context.PopOpacityMask();
        }
    }

    private sealed class ClippedShaderToyVisual : FrameworkElement
    {
        private readonly ShaderToyParams _shader;

        public ClippedShaderToyVisual(ShaderToyParams shader)
        {
            _shader = shader;
            Width = 160f;
            Height = 90f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.PushClip(new Rect(60f, 25f, 40f, 40f));
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _shader);
            context.PopClip();
        }
    }
}
