using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Backend;
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

    [Fact]
    public void ShaderToy_HonorsActiveBlendModeAfterSrcOverPipelineReuse()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(72, 32);

        var shaderKey = $"test_shadertoy_blend_{Guid.NewGuid():N}";
        var srcOver = CreateSolidRedParams(new Rect(0f, 0f, 32f, 32f), "src_over", shaderKey);
        var clear = CreateSolidRedParams(new Rect(40f, 0f, 32f, 32f), "clear", shaderKey);

        window.Content = new ClearBlendShaderToyVisual(srcOver, clear);

        try
        {
            window.Render();

            Assert.False(srcOver.IsFailed);
            Assert.False(clear.IsFailed);

            var pixels = window.ReadPixels();
            var srcOverPixel = ReadPixel(pixels, window.Width, x: 16, y: 16);
            var clearedPixel = ReadPixel(pixels, window.Width, x: 56, y: 16);

            Assert.True(srcOverPixel.R >= 220, $"Expected SrcOver ShaderToy to render red, found {srcOverPixel}.");
            Assert.True(srcOverPixel.G <= 35, $"Expected SrcOver ShaderToy to keep green low, found {srcOverPixel}.");
            Assert.True(srcOverPixel.B <= 35, $"Expected SrcOver ShaderToy to keep blue low, found {srcOverPixel}.");
            Assert.Equal(255, srcOverPixel.A);
            AssertTransparent(clearedPixel);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void ShaderToy_AppliesPartialOpacityMaskOnce()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(32, 32);

        var shader = CreateSolidRedParams(new Rect(0f, 0f, 32f, 32f), "partial_mask");
        window.Content = new PartialMaskShaderToyVisual(shader);

        try
        {
            window.Render();

            Assert.False(shader.IsFailed);

            var pixel = ReadPixel(window.ReadPixels(), window.Width, x: 16, y: 16);

            Assert.InRange(pixel.R, 110, 150);
            Assert.InRange(pixel.G, 0, 16);
            Assert.InRange(pixel.B, 0, 16);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void ShaderToy_GlslIFrameModuloCompilesAndRenders()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(32, 32);

        var shader = new ShaderToyParams
        {
            Rect = new Rect(0f, 0f, 32f, 32f),
            ShaderKey = $"test_shadertoy_iframe_modulo_{Guid.NewGuid():N}",
            ShaderSource = """
                void mainImage(out vec4 fragColor, in vec2 fragCoord)
                {
                    if ((iFrame % 2) == 0)
                    {
                        fragColor = vec4(0.0, 1.0, 0.0, 1.0);
                    }
                    else
                    {
                        fragColor = vec4(1.0, 0.0, 0.0, 1.0);
                    }
                }
                """,
            Resolution = new Vector3(32f, 32f, 1f),
            Time = 0f,
            TimeDelta = 0f,
            Frame = 2f,
            FrameRate = 60f,
            Mouse = Vector4.Zero,
            Date = Vector4.Zero
        };

        window.Content = new SingleShaderToyVisual(shader, 32f, 32f);

        try
        {
            window.Render();

            Assert.False(shader.IsFailed);

            var pixel = ReadPixel(window.ReadPixels(), window.Width, x: 16, y: 16);
            Assert.InRange(pixel.R, 0, 32);
            Assert.InRange(pixel.G, 200, 255);
            Assert.InRange(pixel.B, 0, 32);
            Assert.Equal(255, pixel.A);
        }
        finally
        {
            window.Content = null;
        }
    }

    private static ShaderToyParams CreateSolidRedParams(Rect rect, string name)
    {
        return CreateSolidRedParams(rect, name, $"test_shadertoy_mask_{name}_{Guid.NewGuid():N}");
    }

    private static ShaderToyParams CreateSolidRedParams(Rect rect, string name, string shaderKey)
    {
        return new ShaderToyParams
        {
            Rect = rect,
            ShaderKey = shaderKey,
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

    private static void AssertTransparent(RgbaPixel actual)
    {
        Assert.InRange(actual.R, 0, 8);
        Assert.InRange(actual.G, 0, 8);
        Assert.InRange(actual.B, 0, 8);
        Assert.InRange(actual.A, 0, 8);
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
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 0f)),
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

    private sealed class ClearBlendShaderToyVisual : FrameworkElement
    {
        private readonly ShaderToyParams _srcOver;
        private readonly ShaderToyParams _clear;

        public ClearBlendShaderToyVisual(ShaderToyParams srcOver, ShaderToyParams clear)
        {
            _srcOver = srcOver;
            _clear = clear;
            Width = 72f;
            Height = 32f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(0f, 0f, 1f, 1f)),
                null,
                new Rect(0f, 0f, 72f, 32f));

            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _srcOver);

            context.PushBlendMode(GpuBlendMode.Clear);
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _clear);
            context.PopBlendMode();
        }
    }

    private sealed class PartialMaskShaderToyVisual : FrameworkElement
    {
        private readonly ShaderToyParams _shader;

        public PartialMaskShaderToyVisual(ShaderToyParams shader)
        {
            _shader = shader;
            Width = 32f;
            Height = 32f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.PushOpacityMask(
                new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)) { Opacity = 0.5f },
                new Rect(0f, 0f, 32f, 32f));
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _shader);
            context.PopOpacityMask();
        }
    }

    private sealed class SingleShaderToyVisual : FrameworkElement
    {
        private readonly ShaderToyParams _shader;

        public SingleShaderToyVisual(ShaderToyParams shader, float width, float height)
        {
            _shader = shader;
            Width = width;
            Height = height;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawExtension(
                CompositorBuiltInExtensions.ShaderToy,
                dataParam: _shader);
        }
    }
}
