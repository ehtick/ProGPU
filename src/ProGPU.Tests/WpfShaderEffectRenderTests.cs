using Microsoft.UI.Xaml;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using Silk.NET.WebGPU;
using Xunit;

namespace ProGPU.Tests;

public sealed class WpfShaderEffectRenderTests
{
    [Fact]
    public void WpfShaderEffect_RendersThroughNativeGpuPipeline()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(128, 96);

        using var texture = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "WPF Shader Effect Test Texture");
        texture.WritePixels(new byte[] { 16, 96, 160, 255 });

        var effect = new WpfShaderEffectParams
        {
            Texture = texture,
            Rect = new Rect(24, 20, 64, 48),
            ShaderKey = "test_wpf_native_shader_effect_tint",
            Constants = new[] { 1f, 0.25f, 0.75f, 1f },
            SamplingMode = TextureSamplingMode.Nearest,
            ShaderSource = @"
fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> {
    let tint = wpf_constant(0u);
    return vec4<f32>(tint.r, inputColor.g * tint.g, tint.b, inputColor.a);
}
"
        };

        window.Content = new ShaderEffectVisual(effect);

        try
        {
            window.Render();

            Assert.False(effect.IsFailed, effect.LastError);

            var pixels = window.ReadPixels();
            var center = (((20 + 24) * 128) + (24 + 32)) * 4;

            Assert.InRange(pixels[center + 0], 240, 255);
            Assert.InRange(pixels[center + 1], 18, 32);
            Assert.InRange(pixels[center + 2], 180, 205);
            Assert.Equal(255, pixels[center + 3]);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void WpfShaderEffect_CanSampleNativeSamplerRegister()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(128, 96);

        using var source0 = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "WPF Shader Effect Register 0 Texture");
        source0.WritePixels(new byte[] { 255, 0, 0, 255 });

        using var source1 = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "WPF Shader Effect Register 1 Texture");
        source1.WritePixels(new byte[] { 0, 192, 64, 255 });

        var effect = new WpfShaderEffectParams
        {
            Rect = new Rect(24, 20, 64, 48),
            ShaderKey = "test_wpf_native_shader_effect_sampler_register_1",
            Samplers = new[]
            {
                new WpfShaderEffectSampler(0, source0, TextureSamplingMode.Nearest),
                new WpfShaderEffectSampler(1, source1, TextureSamplingMode.Nearest)
            },
            ShaderSource = @"
fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> {
    return wpf_sample_register(1u, uv);
}
"
        };

        window.Content = new ShaderEffectVisual(effect);

        try
        {
            window.Render();

            Assert.False(effect.IsFailed, effect.LastError);

            var pixels = window.ReadPixels();
            var center = (((20 + 24) * 128) + (24 + 32)) * 4;

            Assert.InRange(pixels[center + 0], 0, 12);
            Assert.InRange(pixels[center + 1], 180, 205);
            Assert.InRange(pixels[center + 2], 54, 78);
            Assert.Equal(255, pixels[center + 3]);
        }
        finally
        {
            window.Content = null;
        }
    }

    private sealed class ShaderEffectVisual : FrameworkElement
    {
        private readonly WpfShaderEffectParams _effect;

        public ShaderEffectVisual(WpfShaderEffectParams effect)
        {
            _effect = effect;
            Width = 128f;
            Height = 96f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawWpfShaderEffect(_effect);
        }
    }
}
