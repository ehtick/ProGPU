using Microsoft.UI.Xaml;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Silk.NET.WebGPU;
using Xunit;

namespace ProGPU.Tests;

public sealed class ImageEffectRenderTests
{
    [Fact]
    public void DrawImageWithEffectHonorsExplicitMaskTexture()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(160, 90);

        using var source = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Image Effect Source");
        source.WritePixels(new byte[] { 255, 0, 0, 255 });

        using var blackMask = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Image Effect Explicit Black Mask");
        blackMask.WritePixels(new byte[] { 0, 0, 0, 255 });

        window.Content = new ExplicitMaskImageEffectVisual(source, blackMask);

        try
        {
            window.Render();

            var pixels = window.ReadPixels();
            var unmasked = ReadPixel(pixels, window.Width, x: 40, y: 45);

            Assert.True(unmasked.R >= 220, $"Expected unmasked image effect to render red, found {unmasked}.");
            Assert.True(unmasked.G <= 35, $"Expected unmasked image effect to keep green low, found {unmasked}.");
            Assert.True(unmasked.B <= 35, $"Expected unmasked image effect to keep blue low, found {unmasked}.");
            Assert.Equal(255, unmasked.A);

            window.Content = new ExplicitMaskImageEffectVisual(source, blackMask, useExplicitMask: true);
            window.Render();

            pixels = window.ReadPixels();
            var masked = ReadPixel(pixels, window.Width, x: 40, y: 45);

            Assert.InRange(masked.R, 10, 35);
            Assert.InRange(masked.G, 10, 35);
            Assert.InRange(masked.B, 20, 45);
            Assert.Equal(255, masked.A);
        }
        finally
        {
            window.Content = null;
        }
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

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class ExplicitMaskImageEffectVisual : FrameworkElement
    {
        private readonly GpuTexture _source;
        private readonly GpuTexture _mask;
        private readonly bool _useExplicitMask;

        public ExplicitMaskImageEffectVisual(GpuTexture source, GpuTexture mask, bool useExplicitMask = false)
        {
            _source = source;
            _mask = mask;
            _useExplicitMask = useExplicitMask;
            Width = 160f;
            Height = 90f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawImageWithEffect(
                _source,
                new Rect(20f, 25f, 40f, 40f),
                maskTexture: _useExplicitMask ? _mask : null);
        }
    }
}
