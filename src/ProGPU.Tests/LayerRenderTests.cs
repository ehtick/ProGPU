using System;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class LayerRenderTests
{
    [Fact]
    public void CachedLayerCompositeIncludesVisualLocalTransform()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(160, 100);
        window.Content = new LayerHostVisual();

        try
        {
            window.Render();

            var pixels = window.ReadPixels();
            var background = ReadPixel(pixels, window.Width, x: 10, y: 10);
            var rotatedOnly = ReadPixel(pixels, window.Width, x: 100, y: 25);
            var unrotatedOnly = ReadPixel(pixels, window.Width, x: 85, y: 40);

            AssertRed(rotatedOnly);
            AssertColorNear(background, unrotatedOnly, tolerance: 12);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void CachedLayerCompositeAppliesVisualOpacityAndClip()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(100, 60);
        window.Content = new VisualCompositeScopeHost(new ClippedOpacityLayerVisual());

        try
        {
            window.Render();

            var pixels = window.ReadPixels();
            var visible = ReadPixel(pixels, window.Width, x: 25, y: 25);
            var clipped = ReadPixel(pixels, window.Width, x: 65, y: 25);

            AssertHalfRed(visible);
            AssertBlack(clipped);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void VisualCompositeScopeAppliesRetainedOpacityMask()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(100, 60);
        window.Content = new VisualCompositeScopeHost(new OpacityMaskedVisual());

        try
        {
            window.Render();

            var pixels = window.ReadPixels();
            var visible = ReadPixel(pixels, window.Width, x: 25, y: 25);
            var masked = ReadPixel(pixels, window.Width, x: 65, y: 25);

            AssertRed(visible);
            AssertBlack(masked);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void CachedLayerCompositeAppliesRetainedOpacityMask()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(100, 60);
        window.Content = new VisualCompositeScopeHost(new CachedOpacityMaskedVisual());

        try
        {
            window.Render();

            var pixels = window.ReadPixels();
            var visible = ReadPixel(pixels, window.Width, x: 25, y: 25);
            var masked = ReadPixel(pixels, window.Width, x: 65, y: 25);

            AssertRed(visible);
            AssertBlack(masked);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void CachedLayerHitTestCacheUsesLayerOwnerWithoutOffscreenCommands()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(100, 60);
        window.Content = new VisualCompositeScopeHost(new HitTestCachedLayerVisual());

        try
        {
            window.Render();
            window.Render();

            var index = window.Compositor.LastHitTestIndex;
            Assert.NotNull(index);
            var ownerPrimitives = index!.Primitives.Where(primitive => primitive.Id == 991).ToArray();
            var primitive = Assert.Single(ownerPrimitives);
            Assert.Equal(GpuHitTestPrimitiveKind.AxisAlignedBounds, primitive.Kind);
            Assert.Equal(new Vector2(10f, 5f), primitive.BoundsMin);
            Assert.Equal(new Vector2(90f, 55f), primitive.BoundsMax);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void PicturePlaybackContributesSubcommandsToHitTestCache()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(100, 60);
        window.Content = new PictureHitTestVisual();

        try
        {
            window.Render();

            var index = window.Compositor.LastHitTestIndex;
            Assert.NotNull(index);
            var primitive = Assert.Single(index!.Primitives, primitive => primitive.Id == 992);
            Assert.Equal(GpuHitTestPrimitiveKind.PathStroke, primitive.Kind);
            Assert.Equal(new Vector2(0f, 0f), primitive.BoundsMin);
            Assert.Equal(new Vector2(12f, 12f), primitive.BoundsMax);
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

    private static void AssertRed(RgbaPixel pixel)
    {
        Assert.True(pixel.R >= 220, $"Expected cached layer to render red, found {pixel}.");
        Assert.True(pixel.G <= 35, $"Expected cached layer green channel to stay low, found {pixel}.");
        Assert.True(pixel.B <= 35, $"Expected cached layer blue channel to stay low, found {pixel}.");
        Assert.Equal(255, pixel.A);
    }

    private static void AssertHalfRed(RgbaPixel pixel)
    {
        Assert.InRange(pixel.R, 115, 140);
        Assert.InRange(pixel.G, 0, 12);
        Assert.InRange(pixel.B, 0, 12);
        Assert.Equal(255, pixel.A);
    }

    private static void AssertBlack(RgbaPixel pixel)
    {
        Assert.InRange(pixel.R, 0, 12);
        Assert.InRange(pixel.G, 0, 12);
        Assert.InRange(pixel.B, 0, 12);
        Assert.Equal(255, pixel.A);
    }

    private static void AssertColorNear(RgbaPixel expected, RgbaPixel actual, int tolerance)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, tolerance);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, tolerance);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, tolerance);
        Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance);
    }

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class VisualCompositeScopeHost : FrameworkElement
    {
        private readonly FrameworkElement _child;
        private readonly SolidColorBrush _background = new(new Vector4(0f, 0f, 0f, 1f));

        public VisualCompositeScopeHost(FrameworkElement child)
        {
            _child = child;
            Width = 100f;
            Height = 60f;
            AddChild(_child);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _child.Measure(new Vector2(80f, 50f));
            return availableSize;
        }

        protected override void ArrangeOverride(Rect arrangeRect)
        {
            _child.Arrange(new Rect(10f, 5f, 80f, 50f));
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(_background, null, new Rect(0f, 0f, 100f, 60f));
        }
    }

    private sealed class LayerHostVisual : FrameworkElement
    {
        private readonly RotatedCachedLayerVisual _layer = new();

        public LayerHostVisual()
        {
            Width = 160f;
            Height = 100f;
            AddChild(_layer);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _layer.Measure(new Vector2(40f, 20f));
            return availableSize;
        }

        protected override void ArrangeOverride(Rect arrangeRect)
        {
            _layer.Arrange(new Rect(80f, 30f, 40f, 20f));
        }
    }

    private sealed class RotatedCachedLayerVisual : FrameworkElement
    {
        public RotatedCachedLayerVisual()
        {
            Width = 40f;
            Height = 20f;
            Rotation = MathF.PI * 0.5f;
            CacheAsLayer = true;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 40f, 20f));
        }
    }

    private sealed class ClippedOpacityLayerVisual : FrameworkElement
    {
        private readonly SolidColorBrush _red = new(new Vector4(1f, 0f, 0f, 1f));

        public ClippedOpacityLayerVisual()
        {
            Width = 80f;
            Height = 50f;
            CacheAsLayer = true;
            Opacity = 0.5f;
            ClipBounds = new Rect(0f, 0f, 40f, 50f);
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(_red, null, new Rect(0f, 0f, 80f, 50f));
        }
    }

    private class OpacityMaskedVisual : FrameworkElement
    {
        private readonly SolidColorBrush _red = new(new Vector4(1f, 0f, 0f, 1f));

        public OpacityMaskedVisual()
        {
            Width = 80f;
            Height = 50f;
            OpacityMask = new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f));
            OpacityMaskBounds = new Rect(0f, 0f, 40f, 50f);
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(_red, null, new Rect(0f, 0f, 80f, 50f));
        }
    }

    private sealed class CachedOpacityMaskedVisual : OpacityMaskedVisual
    {
        public CachedOpacityMaskedVisual()
        {
            CacheAsLayer = true;
        }
    }

    private sealed class HitTestCachedLayerVisual : FrameworkElement
    {
        private readonly SolidColorBrush _red = new(new Vector4(1f, 0f, 0f, 1f));

        public HitTestCachedLayerVisual()
        {
            Width = 80f;
            Height = 50f;
            CacheAsLayer = true;
            HitTestId = 991;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(_red, null, new Rect(0f, 0f, 80f, 50f));
        }
    }

    private sealed class PictureHitTestVisual : FrameworkElement
    {
        private readonly GpuPicture _picture;

        public PictureHitTestVisual()
        {
            Width = 100f;
            Height = 60f;

            _picture = new GpuPicture(
                [
                    new RenderCommand
                    {
                        Type = RenderCommandType.PushClip,
                        Rect = new Rect(0f, 0f, 12f, 12f)
                    },
                    new RenderCommand
                    {
                        Type = RenderCommandType.DrawPolyline,
                        HitTestId = 992,
                        Pen = new Pen(new SolidColorBrush(new Vector4(1f, 0f, 0f, 1f)), 2f),
                        PointBufferOffset = 0,
                        PointBufferCount = 3
                    },
                    new RenderCommand
                    {
                        Type = RenderCommandType.PopClip
                    }
                ],
                [
                    new Vector2(0f, 0f),
                    new Vector2(20f, 0f),
                    new Vector2(20f, 20f)
                ],
                [],
                [],
                []);
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawPicture(_picture);
        }
    }
}
