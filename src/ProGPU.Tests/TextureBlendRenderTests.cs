using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.UI.Xaml;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Silk.NET.WebGPU;
using Xunit;

namespace ProGPU.Tests;

public sealed class TextureBlendRenderTests
{
    [Fact]
    public void DefaultUploadedTextureUsesSourceAlphaForColorBlend()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(32, 32);
        using var texture = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Default Straight Alpha Texture Blend Test");
        Assert.Equal(GpuTextureAlphaMode.Straight, texture.AlphaMode);

        texture.WritePixels<byte>(new byte[] { 200, 80, 20, 128 });
        window.Content = new TextureBlendVisual(texture);

        try
        {
            window.Render();

            var pixel = ReadPixel(window.ReadPixels(), window.Width, x: 16, y: 16);

            Assert.InRange(pixel.R, 95, 105);
            Assert.InRange(pixel.G, 35, 45);
            Assert.InRange(pixel.B, 5, 15);
            Assert.Equal(255, pixel.A);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void StraightAlphaTextureAppliesOpacityMaskOnce()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(32, 32);
        using var texture = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Straight Alpha Texture Mask Test",
            alphaMode: GpuTextureAlphaMode.Straight);
        texture.WritePixels<byte>(new byte[] { 200, 80, 20, 255 });
        window.Content = new StraightAlphaOpacityMaskTextureVisual(texture);

        try
        {
            window.Render();

            var pixel = ReadPixel(window.ReadPixels(), window.Width, x: 16, y: 16);

            Assert.InRange(pixel.R, 95, 105);
            Assert.InRange(pixel.G, 35, 45);
            Assert.InRange(pixel.B, 5, 15);
            Assert.Equal(255, pixel.A);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void PremultipliedTextureScalesRgbWhenOpacityIsApplied()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(32, 32);
        using var texture = new GpuTexture(
            window.Context,
            1,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Premultiplied Texture Opacity Test",
            alphaMode: GpuTextureAlphaMode.Premultiplied);
        texture.WritePixels<byte>(new byte[] { 128, 0, 0, 128 });
        window.Content = new PremultipliedOpacityTextureVisual(texture);

        try
        {
            window.Render();

            var pixel = ReadPixel(window.ReadPixels(), window.Width, x: 16, y: 16);

            Assert.InRange(pixel.R, 58, 70);
            Assert.InRange(pixel.G, 0, 8);
            Assert.InRange(pixel.B, 0, 8);
            Assert.Equal(255, pixel.A);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void ClippedTextureCommandUploadsTrimmedQuadWithPreservedSourceRectUv()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(40, 20);
        using var texture = new GpuTexture(
            window.Context,
            4,
            1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.TextureBinding | TextureUsage.CopyDst,
            "Clipped Texture Source Rect Test",
            alphaMode: GpuTextureAlphaMode.Straight);
        texture.WritePixels<byte>(
        [
            0, 0, 255, 255,
            255, 0, 0, 255,
            0, 255, 0, 255,
            255, 255, 0, 255
        ]);
        window.Content = new ClippedSourceRectTextureVisual(texture);

        try
        {
            window.Render();

            var textureVertices = GetTextureVertices(window.Compositor);
            var drawVertices = textureVertices.Skip(textureVertices.Count - 4).Take(4).ToArray();

            Assert.All(drawVertices, vertex => Assert.InRange(vertex.Position.X, 20f, 40f));
            Assert.All(drawVertices, vertex => Assert.InRange(vertex.Position.Y, 0f, 20f));
            Assert.Equal(0.5f, drawVertices.Min(static vertex => vertex.TexCoord.X), 3);
            Assert.Equal(0.75f, drawVertices.Max(static vertex => vertex.TexCoord.X), 3);

            var pixels = window.ReadPixels();
            var outside = ReadPixel(pixels, window.Width, x: 10, y: 10);
            var inside = ReadPixel(pixels, window.Width, x: 30, y: 10);

            Assert.Equal(new RgbaPixel(0, 0, 0, 255), outside);
            Assert.True(
                inside.G > 180 && inside.R < 80 && inside.B < 80,
                $"Expected preserved source-rect UVs to sample the green texel, got RGBA({inside.R}, {inside.G}, {inside.B}, {inside.A}).");
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

    private static List<VectorVertex> GetTextureVertices(Compositor compositor)
    {
        var field = typeof(Compositor).GetField("_textureVerticesList", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<List<VectorVertex>>(field.GetValue(compositor));
    }

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class TextureBlendVisual : FrameworkElement
    {
        private readonly GpuTexture _texture;

        public TextureBlendVisual(GpuTexture texture)
        {
            _texture = texture;
            Width = 32f;
            Height = 32f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 32f, 32f));
            context.DrawTexture(_texture, new Rect(0f, 0f, 32f, 32f));
        }
    }

    private sealed class ClippedSourceRectTextureVisual : FrameworkElement
    {
        private readonly GpuTexture _texture;

        public ClippedSourceRectTextureVisual(GpuTexture texture)
        {
            _texture = texture;
            Width = 40f;
            Height = 20f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 40f, 20f));
            context.PushClip(new Rect(20f, 0f, 20f, 20f));
            context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawTexture,
                Texture = _texture,
                Rect = new Rect(0f, 0f, 40f, 20f),
                SrcRect = new Rect(1f, 0f, 2f, 1f),
                TextureSamplingMode = TextureSamplingMode.Nearest
            });
            context.PopClip();
        }
    }

    private sealed class StraightAlphaOpacityMaskTextureVisual : FrameworkElement
    {
        private readonly GpuTexture _texture;

        public StraightAlphaOpacityMaskTextureVisual(GpuTexture texture)
        {
            _texture = texture;
            Width = 32f;
            Height = 32f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 32f, 32f));
            context.PushOpacityMask(
                new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)) { Opacity = 0.5f },
                new Rect(0f, 0f, 32f, 32f));
            context.DrawTexture(_texture, new Rect(0f, 0f, 32f, 32f));
            context.PopOpacityMask();
        }
    }

    private sealed class PremultipliedOpacityTextureVisual : FrameworkElement
    {
        private readonly GpuTexture _texture;

        public PremultipliedOpacityTextureVisual(GpuTexture texture)
        {
            _texture = texture;
            Width = 32f;
            Height = 32f;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 32f, 32f));
            context.PushOpacity(0.5f);
            context.DrawTexture(_texture, new Rect(0f, 0f, 32f, 32f));
            context.PopOpacity();
        }
    }
}
