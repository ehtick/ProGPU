using System;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Fonts.Inter;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class LayerRenderTests
{
    [Fact]
    public void UnchangedSceneReusesCompiledGpuBuffers()
    {
        using var window = new HeadlessWindow(64, 64);
        var visual = new SceneCacheVisual();
        window.Content = visual;

        try
        {
            window.Render();
            Assert.False(window.Compositor.Metrics.SceneCacheHit);

            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(1, visual.RenderCount);
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, 20, 20));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void SceneTransformHandleMovesRetainedPictureOnCompiledSceneCacheHit()
    {
        using var window = new HeadlessWindow(64, 32);
        using var visual = new SceneTransformPictureVisual();
        window.Content = visual;

        try
        {
            window.Render();
            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, x: 12, y: 8));

            visual.SceneTransform.Translation = new Vector2(30f, 0f);
            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(1, visual.RenderCount);
            var pixels = window.ReadPixels();
            var background = ReadPixel(pixels, window.Width, x: 2, y: 8);
            AssertColorNear(background, ReadPixel(pixels, window.Width, x: 12, y: 8), tolerance: 2);
            AssertRed(ReadPixel(pixels, window.Width, x: 32, y: 8));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void RetainedVisualTransformMovesWholeSubtreeOnCompiledSceneCacheHit()
    {
        using var window = new HeadlessWindow(64, 32);
        var visual = new RetainedTransformVisual();
        window.Content = visual;

        try
        {
            window.Render();
            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, x: 12, y: 8));

            visual.TransformHandle.Translation = new Vector2(30f, 0f);
            visual.InvalidateRetainedTransform();
            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(1, visual.RenderCount);
            var pixels = window.ReadPixels();
            var background = ReadPixel(pixels, window.Width, x: 2, y: 8);
            AssertColorNear(background, ReadPixel(pixels, window.Width, x: 12, y: 8), tolerance: 2);
            AssertRed(ReadPixel(pixels, window.Width, x: 32, y: 8));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void RetainedChildTransformMovesContentButKeepsExcludedOverlayFixed()
    {
        using var window = new HeadlessWindow(64, 32);
        var visual = new RetainedChildrenHost();
        window.Content = visual;

        try
        {
            window.Render();
            Assert.Equal(1, visual.Content.RenderCount);
            Assert.Equal(1, visual.Overlay.RenderCount);
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, x: 12, y: 8));
            var initialPixels = window.ReadPixels();
            Assert.True(
                CountGreenPixels(initialPixels) > 0,
                $"Expected fixed green overlay; {DescribeBrightestPixel(initialPixels, window.Width)}");
            Assert.Equal(0, FindGreenMinimumX(initialPixels, window.Width));

            visual.TransformHandle.Translation = new Vector2(30f, 0f);
            visual.InvalidateRetainedTransform();
            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            var pixels = window.ReadPixels();
            AssertGreen(ReadPixel(pixels, window.Width, x: 2, y: 8));
            AssertRed(ReadPixel(pixels, window.Width, x: 32, y: 8));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void IncompatibleRetainedChildFallsBackWithoutReusingBakedPlacement()
    {
        using var window = new HeadlessWindow(64, 32);
        var visual = new IncompatibleRetainedChildrenHost();
        window.Content = visual;

        try
        {
            window.Render();
            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, x: 12, y: 8));

            visual.TransformHandle.Translation = new Vector2(30f, 0f);
            visual.InvalidateRetainedTransform();
            window.Render();

            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(2, visual.Content.RenderCount);
            var pixels = window.ReadPixels();
            var background = ReadPixel(pixels, window.Width, x: 2, y: 8);
            AssertColorNear(background, ReadPixel(pixels, window.Width, x: 12, y: 8), tolerance: 2);
            AssertRed(ReadPixel(pixels, window.Width, x: 32, y: 8));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void IndependentSceneTransformHandlesKeepTheirOwnBindGroupsOnCacheHit()
    {
        using var window = new HeadlessWindow(64, 32);
        using var visual = new TwoSceneTransformPicturesVisual();
        window.Content = visual;

        try
        {
            window.Render();
            AssertRed(ReadPixel(window.ReadPixels(), window.Width, x: 7, y: 8));
            AssertGreen(ReadPixel(window.ReadPixels(), window.Width, x: 22, y: 8));

            visual.RedTransform.Translation = new Vector2(35f, 0f);
            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(1, visual.RenderCount);
            var pixels = window.ReadPixels();
            AssertGreen(ReadPixel(pixels, window.Width, x: 22, y: 8));
            AssertRed(ReadPixel(pixels, window.Width, x: 37, y: 8));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void SceneTransformHandleMovesAtlasTextThroughViewUniformOnCacheHit()
    {
        using var window = new HeadlessWindow(80, 40);
        using var visual = new SceneTransformTextVisual();
        window.Content = visual;

        try
        {
            window.Render();
            var initialPixels = window.ReadPixels();
            Assert.True(
                CountBrightPixels(initialPixels, window.Width, 8, 0, 24, 40) > 0,
                DescribeBrightestPixel(initialPixels, window.Width));

            visual.SceneTransform.Translation = new Vector2(40f, 0f);
            window.Render();

            Assert.True(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(1, visual.RenderCount);
            var pixels = window.ReadPixels();
            Assert.Equal(0, CountBrightPixels(pixels, window.Width, 8, 0, 24, 40));
            Assert.True(CountBrightPixels(pixels, window.Width, 38, 0, 54, 40) > 0);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void SceneTransformPictureRejectsClipCommandsInsteadOfRenderingStaleClipState()
    {
        using var window = new HeadlessWindow(64, 32);
        using var visual = new UnsupportedSceneTransformPictureVisual();
        window.Content = visual;

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => window.Render());
            Assert.Contains(nameof(RenderCommandType.PushClip), exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void VisualInvalidationRecompilesSceneAndUpdatesPixels()
    {
        using var window = new HeadlessWindow(64, 64);
        var visual = new SceneCacheVisual();
        window.Content = visual;

        try
        {
            window.Render();
            window.Render();
            Assert.True(window.Compositor.Metrics.SceneCacheHit);

            visual.SetColor(new Vector4(0f, 1f, 0f, 1f));
            window.Render();

            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal("Root version changed", window.Compositor.Metrics.SceneCacheMissReason);
            Assert.Equal(2, visual.RenderCount);
            AssertGreen(ReadPixel(window.ReadPixels(), window.Width, 20, 20));
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void ResizeInvalidatesCompiledSceneTarget()
    {
        using var window = new HeadlessWindow(64, 64);
        var visual = new SceneCacheVisual();
        window.Content = visual;

        try
        {
            window.Render();
            window.Render();
            Assert.True(window.Compositor.Metrics.SceneCacheHit);

            window.Resize(80, 64);
            window.Render();

            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal(2, visual.RenderCount);
        }
        finally
        {
            window.Content = null;
        }
    }

    [Fact]
    public void MutableDrawingVisualDisablesCompiledSceneReuse()
    {
        using var window = new HeadlessWindow(64, 64);
        window.Content = new DrawingVisualHost();

        try
        {
            window.Render();
            window.Render();

            Assert.False(window.Compositor.Metrics.SceneCacheHit);
            Assert.Equal("Drawing visuals active", window.Compositor.Metrics.SceneCacheMissReason);
        }
        finally
        {
            window.Content = null;
        }
    }

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
    public void CachedLayerHitTestCachePreservesLayerAndDescendantOwners()
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

            var childPrimitive = Assert.Single(index.Primitives, primitive => primitive.Id == 993);
            Assert.Equal(GpuHitTestPrimitiveKind.AxisAlignedBounds, childPrimitive.Kind);
            Assert.Equal(new Vector2(20f, 15f), childPrimitive.BoundsMin);
            Assert.Equal(new Vector2(50f, 35f), childPrimitive.BoundsMax);
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

    private static int CountBrightPixels(
        byte[] pixels,
        uint width,
        int minX,
        int minY,
        int maxX,
        int maxY)
    {
        int count = 0;
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                var pixel = ReadPixel(pixels, width, x, y);
                if (pixel.R > 60 && pixel.G > 60 && pixel.B > 60)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountGreenPixels(byte[] pixels)
    {
        int count = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 1] >= 200 && pixels[index] <= 50 && pixels[index + 2] <= 50)
            {
                count++;
            }
        }
        return count;
    }

    private static int FindGreenMinimumX(byte[] pixels, uint width)
    {
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 1] >= 200 && pixels[index] <= 50 && pixels[index + 2] <= 50)
            {
                return (index / 4) % (int)width;
            }
        }
        return -1;
    }

    private static string DescribeBrightestPixel(byte[] pixels, uint width)
    {
        int brightest = -1;
        int brightestX = -1;
        int brightestY = -1;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            int value = pixels[index] + pixels[index + 1] + pixels[index + 2];
            if (value <= brightest)
            {
                continue;
            }

            brightest = value;
            int pixelIndex = index / 4;
            brightestX = pixelIndex % (int)width;
            brightestY = pixelIndex / (int)width;
        }

        return $"Brightest RGB sum {brightest} at ({brightestX}, {brightestY}).";
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

    private static void AssertGreen(RgbaPixel pixel)
    {
        Assert.True(pixel.G >= 220, $"Expected scene to render green, found {pixel}.");
        Assert.True(pixel.R <= 35, $"Expected scene red channel to stay low, found {pixel}.");
        Assert.True(pixel.B <= 35, $"Expected scene blue channel to stay low, found {pixel}.");
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

    private sealed class SceneCacheVisual : FrameworkElement
    {
        private readonly SolidColorBrush _brush = new(new Vector4(1f, 0f, 0f, 1f));

        public int RenderCount { get; private set; }

        public SceneCacheVisual()
        {
            Width = 64f;
            Height = 64f;
        }

        public void SetColor(Vector4 color)
        {
            _brush.Color = color;
            Invalidate();
        }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawRectangle(_brush, null, new Rect(0f, 0f, 64f, 64f));
        }
    }

    private sealed class DrawingVisualHost : FrameworkElement
    {
        public DrawingVisualHost()
        {
            Width = 64f;
            Height = 64f;
            var drawing = new DrawingVisual { Size = new Vector2(64f, 64f) };
            drawing.Context.DrawRectangle(
                new SolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 64f, 64f));
            AddChild(drawing);
        }
    }

    private sealed class SceneTransformPictureVisual : FrameworkElement, IDisposable
    {
        private readonly GpuPicture _picture;

        public SceneTransformPictureVisual()
        {
            Width = 64f;
            Height = 32f;
            SceneTransform.Translation = new Vector2(10f, 0f);

            var recorder = new GpuPictureRecorder();
            var context = recorder.BeginRecording(new Rect(0f, 0f, 8f, 16f));
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
                null,
                new Rect(0f, 0f, 8f, 16f));
            _picture = recorder.EndRecording();
        }

        public SceneTransformHandle SceneTransform { get; } = new();

        public int RenderCount { get; private set; }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawPicture(_picture, SceneTransform);
        }

        public void Dispose() => _picture.Dispose();
    }

    private sealed class RetainedTransformVisual : FrameworkElement
    {
        private readonly SolidColorBrush _brush = new(new Vector4(1f, 0f, 0f, 1f));

        public RetainedTransformVisual()
        {
            Width = 64f;
            Height = 32f;
            TransformHandle.Translation = new Vector2(10f, 0f);
            RetainedTransform = TransformHandle;
        }

        public SceneTransformHandle TransformHandle { get; } = new();
        public int RenderCount { get; private set; }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawRectangle(_brush, null, new Rect(0f, 0f, 8f, 16f));
        }
    }

    private sealed class RetainedChildrenHost : FrameworkElement
    {
        public RetainedChildrenHost()
        {
            Width = 64f;
            Height = 32f;
            TransformHandle.Translation = new Vector2(10f, 0f);
            ChildrenRetainedTransform = TransformHandle;

            Content = new ColorRectVisual(new Vector4(1f, 0f, 0f, 1f));
            Overlay = new ColorRectVisual(new Vector4(0f, 1f, 0f, 1f))
            {
                Offset = new Vector2(20f, 0f),
                ExcludeFromParentRetainedTransform = true
            };
            AddChild(Content);
            AddChild(Overlay);
        }

        public SceneTransformHandle TransformHandle { get; } = new();
        public ColorRectVisual Content { get; }
        public ColorRectVisual Overlay { get; }
    }

    private sealed class IncompatibleRetainedChildrenHost : FrameworkElement
    {
        public IncompatibleRetainedChildrenHost()
        {
            Width = 64f;
            Height = 32f;
            TransformHandle.Translation = new Vector2(10f, 0f);
            ChildrenRetainedTransform = TransformHandle;
            Content = new ColorRectVisual(new Vector4(1f, 0f, 0f, 1f))
            {
                ClipBounds = new Rect(0f, 0f, 8f, 16f)
            };
            AddChild(Content);
        }

        public SceneTransformHandle TransformHandle { get; } = new();
        public ColorRectVisual Content { get; }
    }

    private sealed class ColorRectVisual(Vector4 color) : FrameworkElement
    {
        private readonly SolidColorBrush _brush = new(color);
        public int RenderCount { get; private set; }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawRectangle(_brush, null, new Rect(0f, 0f, 8f, 16f));
        }
    }

    private sealed class TwoSceneTransformPicturesVisual : FrameworkElement, IDisposable
    {
        private readonly GpuPicture _redPicture;
        private readonly GpuPicture _greenPicture;

        public TwoSceneTransformPicturesVisual()
        {
            Width = 64f;
            Height = 32f;
            RedTransform.Translation = new Vector2(5f, 0f);
            GreenTransform.Translation = new Vector2(20f, 0f);
            _redPicture = CreatePicture(new Vector4(1f, 0f, 0f, 1f));
            _greenPicture = CreatePicture(new Vector4(0f, 1f, 0f, 1f));
        }

        public SceneTransformHandle RedTransform { get; } = new();
        public SceneTransformHandle GreenTransform { get; } = new();
        public int RenderCount { get; private set; }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawPicture(_redPicture, RedTransform);
            context.DrawPicture(_greenPicture, GreenTransform);
        }

        public void Dispose()
        {
            _redPicture.Dispose();
            _greenPicture.Dispose();
        }

        private static GpuPicture CreatePicture(Vector4 color)
        {
            var recorder = new GpuPictureRecorder();
            var context = recorder.BeginRecording(new Rect(0f, 0f, 8f, 16f));
            context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0f, 0f, 8f, 16f));
            return recorder.EndRecording();
        }
    }

    private sealed class UnsupportedSceneTransformPictureVisual : FrameworkElement, IDisposable
    {
        private readonly GpuPicture _picture = new(
            [new RenderCommand { Type = RenderCommandType.PushClip, Rect = new Rect(0f, 0f, 8f, 8f) }],
            [],
            [],
            [],
            []);

        private readonly SceneTransformHandle _transform = new();

        public override void OnRender(DrawingContext context) => context.DrawPicture(_picture, _transform);

        public void Dispose() => _picture.Dispose();
    }

    private sealed class SceneTransformTextVisual : FrameworkElement, IDisposable
    {
        private readonly GpuPicture _picture;

        public SceneTransformTextVisual()
        {
            Width = 80f;
            Height = 40f;
            SceneTransform.Translation = new Vector2(10f, 0f);

            var recorder = new GpuPictureRecorder();
            var context = recorder.BeginRecording(new Rect(0f, 0f, 20f, 36f));
            context.DrawText(
                "I",
                InterFontFamily.Regular,
                28f,
                new SolidColorBrush(Vector4.One),
                new Vector2(0f, 30f));
            _picture = recorder.EndRecording();
        }

        public SceneTransformHandle SceneTransform { get; } = new();
        public int RenderCount { get; private set; }

        public override void OnRender(DrawingContext context)
        {
            RenderCount++;
            context.DrawPicture(_picture, SceneTransform);
        }

        public void Dispose() => _picture.Dispose();
    }

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
        private readonly FrameworkElement _child;

        public HitTestCachedLayerVisual()
        {
            Width = 80f;
            Height = 50f;
            CacheAsLayer = true;
            HitTestId = 991;

            _child = new FrameworkElement
            {
                Width = 30f,
                Height = 20f,
                HitTestId = 993
            };
            AddChild(_child);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _child.Measure(new Vector2(30f, 20f));
            return availableSize;
        }

        protected override void ArrangeOverride(Rect arrangeRect)
        {
            _child.Arrange(new Rect(10f, 10f, 30f, 20f));
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
