using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Vector;
using Silk.NET.WebGPU;

namespace SkiaSharp;

public class SKCanvas : IDisposable
{
    private DrawingContext _context;
    private readonly float _width;
    private readonly float _height;
    private readonly WgpuContext? _gpuContext;
    private readonly Action? _flush;
    private SKMatrix _currentMatrix = SKMatrix.Identity;
    private float _currentOpacity = 1f;
    private readonly List<GpuTexture> _ownedLayerTextures = new();
    private List<SKRect>? _cpuReadbackRegions;
    private static readonly Dictionary<WgpuContext, Compositor> s_compositorCache = new();

    static SKCanvas()
    {
        WgpuContext.Disposing += RemoveCachedCompositor;
    }

    public enum PushKind
    {
        RectClip,
        GeometryClip,
        Opacity
    }

    private readonly Stack<(SKMatrix Matrix, float Opacity, int PushedScopesCount)> _stateStack = new();
    private readonly Stack<PushKind> _pushedScopes = new();
    private readonly Stack<RenderCommand> _activeClipPushes = new();
    private readonly Stack<LayerFrame> _layerStack = new();

    private sealed class LayerFrame
    {
        public LayerFrame(
            DrawingContext parentContext,
            DrawingContext layerContext,
            SKPaint? paint,
            int stateDepth,
            SKRect bounds,
            SKMatrix boundsMatrix,
            RenderCommand[] activeClipPushes)
        {
            ParentContext = parentContext;
            LayerContext = layerContext;
            Paint = paint;
            StateDepth = stateDepth;
            Bounds = bounds;
            BoundsMatrix = boundsMatrix;
            ActiveClipPushes = activeClipPushes;
        }

        public DrawingContext ParentContext { get; }
        public DrawingContext LayerContext { get; }
        public SKPaint? Paint { get; }
        public int StateDepth { get; }
        public SKRect Bounds { get; }
        public SKMatrix BoundsMatrix { get; }
        public RenderCommand[] ActiveClipPushes { get; }
    }

    public SKMatrix TotalMatrix
    {
        get => _currentMatrix;
        set => SetMatrix(value);
    }

    public SKMatrix44 TotalMatrix44 => SKMatrix44.FromMatrix4x4(_currentMatrix.ToMatrix4x4());

    public SKCanvas(
        DrawingContext context,
        float width,
        float height,
        WgpuContext? gpuContext = null,
        Action? flush = null)
    {
        _context = context;
        _width = width;
        _height = height;
        _gpuContext = gpuContext;
        _flush = flush;
    }

    public void Clear()
    {
        Clear(SKColors.Transparent);
    }

    public void Clear(SKColor color)
    {
        var c = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        var brush = new SolidColorBrush(c);
        _context.PushBlendMode(GpuBlendMode.Src);
        _context.Commands.Add(new RenderCommand
        {
            Type = RenderCommandType.DrawRect,
            Rect = new Rect(0, 0, _width, _height),
            Brush = brush,
            Transform = Matrix4x4.Identity // Clear is always in identity screen space
        });
        _context.PopBlendMode();
    }

    public int Save()
    {
        var restoreCount = _stateStack.Count;
        _stateStack.Push((_currentMatrix, _currentOpacity, _pushedScopes.Count));
        return restoreCount;
    }

    public int SaveLayer(SKRect bounds, SKPaint paint)
    {
        var restoreCount = _stateStack.Count;
        Save();

        var parentContext = _context;
        var layerContext = new DrawingContext();
        _layerStack.Push(new LayerFrame(
            parentContext,
            layerContext,
            paint?.Clone(),
            _stateStack.Count,
            bounds,
            _currentMatrix,
            SnapshotActiveClipPushes()));
        _context = layerContext;

        return restoreCount;
    }

    public int SaveLayer(SKPaint paint)
    {
        return SaveLayer(new SKRect(0, 0, _width, _height), paint);
    }

    public void Restore()
    {
        if (_stateStack.Count > 0)
        {
            var layerFrame = _layerStack.Count > 0 && _layerStack.Peek().StateDepth == _stateStack.Count
                ? _layerStack.Pop()
                : null;

            var state = _stateStack.Pop();
            _currentMatrix = state.Matrix;
            _currentOpacity = state.Opacity;

            // Pop any clips or layers pushed in this save frame
            while (_pushedScopes.Count > state.PushedScopesCount)
            {
                var kind = _pushedScopes.Pop();
                switch (kind)
                {
                    case PushKind.RectClip:
                        _context.PopClip();
                        PopActiveClipScope();
                        break;
                    case PushKind.GeometryClip:
                        _context.PopGeometryClip();
                        PopActiveClipScope();
                        break;
                    case PushKind.Opacity:
                        _context.PopOpacity();
                        break;
                }
            }

            if (layerFrame != null)
            {
                RestoreLayer(layerFrame);
            }
        }
    }

    public void RestoreToCount(int count)
    {
        count = Math.Max(0, count);
        while (_stateStack.Count > count)
        {
            Restore();
        }
    }

    private void RestoreLayer(LayerFrame layerFrame)
    {
        _context = layerFrame.ParentContext;
        if (layerFrame.LayerContext.Commands.Count == 0 || !IsValidLayerBounds(layerFrame.Bounds))
        {
            layerFrame.LayerContext.Clear();
            return;
        }

        var pushedBlendMode = PushPaintBlendMode(layerFrame.Paint);
        var opacity = layerFrame.Paint?.Color.A / 255f ?? 1f;

        try
        {
            var pushedOpacity = false;
            if (opacity < 1f)
            {
                _context.PushOpacity(opacity);
                pushedOpacity = true;
            }

            try
            {
                var pushedLayerBoundsClip = PushLayerBoundsClip(_context, layerFrame);
                try
                {
                    DrawRestoredLayerTexture(layerFrame, RenderLayerToTexture(layerFrame));
                }
                finally
                {
                    if (pushedLayerBoundsClip)
                    {
                        _context.PopClip();
                    }
                }
            }
            finally
            {
                if (pushedOpacity)
                {
                    _context.PopOpacity();
                }
            }
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    private void DrawRestoredLayerTexture(LayerFrame layerFrame, GpuTexture texture)
    {
        var rect = new Rect(0f, 0f, _width, _height);
        var imageFilter = layerFrame.Paint?.ImageFilter;
        if (imageFilter is { IsBlur: true })
        {
            RetainLayerTextureForDeferredCommand(texture);
            _context.DrawImageWithEffect(
                texture,
                rect,
                blurSigma: MathF.Max(imageFilter.SigmaX, imageFilter.SigmaY));
            return;
        }

        if (imageFilter is { IsDropShadow: true })
        {
            texture = RenderFilteredLayerToTexture(
                texture,
                new DropShadowEffect(
                    MathF.Max(imageFilter.SigmaX, imageFilter.SigmaY),
                    new Vector2(imageFilter.Dx, imageFilter.Dy),
                    ToVector4(imageFilter.ShadowColor)));
        }

        DrawRestoredLayerTexture(texture, rect);
    }

    private void DrawRestoredLayerTexture(GpuTexture texture, Rect rect)
    {
        RetainLayerTextureForDeferredCommand(texture);
        _context.Commands.Add(new RenderCommand
        {
            Type = RenderCommandType.DrawTexture,
            Texture = texture,
            Rect = rect,
            Transform = Matrix4x4.Identity,
            TextureSamplingMode = TextureSamplingMode.Linear
        });
    }

    private void RetainLayerTextureForDeferredCommand(GpuTexture texture)
    {
        _context.RetainResource(texture);
        _ownedLayerTextures.Remove(texture);
    }

    private GpuTexture RenderFilteredLayerToTexture(GpuTexture sourceTexture, EffectBase effect)
    {
        var context = _gpuContext != null && !_gpuContext.IsDisposed
            ? _gpuContext
            : SKContextHelper.GetContext();
        var texture = new GpuTexture(
            context,
            (uint)_width,
            (uint)_height,
            TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            "SKCanvas SaveLayer Filtered Texture",
            alphaMode: GpuTextureAlphaMode.Premultiplied);

        var visual = new DrawingVisual
        {
            Size = new Vector2(_width, _height),
            Effect = effect
        };
        visual.Context.DrawTexture(sourceTexture, new Rect(0f, 0f, _width, _height));

        var textureRetained = false;
        try
        {
            try
            {
                GetCompositorForContext(context).RenderOffscreen(
                    visual,
                    (uint)_width,
                    (uint)_height,
                    texture,
                    padding: 0f,
                    dpiScale: 1f);
            }
            finally
            {
                visual.Context.Clear();
            }

            _ownedLayerTextures.Add(texture);
            textureRetained = true;
            return texture;
        }
        finally
        {
            if (!textureRetained)
            {
                texture.Dispose();
            }
        }
    }

    private static Vector4 ToVector4(SKColor color)
    {
        return new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    private GpuTexture RenderLayerToTexture(LayerFrame layerFrame)
    {
        var context = _gpuContext != null && !_gpuContext.IsDisposed
            ? _gpuContext
            : SKContextHelper.GetContext();
        var texture = new GpuTexture(
            context,
            (uint)_width,
            (uint)_height,
            TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            "SKCanvas SaveLayer Texture",
            alphaMode: GpuTextureAlphaMode.Premultiplied);

        var visual = new DrawingVisual { Size = new Vector2(_width, _height) };
        ReplayActiveClipPushes(visual.Context, layerFrame.ActiveClipPushes);
        var pushedLayerBoundsClip = PushLayerBoundsClip(visual.Context, layerFrame);
        visual.Context.Append(layerFrame.LayerContext);
        if (pushedLayerBoundsClip)
        {
            visual.Context.PopClip();
        }
        PopReplayedClipPushes(visual.Context, layerFrame.ActiveClipPushes);

        var textureRetained = false;
        try
        {
            try
            {
                GetCompositorForContext(context).RenderOffscreen(
                    visual,
                    (uint)_width,
                    (uint)_height,
                    texture,
                    padding: 0f,
                    dpiScale: 1f);
            }
            finally
            {
                visual.Context.Clear();
            }

            layerFrame.LayerContext.Clear();
            _ownedLayerTextures.Add(texture);
            textureRetained = true;
            return texture;
        }
        finally
        {
            if (!textureRetained)
            {
                layerFrame.LayerContext.Clear();
                texture.Dispose();
            }
        }
    }

    private bool PushLayerBoundsClip(DrawingContext context, LayerFrame layerFrame)
    {
        if (IsFullCanvasLayerBounds(layerFrame.Bounds))
        {
            return false;
        }

        context.Commands.Add(new RenderCommand
        {
            Type = RenderCommandType.PushClip,
            Rect = new Rect(
                layerFrame.Bounds.Left,
                layerFrame.Bounds.Top,
                layerFrame.Bounds.Width,
                layerFrame.Bounds.Height),
            Transform = layerFrame.BoundsMatrix.ToMatrix4x4()
        });
        return true;
    }

    private RenderCommand[] SnapshotActiveClipPushes()
    {
        var clips = _activeClipPushes.ToArray();
        Array.Reverse(clips);
        return clips;
    }

    private static void ReplayActiveClipPushes(DrawingContext context, IReadOnlyList<RenderCommand> clipPushes)
    {
        for (int i = 0; i < clipPushes.Count; i++)
        {
            context.Commands.Add(clipPushes[i]);
        }
    }

    private static void PopReplayedClipPushes(DrawingContext context, IReadOnlyList<RenderCommand> clipPushes)
    {
        for (int i = clipPushes.Count - 1; i >= 0; i--)
        {
            switch (clipPushes[i].Type)
            {
                case RenderCommandType.PushClip:
                    context.PopClip();
                    break;
                case RenderCommandType.PushGeometryClip:
                    context.PopGeometryClip();
                    break;
            }
        }
    }

    private void PushRectClipScope(SKRect rect, Matrix4x4 transform)
    {
        var command = new RenderCommand
        {
            Type = RenderCommandType.PushClip,
            Rect = new Rect(rect.Left, rect.Top, rect.Width, rect.Height),
            Transform = transform
        };
        _context.Commands.Add(command);
        _pushedScopes.Push(PushKind.RectClip);
        _activeClipPushes.Push(command);
    }

    private void PushGeometryClipScope(PathGeometry geometry, Matrix4x4 transform)
    {
        var command = new RenderCommand
        {
            Type = RenderCommandType.PushGeometryClip,
            Path = geometry,
            Transform = transform
        };
        _context.Commands.Add(command);
        _pushedScopes.Push(PushKind.GeometryClip);
        _activeClipPushes.Push(command);
    }

    private void PopActiveClipScope()
    {
        if (_activeClipPushes.Count > 0)
        {
            _activeClipPushes.Pop();
        }
    }

    private static bool IsValidLayerBounds(SKRect bounds)
    {
        return float.IsFinite(bounds.Left) &&
            float.IsFinite(bounds.Top) &&
            float.IsFinite(bounds.Right) &&
            float.IsFinite(bounds.Bottom) &&
            bounds.Width > 0f &&
            bounds.Height > 0f;
    }

    private bool IsFullCanvasLayerBounds(SKRect bounds)
    {
        return MathF.Abs(bounds.Left) < 0.0001f &&
            MathF.Abs(bounds.Top) < 0.0001f &&
            MathF.Abs(bounds.Width - _width) < 0.0001f &&
            MathF.Abs(bounds.Height - _height) < 0.0001f;
    }

    private static Compositor GetCompositorForContext(WgpuContext context)
    {
        lock (s_compositorCache)
        {
            if (!s_compositorCache.TryGetValue(context, out var compositor))
            {
                compositor = new Compositor(context, TextureFormat.Rgba8Unorm);
                s_compositorCache[context] = compositor;
            }

            return compositor;
        }
    }

    private static void RemoveCachedCompositor(WgpuContext context)
    {
        Compositor? compositor = null;
        lock (s_compositorCache)
        {
            if (s_compositorCache.TryGetValue(context, out compositor))
            {
                s_compositorCache.Remove(context);
            }
        }

        compositor?.Dispose();
    }

    private static GpuBlendMode MapBlendMode(SKBlendMode blendMode)
    {
        return blendMode switch
        {
            SKBlendMode.Clear => GpuBlendMode.Clear,
            SKBlendMode.Src => GpuBlendMode.Src,
            SKBlendMode.Dst => GpuBlendMode.Dst,
            SKBlendMode.SrcIn => GpuBlendMode.SrcIn,
            SKBlendMode.DstIn => GpuBlendMode.DstIn,
            SKBlendMode.SrcOut => GpuBlendMode.SrcOut,
            SKBlendMode.DstOut => GpuBlendMode.DstOut,
            SKBlendMode.SrcATop => GpuBlendMode.SrcAtop,
            SKBlendMode.DstATop => GpuBlendMode.DstAtop,
            SKBlendMode.Xor => GpuBlendMode.Xor,
            SKBlendMode.DstOver => GpuBlendMode.DstOver,
            SKBlendMode.Plus => GpuBlendMode.Plus,
            SKBlendMode.Screen => GpuBlendMode.Screen,
            SKBlendMode.Multiply => GpuBlendMode.Multiply,
            SKBlendMode.Darken => GpuBlendMode.Darken,
            SKBlendMode.Lighten => GpuBlendMode.Lighten,
            SKBlendMode.Exclusion => GpuBlendMode.Exclusion,
            SKBlendMode.Overlay => GpuBlendMode.Overlay,
            SKBlendMode.ColorDodge => GpuBlendMode.ColorDodge,
            SKBlendMode.ColorBurn => GpuBlendMode.ColorBurn,
            SKBlendMode.HardLight => GpuBlendMode.HardLight,
            SKBlendMode.SoftLight => GpuBlendMode.SoftLight,
            SKBlendMode.Difference => GpuBlendMode.Difference,
            SKBlendMode.Hue => GpuBlendMode.Hue,
            SKBlendMode.Saturation => GpuBlendMode.Saturation,
            SKBlendMode.Color => GpuBlendMode.Color,
            SKBlendMode.Luminosity => GpuBlendMode.Luminosity,
            _ => GpuBlendMode.SrcOver
        };
    }

    private bool PushPaintBlendMode(SKPaint? paint)
    {
        var blendMode = MapBlendMode(paint?.BlendMode ?? SKBlendMode.SrcOver);
        if (blendMode == GpuBlendMode.SrcOver)
        {
            return false;
        }

        _context.PushBlendMode(blendMode);
        return true;
    }

    private void PopPaintBlendMode(bool pushedBlendMode)
    {
        if (pushedBlendMode)
        {
            _context.PopBlendMode();
        }
    }

    public void Translate(float dx, float dy)
    {
        _currentMatrix.TransX += dx * _currentMatrix.ScaleX + dy * _currentMatrix.SkewX;
        _currentMatrix.TransY += dx * _currentMatrix.SkewY + dy * _currentMatrix.ScaleY;
    }

    public void Scale(float sx, float sy)
    {
        _currentMatrix.ScaleX *= sx;
        _currentMatrix.SkewY *= sx;
        _currentMatrix.SkewX *= sy;
        _currentMatrix.ScaleY *= sy;
    }

    public void SetMatrix(SKMatrix matrix)
    {
        _currentMatrix = matrix;
    }

    public void SetMatrix(SKMatrix44 matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        _currentMatrix = SKMatrix.FromMatrix4x4(matrix.ToMatrix4x4());
    }

    public void ResetMatrix()
    {
        _currentMatrix = SKMatrix.Identity;
    }

    public void ClipRect(SKRect rect, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = true)
    {
        if (operation == SKClipOperation.Difference)
        {
            var excluded = CreateRectGeometry(rect).CreateTransformed(_currentMatrix.ToMatrix4x4());
            PushGeometryClipScope(CreateCanvasDifferenceGeometry(excluded), Matrix4x4.Identity);
            return;
        }

        PushRectClipScope(rect, _currentMatrix.ToMatrix4x4());
    }

    public void ClipPath(SKPath? path, SKClipOperation operation = SKClipOperation.Intersect, bool antialias = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (operation == SKClipOperation.Difference)
        {
            if (IsInverseFillType(path.FillType))
            {
                PushGeometryClipScope(path.Geometry, _currentMatrix.ToMatrix4x4());
            }
            else
            {
                var excluded = path.Geometry.CreateTransformed(_currentMatrix.ToMatrix4x4());
                PushGeometryClipScope(CreateCanvasDifferenceGeometry(excluded), Matrix4x4.Identity);
            }
            return;
        }

        if (IsInverseFillType(path.FillType))
        {
            var excluded = path.Geometry.CreateTransformed(_currentMatrix.ToMatrix4x4());
            PushGeometryClipScope(CreateCanvasDifferenceGeometry(excluded), Matrix4x4.Identity);
            return;
        }

        var transform = _currentMatrix.ToMatrix4x4();
        if (IsAxisAligned2DTransform(transform) && TryGetRectGeometry(path.Geometry, out var rect))
        {
            PushRectClipScope(rect, transform);
            return;
        }

        PushGeometryClipScope(path.Geometry, transform);
    }

    public void ClipRoundRect(
        SKRoundRect? rect,
        SKClipOperation operation = SKClipOperation.Intersect,
        bool antialias = false)
    {
        ArgumentNullException.ThrowIfNull(rect);
        using var path = new SKPath();
        path.AddRoundRect(rect);
        ClipPath(path, operation, antialias);
    }

    public void ClipRegion(SKRegion region, SKClipOperation operation = SKClipOperation.Intersect)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (operation == SKClipOperation.Intersect && _context.Commands.Count == 0 && _cpuReadbackRegions == null)
        {
            _cpuReadbackRegions = new List<SKRect>(region.Rects.Count);
            foreach (var rect in region.Rects)
            {
                _cpuReadbackRegions.Add(MapRectToBounds(
                    new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                    _currentMatrix.ToMatrix4x4()));
            }
        }

        using var path = CreateRegionPath(region);
        ClipPath(path, operation, antialias: false);
    }

    internal SKRect[]? TakeCpuReadbackRegions()
    {
        if (_cpuReadbackRegions == null)
        {
            return null;
        }

        var regions = _cpuReadbackRegions.ToArray();
        _cpuReadbackRegions = null;
        return regions;
    }

    private static SKRect MapRectToBounds(SKRect rect, Matrix4x4 matrix)
    {
        var topLeft = Vector2.Transform(new Vector2(rect.Left, rect.Top), matrix);
        var topRight = Vector2.Transform(new Vector2(rect.Right, rect.Top), matrix);
        var bottomRight = Vector2.Transform(new Vector2(rect.Right, rect.Bottom), matrix);
        var bottomLeft = Vector2.Transform(new Vector2(rect.Left, rect.Bottom), matrix);
        return new SKRect(
            MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X)),
            MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y)),
            MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X)),
            MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y)));
    }

    public void DrawPicture(SKPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        _context.DrawPictureTransformed(picture.Picture, _currentMatrix.ToMatrix4x4());
    }

    public void DrawPicture(SKPicture picture, SKPaint? paint)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var pushedBlendMode = PushPaintBlendMode(paint);
        var pushedOpacity = false;
        try
        {
            var opacity = paint?.Color.A / 255f ?? 1f;
            if (opacity < 1f)
            {
                _context.PushOpacity(opacity);
                pushedOpacity = true;
            }

            DrawPicture(picture);
        }
        finally
        {
            if (pushedOpacity)
            {
                _context.PopOpacity();
            }

            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawLine(float x0, float y0, float x1, float y1, SKPaint paint)
    {
        using var path = new SKPath();
        path.MoveTo(x0, y0);
        path.LineTo(x1, y1);
        DrawPath(path, paint);
    }

    public void DrawRect(float x, float y, float w, float h, SKPaint paint)
    {
        var rect = new SKRect(x, y, x + w, y + h);
        if (TryDrawSpecialShader(CreateRectGeometry(rect), rect, paint))
        {
            return;
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            var brush = paint.ToBrush();
            var pen = paint.ToLocalPen(GetCurrentStrokeScale());
            _context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawRect,
                Rect = new Rect(x, y, w, h),
                Brush = brush,
                Pen = pen,
                Transform = _currentMatrix.ToMatrix4x4(),
                IsEdgeAliased = !paint.IsAntialias,
                IsPenThicknessLocal = true
            });
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawRect(SKRect rect, SKPaint paint) => DrawRect(rect.Left, rect.Top, rect.Width, rect.Height, paint);

    public void DrawRoundRect(SKRoundRect? rect, SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(rect);
        if (HasSpecialShader(paint.Shader))
    {
            using var clipPath = new SKPath();
            clipPath.AddRoundRect(rect);
            if (TryDrawSpecialShader(clipPath.Geometry, rect.Rect, paint))
            {
                return;
            }
        }

        if (!TryGetUniformRadii(rect, out var radiusX, out var radiusY))
        {
            using var path = new SKPath();
            path.AddRoundRect(rect);
            DrawPath(path, paint);
            return;
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            var brush = paint.ToBrush();
            var pen = paint.ToLocalPen(GetCurrentStrokeScale());
            _context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawRoundedRect,
                Rect = new Rect(rect.Rect.Left, rect.Rect.Top, rect.Rect.Width, rect.Rect.Height),
                RadiusX = radiusX,
                RadiusY = radiusY,
                Brush = brush,
                Pen = pen,
                Transform = _currentMatrix.ToMatrix4x4(),
                IsEdgeAliased = !paint.IsAntialias,
                IsPenThicknessLocal = true
            });
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawRoundRect(SKRect rect, float rx, float ry, SKPaint paint)
    {
        DrawRoundRect(new SKRoundRect(rect, rx, ry), paint);
    }

    private static bool TryGetUniformRadii(SKRoundRect rect, out float radiusX, out float radiusY)
    {
        radiusX = rect.CornerRadii[0].X;
        radiusY = rect.CornerRadii[0].Y;
        for (int i = 1; i < rect.CornerRadii.Length; i++)
        {
            if (MathF.Abs(rect.CornerRadii[i].X - radiusX) > 0.0001f ||
                MathF.Abs(rect.CornerRadii[i].Y - radiusY) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    public void DrawOval(SKRect rect, SKPaint paint)
    {
        if (HasSpecialShader(paint.Shader))
        {
            using var clipPath = new SKPath();
            AddOvalPath(clipPath, rect);
            if (TryDrawSpecialShader(clipPath.Geometry, rect, paint))
            {
                return;
            }
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            var brush = paint.ToBrush();
            var pen = paint.ToLocalPen(GetCurrentStrokeScale());
            _context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawEllipse,
                Position2 = new Vector2(rect.MidX, rect.MidY),
                RadiusX = rect.Width / 2f,
                RadiusY = rect.Height / 2f,
                Brush = brush,
                Pen = pen,
                Transform = _currentMatrix.ToMatrix4x4(),
                IsEdgeAliased = !paint.IsAntialias,
                IsPenThicknessLocal = true
            });
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawCircle(float cx, float cy, float radius, SKPaint paint)
    {
        if (HasSpecialShader(paint.Shader))
        {
            using var clipPath = new SKPath();
            clipPath.AddCircle(cx, cy, radius);
            var bounds = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
            if (TryDrawSpecialShader(clipPath.Geometry, bounds, paint))
            {
                return;
            }
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            var brush = paint.ToBrush();
            var pen = paint.ToLocalPen(GetCurrentStrokeScale());
            _context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawCircle,
                Position2 = new Vector2(cx, cy),
                RadiusX = radius,
                Brush = brush,
                Pen = pen,
                Transform = _currentMatrix.ToMatrix4x4(),
                IsEdgeAliased = !paint.IsAntialias,
                IsPenThicknessLocal = true
            });
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawRoundRectDifference(SKRoundRect outer, SKRoundRect inner, SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        using var outerPath = new SKPath();
        using var innerPath = new SKPath();
        outerPath.AddRoundRect(outer);
        innerPath.AddRoundRect(inner);
        using var difference = outerPath.Op(innerPath, SKPathOp.Difference);
        DrawPath(difference, paint);
    }

    public void DrawRegion(SKRegion region, SKPaint paint)
    {
        ArgumentNullException.ThrowIfNull(region);
        using var path = CreateRegionPath(region);
        DrawPath(path, paint);
    }

    public void DrawPaint(SKPaint paint)
    {
        DrawRect(new SKRect(0f, 0f, _width, _height), paint);
    }

    public void DrawPath(SKPath path, SKPaint paint)
    {
        if (TryDrawSpecialShader(path.Geometry, path.Bounds, paint))
        {
            return;
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            var brush = paint.ToBrush();
            var pen = paint.ToPen(GetCurrentStrokeScale());

            if (IsInverseFillType(path.FillType))
            {
                if (brush != null)
                {
                    var excluded = path.Geometry.CreateTransformed(_currentMatrix.ToMatrix4x4());
                    AddDrawPathCommand(CreateCanvasDifferenceGeometry(excluded), brush, null, Matrix4x4.Identity, !paint.IsAntialias);
                }

                if (pen != null)
                {
                    AddDrawPathCommand(path.Geometry, null, pen, _currentMatrix.ToMatrix4x4(), !paint.IsAntialias);
                }

                return;
            }

            AddDrawPathCommand(path.Geometry, brush, pen, _currentMatrix.ToMatrix4x4(), !paint.IsAntialias);
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    private void AddDrawPathCommand(
        PathGeometry path,
        Brush? brush,
        Pen? pen,
        Matrix4x4 transform,
        bool isEdgeAliased = false)
    {
        _context.Commands.Add(new RenderCommand
        {
            Type = RenderCommandType.DrawPath,
            Path = path,
            Brush = brush,
            Pen = pen,
            Transform = transform,
            IsEdgeAliased = isEdgeAliased
        });
    }

    private float GetCurrentStrokeScale()
    {
        return TransformMetrics.GetStrokeScale(_currentMatrix.ToMatrix4x4());
    }

    private PathGeometry CreateCanvasDifferenceGeometry(PathGeometry excluded)
    {
        return new PathGeometry
        {
            IsCombined = true,
            PathA = CreateCanvasBoundsGeometry(),
            PathB = excluded,
            Op = (int)SKPathOp.Difference,
            FillRule = FillRule.Nonzero
        };
    }

    private PathGeometry CreateCanvasBoundsGeometry()
    {
        return CreateRectGeometry(new SKRect(0f, 0f, _width, _height));
    }

    private static PathGeometry CreateRectGeometry(SKRect rect)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure(new Vector2(rect.Left, rect.Top), isClosed: true);
        figure.Segments.Add(new LineSegment(new Vector2(rect.Right, rect.Top)));
        figure.Segments.Add(new LineSegment(new Vector2(rect.Right, rect.Bottom)));
        figure.Segments.Add(new LineSegment(new Vector2(rect.Left, rect.Bottom)));
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static bool TryGetRectGeometry(PathGeometry geometry, out SKRect rect)
    {
        rect = default;
        if (geometry.IsCombined || geometry.Figures.Count != 1)
        {
            return false;
        }

        var figure = geometry.Figures[0];
        if (!figure.IsClosed || figure.Segments.Count is < 3 or > 8)
        {
            return false;
        }

        Span<Vector2> points = stackalloc Vector2[9];
        var count = 1;
        points[0] = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            Vector2 point;
            if (segment is LineSegment line)
            {
                point = line.Point;
            }
            else if (segment is ArcSegment arc &&
                     (NearlyEqual(arc.Size.X, 0f) || NearlyEqual(arc.Size.Y, 0f)))
            {
                point = arc.Point;
            }
            else
            {
                return false;
            }

            if (!NearlyEqual(point, points[count - 1]))
            {
                points[count++] = point;
            }
        }

        if (count > 1 && NearlyEqual(points[count - 1], points[0]))
        {
            count--;
        }

        if (count != 4)
        {
            return false;
        }

        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = minX;
        var maxY = minY;
        for (var i = 1; i < count; i++)
        {
            minX = MathF.Min(minX, points[i].X);
            minY = MathF.Min(minY, points[i].Y);
            maxX = MathF.Max(maxX, points[i].X);
            maxY = MathF.Max(maxY, points[i].Y);
        }

        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % count];
            var isCorner = (NearlyEqual(current.X, minX) || NearlyEqual(current.X, maxX))
                && (NearlyEqual(current.Y, minY) || NearlyEqual(current.Y, maxY));
            var isAxisAlignedEdge = NearlyEqual(current.X, next.X) != NearlyEqual(current.Y, next.Y);
            if (!isCorner || !isAxisAlignedEdge)
            {
                return false;
            }
        }

        rect = new SKRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool IsAxisAligned2DTransform(Matrix4x4 transform)
    {
        const float epsilon = 0.0001f;
        return MathF.Abs(transform.M12) <= epsilon && MathF.Abs(transform.M21) <= epsilon;
    }

    private static bool NearlyEqual(Vector2 left, Vector2 right)
    {
        return NearlyEqual(left.X, right.X) && NearlyEqual(left.Y, right.Y);
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.0001f;
    }

    private static SKPath CreateRegionPath(SKRegion region)
    {
        var path = new SKPath();
        foreach (var rect in region.Rects)
        {
            path.AddRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));
        }

        return path;
    }

    private static void AddOvalPath(SKPath path, SKRect rect)
    {
        var radiusX = rect.Width / 2f;
        var radiusY = rect.Height / 2f;
        var centerX = rect.MidX;
        var centerY = rect.MidY;
        path.MoveTo(centerX - radiusX, centerY);
        path.ArcTo(radiusX, radiusY, 0f, SKPathArcSize.Large, SKPathDirection.Clockwise, centerX + radiusX, centerY);
        path.ArcTo(radiusX, radiusY, 0f, SKPathArcSize.Large, SKPathDirection.Clockwise, centerX - radiusX, centerY);
        path.Close();
    }

    private bool TryDrawSpecialShader(PathGeometry clipGeometry, SKRect targetBounds, SKPaint paint)
    {
        var shader = paint.Shader;
        if (shader == null || (shader.Picture == null && shader.Image == null && shader.Composed == null))
        {
            return false;
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        var pushedOpacity = false;
        try
        {
            var opacity = paint.Color.A / 255f;
            if (opacity < 1f)
            {
                _context.PushOpacity(opacity);
                pushedOpacity = true;
            }

            if (paint.Style == SKPaintStyle.Fill)
            {
                DrawShaderLayer(shader, clipGeometry, targetBounds, paint, drawAsFill: false);
            }
            else
            {
                using var sourcePath = new SKPath();
                sourcePath.Geometry.FillRule = clipGeometry.FillRule;
                foreach (var figure in clipGeometry.Figures)
                {
                    sourcePath.Geometry.Figures.Add(figure);
                }

                using var fillPath = new SKPath();
                if (paint.GetFillPath(sourcePath, fillPath))
                {
                    DrawShaderLayer(shader, fillPath.Geometry, fillPath.Bounds, paint, drawAsFill: true);
                }
            }
        }
        finally
        {
            if (pushedOpacity)
            {
                _context.PopOpacity();
            }

            PopPaintBlendMode(pushedBlendMode);
        }

        return true;
    }

    private static bool HasSpecialShader(SKShader? shader)
    {
        return shader != null
            && (shader.Picture != null || shader.Image != null || shader.Composed != null);
    }

    private void DrawShaderLayer(
        SKShader shader,
        PathGeometry clipGeometry,
        SKRect targetBounds,
        SKPaint paint,
        bool drawAsFill)
    {
        if (shader.Composed is { } composed)
        {
            if (TryCreateComposedConicalBrush(composed, out var conicalBrush))
            {
                var style = drawAsFill ? SKPaintStyle.Fill : paint.Style;
                var conicalFill = style == SKPaintStyle.Stroke ? null : conicalBrush;
                var conicalPen = style == SKPaintStyle.Fill
                    ? null
                    : paint.ToPen(conicalBrush, GetCurrentStrokeScale());
                AddDrawPathCommand(
                    clipGeometry,
                    conicalFill,
                    conicalPen,
                    _currentMatrix.ToMatrix4x4(),
                    !paint.IsAntialias);
                return;
            }

            DrawShaderLayer(composed.Destination, clipGeometry, targetBounds, paint, drawAsFill);
            DrawShaderLayer(composed.Source, clipGeometry, targetBounds, paint, drawAsFill);
            return;
        }

        if (shader.Picture is { } picture)
        {
            DrawTiledPicture(
                picture.Picture,
                picture.TileRect,
                picture.TileModeX,
                picture.TileModeY,
                picture.LocalMatrix,
                shader.ColorFilter,
                clipGeometry,
                targetBounds);
            return;
        }

        if (shader.Image is { } image)
        {
            DrawTiledImage(image, shader.ColorFilter, clipGeometry, targetBounds);
            return;
        }

        var brush = shader.ToBrush();
        var shaderStyle = drawAsFill ? SKPaintStyle.Fill : paint.Style;
        var fill = shaderStyle == SKPaintStyle.Stroke ? null : brush;
        var pen = shaderStyle == SKPaintStyle.Fill
            ? null
            : paint.ToPen(brush, GetCurrentStrokeScale());
        AddDrawPathCommand(
            clipGeometry,
            fill,
            pen,
            _currentMatrix.ToMatrix4x4(),
            !paint.IsAntialias);
    }

    private static bool TryCreateComposedConicalBrush(
        SKShader.ComposedShaderData composed,
        out TwoPointConicalGradientBrush brush)
    {
        brush = null!;
        Brush destination;
        Brush source;
        try
        {
            destination = composed.Destination.ToBrush();
            source = composed.Source.ToBrush();
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (destination is not SolidColorBrush solid || source is not TwoPointConicalGradientBrush conical)
        {
            return false;
        }

        var destinationColor = ApplyOpacity(solid.Color, solid.Opacity);
        for (var i = 0; i < conical.Stops.Length; i++)
        {
            var stop = conical.Stops[i];
            stop.Color = SourceOver(ApplyOpacity(stop.Color, conical.Opacity), destinationColor);
            conical.Stops[i] = stop;
        }

        conical.Opacity = 1f;
        conical.OutsideColor = destinationColor;
        brush = conical;
        return true;
    }

    private static Vector4 ApplyOpacity(Vector4 color, float opacity)
    {
        color.W *= Math.Clamp(opacity, 0f, 1f);
        return color;
    }

    private static Vector4 SourceOver(Vector4 source, Vector4 destination)
    {
        var sourceAlpha = Math.Clamp(source.W, 0f, 1f);
        var destinationAlpha = Math.Clamp(destination.W, 0f, 1f);
        var alpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
        if (alpha <= 0f)
        {
            return Vector4.Zero;
        }

        var rgb = (new Vector3(source.X, source.Y, source.Z) * sourceAlpha
            + new Vector3(destination.X, destination.Y, destination.Z)
            * destinationAlpha * (1f - sourceAlpha)) / alpha;
        return new Vector4(rgb, alpha);
    }

    private void DrawTiledPicture(
        GpuPicture picture,
        SKRect tileRect,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix shaderMatrix,
        SKColorFilter? colorFilter,
        PathGeometry clipGeometry,
        SKRect targetBounds)
    {
        if (tileRect.Width <= 0f || tileRect.Height <= 0f || targetBounds.Width <= 0f || targetBounds.Height <= 0f)
        {
            return;
        }

        var localMatrix = shaderMatrix.ToMatrix4x4();
        var texture = RasterizePictureTile(
            picture,
            tileRect,
            localMatrix * _currentMatrix.ToMatrix4x4());
        GetPictureShaderBounds(targetBounds, localMatrix, out var minX, out var minY, out var maxX, out var maxY);
        GetTileRange(tileModeX, minX, maxX, tileRect.Width, out var startX, out var endX);
        GetTileRange(tileModeY, minY, maxY, tileRect.Height, out var startY, out var endY);
        LimitTileRange(ref startX, ref endX);
        LimitTileRange(ref startY, ref endY);
        _context.PushGeometryClip(clipGeometry, _currentMatrix.ToMatrix4x4());
        var pushedOpacity = PushShaderColorFilterOpacity(colorFilter);
        try
        {
            for (var y = startY; y <= endY; y++)
            {
                for (var x = startX; x <= endX; x++)
                {
                    var placement = CreateTilePlacement(tileRect, x, y, tileModeX, tileModeY);
                    var pictureTransform = placement * localMatrix * _currentMatrix.ToMatrix4x4();
                    _context.Commands.Add(new RenderCommand
                    {
                        Type = RenderCommandType.DrawTexture,
                        Texture = texture,
                        Rect = new Rect(tileRect.Left, tileRect.Top, tileRect.Width, tileRect.Height),
                        SrcRect = new Rect(0f, 0f, texture.Width, texture.Height),
                        Transform = pictureTransform,
                        TextureSamplingMode = TextureSamplingMode.Nearest
                    });
                }
            }
        }
        finally
        {
            if (pushedOpacity)
            {
                _context.PopOpacity();
            }

            _context.PopGeometryClip();
        }
    }

    private GpuTexture RasterizePictureTile(
        GpuPicture picture,
        SKRect tileRect,
        Matrix4x4 pictureToDevice)
    {
        const float maxPictureTileArea = 2048f * 2048f;
        var scaleX = GetAxisScale(pictureToDevice, Vector2.UnitX);
        var scaleY = GetAxisScale(pictureToDevice, Vector2.UnitY);
        var scaledWidth = tileRect.Width * scaleX;
        var scaledHeight = tileRect.Height * scaleY;
        var scaledArea = scaledWidth * scaledHeight;
        if (scaledArea > maxPictureTileArea)
        {
            var clampScale = MathF.Sqrt(maxPictureTileArea / scaledArea);
            scaledWidth *= clampScale;
            scaledHeight *= clampScale;
        }

        const uint maxPictureTileDimension = 8192;
        var width = (uint)Math.Clamp(Math.Ceiling(scaledWidth), 1d, maxPictureTileDimension);
        var height = (uint)Math.Clamp(Math.Ceiling(scaledHeight), 1d, maxPictureTileDimension);
        var context = _gpuContext != null && !_gpuContext.IsDisposed
            ? _gpuContext
            : SKContextHelper.GetContext();
        var texture = new GpuTexture(
            context,
            width,
            height,
            TextureFormat.Rgba8Unorm,
            TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            "SKPicture Shader Tile",
            alphaMode: GpuTextureAlphaMode.Premultiplied);

        var rasterTransform = Matrix4x4.CreateTranslation(-tileRect.Left, -tileRect.Top, 0f)
            * Matrix4x4.CreateScale(width / tileRect.Width, height / tileRect.Height, 1f);
        var visual = new DrawingVisual { Size = new Vector2(width, height) };
        visual.Context.DrawPictureTransformed(picture, rasterTransform);

        var retained = false;
        try
        {
            try
            {
                GetCompositorForContext(context).RenderOffscreen(
                    visual,
                    width,
                    height,
                    texture,
                    padding: 0f,
                    dpiScale: 1f,
                    clearColor: Vector4.Zero);
            }
            finally
            {
                visual.Context.Clear();
            }

            _context.RetainResource(texture);
            retained = true;
            return texture;
        }
        finally
        {
            if (!retained)
            {
                texture.Dispose();
            }
        }
    }

    private static float GetAxisScale(Matrix4x4 transform, Vector2 axis)
    {
        var transformed = Vector2.TransformNormal(axis, transform);
        var scale = transformed.Length();
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private void DrawTiledImage(
        SKShader.ImageShaderData imageShader,
        SKColorFilter? colorFilter,
        PathGeometry clipGeometry,
        SKRect targetBounds)
    {
        var tileRect = imageShader.TileRect;
        if (tileRect.Width <= 0f || tileRect.Height <= 0f || targetBounds.Width <= 0f || targetBounds.Height <= 0f)
        {
            return;
        }

        var localMatrix = imageShader.LocalMatrix.ToMatrix4x4();
        GetPictureShaderBounds(targetBounds, localMatrix, out var minX, out var minY, out var maxX, out var maxY);
        GetTileRange(imageShader.TileModeX, minX, maxX, tileRect.Width, out var startX, out var endX);
        GetTileRange(imageShader.TileModeY, minY, maxY, tileRect.Height, out var startY, out var endY);
        LimitTileRange(ref startX, ref endX);
        LimitTileRange(ref startY, ref endY);

        var texture = RetainImageTexture(imageShader.Image);
        _context.PushGeometryClip(clipGeometry, _currentMatrix.ToMatrix4x4());
        var pushedOpacity = PushShaderColorFilterOpacity(colorFilter);
        try
        {
            for (var y = startY; y <= endY; y++)
            {
                for (var x = startX; x <= endX; x++)
                {
                    var placement = CreateTilePlacement(
                        tileRect,
                        x,
                        y,
                        imageShader.TileModeX,
                        imageShader.TileModeY);
                    _context.Commands.Add(new RenderCommand
                    {
                        Type = RenderCommandType.DrawTexture,
                        Texture = texture,
                        Rect = new Rect(0f, 0f, imageShader.Image.Width, imageShader.Image.Height),
                        SrcRect = new Rect(0f, 0f, imageShader.Image.Width, imageShader.Image.Height),
                        Transform = placement * localMatrix * _currentMatrix.ToMatrix4x4(),
                        TextureSamplingMode = TextureSamplingMode.Linear
                    });
                }
            }
        }
        finally
        {
            if (pushedOpacity)
            {
                _context.PopOpacity();
            }

            _context.PopGeometryClip();
        }
    }

    private bool PushShaderColorFilterOpacity(SKColorFilter? colorFilter)
    {
        if (colorFilter == null)
        {
            return false;
        }

        var opacity = colorFilter.Apply(SKColors.White).A / 255f;
        if (opacity >= 1f)
        {
            return false;
        }

        _context.PushOpacity(opacity);
        return true;
    }

    private static void GetPictureShaderBounds(
        SKRect targetBounds,
        Matrix4x4 localMatrix,
        out float minX,
        out float minY,
        out float maxX,
        out float maxY)
    {
        if (!Matrix4x4.Invert(localMatrix, out var inverse))
        {
            minX = targetBounds.Left;
            minY = targetBounds.Top;
            maxX = targetBounds.Right;
            maxY = targetBounds.Bottom;
            return;
        }

        var topLeft = Vector2.Transform(new Vector2(targetBounds.Left, targetBounds.Top), inverse);
        var topRight = Vector2.Transform(new Vector2(targetBounds.Right, targetBounds.Top), inverse);
        var bottomRight = Vector2.Transform(new Vector2(targetBounds.Right, targetBounds.Bottom), inverse);
        var bottomLeft = Vector2.Transform(new Vector2(targetBounds.Left, targetBounds.Bottom), inverse);
        minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));
    }

    private static void GetTileRange(
        SKShaderTileMode tileMode,
        float minimum,
        float maximum,
        float tileSize,
        out int start,
        out int end)
    {
        if (tileMode is SKShaderTileMode.Repeat or SKShaderTileMode.Mirror)
        {
            start = (int)MathF.Floor(minimum / tileSize) - 1;
            end = (int)MathF.Floor(maximum / tileSize) + 1;
            return;
        }

        start = 0;
        end = 0;
    }

    private static void LimitTileRange(ref int start, ref int end)
    {
        const int maxTileCountPerAxis = 128;
        var count = (long)end - start + 1;
        if (count <= maxTileCountPerAxis)
        {
            return;
        }

        var center = start + (int)(count / 2);
        start = center - maxTileCountPerAxis / 2;
        end = start + maxTileCountPerAxis - 1;
    }

    private static Matrix4x4 CreateTilePlacement(
        SKRect tileRect,
        int tileX,
        int tileY,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY)
    {
        var mirrorX = tileModeX == SKShaderTileMode.Mirror && (tileX & 1) != 0;
        var mirrorY = tileModeY == SKShaderTileMode.Mirror && (tileY & 1) != 0;
        var scaleX = mirrorX ? -1f : 1f;
        var scaleY = mirrorY ? -1f : 1f;
        var translateX = mirrorX
            ? tileRect.Left + (tileX + 1) * tileRect.Width
            : tileX * tileRect.Width - tileRect.Left;
        var translateY = mirrorY
            ? tileRect.Top + (tileY + 1) * tileRect.Height
            : tileY * tileRect.Height - tileRect.Top;

        return Matrix4x4.CreateScale(scaleX, scaleY, 1f)
            * Matrix4x4.CreateTranslation(translateX, translateY, 0f);
    }

    private static bool IsInverseFillType(SKPathFillType fillType)
    {
        return fillType is SKPathFillType.InverseWinding or SKPathFillType.InverseEvenOdd;
    }

    public void DrawImage(SKImage image, SKRect source, SKRect dest, SKPaint? paint)
    {
        DrawImageCore(image, source, dest, TextureSamplingMode.Linear, paint);
    }

    private void DrawImageCore(
        SKImage image,
        SKRect source,
        SKRect dest,
        TextureSamplingMode samplingMode,
        SKPaint? paint)
    {
        paint?.ThrowIfImageColorFilter();
        var opacity = paint != null ? paint.Color.A / 255f : 1f;
        var retainedTexture = RetainImageTexture(
            image,
            samplingMode == TextureSamplingMode.LinearMipmap);
        var pushedBlendMode = PushPaintBlendMode(paint);
        var pushedOpacity = false;
        var pushedEdgeClip = false;
        try
        {
            if (opacity < 1f)
            {
                _context.PushOpacity(opacity);
                pushedOpacity = true;
            }

            if (paint?.IsAntialias != false)
            {
                _context.PushGeometryClip(CreateRectGeometry(dest), _currentMatrix.ToMatrix4x4());
                pushedEdgeClip = true;
            }

            var rasterExtension = pushedEdgeClip ? 0.5f : 0f;
            var sourceExtensionX = dest.Width != 0f
                ? rasterExtension * source.Width / dest.Width
                : 0f;
            var sourceExtensionY = dest.Height != 0f
                ? rasterExtension * source.Height / dest.Height
                : 0f;

            _context.Commands.Add(new RenderCommand
            {
                Type = RenderCommandType.DrawTexture,
                Texture = retainedTexture,
                // Rasterize through the trailing pixel center; the exact clip supplies edge coverage.
                Rect = new Rect(dest.Left, dest.Top, dest.Width + rasterExtension, dest.Height + rasterExtension),
                SrcRect = new Rect(
                    source.Left,
                    source.Top,
                    source.Width + sourceExtensionX,
                    source.Height + sourceExtensionY),
                Transform = _currentMatrix.ToMatrix4x4(),
                TextureSamplingMode = samplingMode,
                IsEdgeAliased = paint is { IsAntialias: false }
            });

        }
        finally
        {
            if (pushedEdgeClip)
            {
                _context.PopGeometryClip();
            }

            if (pushedOpacity)
            {
                _context.PopOpacity();
            }

            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawImage(
        SKImage image,
        SKRect source,
        SKRect dest,
        SKSamplingOptions sampling,
        SKPaint paint)
    {
        var samplingMode = sampling.UseCubic
            ? TextureSamplingMode.Cubic
            : sampling.MipmapMode != SKMipmapMode.None
                ? TextureSamplingMode.LinearMipmap
            : sampling.FilterMode == SKFilterMode.Nearest
                ? TextureSamplingMode.Nearest
                : TextureSamplingMode.Linear;
        DrawImageCore(image, source, dest, samplingMode, paint);
    }

    public void DrawImage(SKImage image, float x, float y, SKPaint? paint)
    {
        DrawImage(image, new SKRect(0, 0, image.Width, image.Height), new SKRect(x, y, x + image.Width, y + image.Height), paint);
    }

    public void DrawImage(SKImage image, SKRect destination)
    {
        using var paint = new SKPaint();
        DrawImage(
            image,
            new SKRect(0f, 0f, image.Width, image.Height),
            destination,
            paint);
    }

    private GpuTexture RetainImageTexture(SKImage image, bool generateMipmaps = false)
    {
        var source = image.Texture;
        var currentContext = WgpuContext.Current;
        var targetContext = _gpuContext != null && !_gpuContext.IsDisposed
            ? _gpuContext
            : currentContext != null && !currentContext.IsDisposed
                ? currentContext
                : source.Context;
        if (!ReferenceEquals(source.Context, targetContext))
        {
            throw new InvalidOperationException(
                "SKCanvas.DrawImage cannot draw an SKImage from a different WebGPU context. " +
                "Create the image in the same GRContext/SKSurface context before recording the draw.");
        }

        var mipLevelCount = generateMipmaps
            ? CalculateMipLevelCount(source.Width, source.Height)
            : source.MipLevelCount;
        var usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc;
        if (generateMipmaps)
        {
            usage |= TextureUsage.RenderAttachment;
        }

        var retainedTexture = new GpuTexture(
            targetContext,
            source.Width,
            source.Height,
            source.Format,
            usage,
            "SKCanvas DrawImage Retained Source Texture",
            alphaMode: source.AlphaMode,
            mipLevelCount: mipLevelCount);
        if (retainedTexture.MipLevelCount == source.MipLevelCount)
        {
        retainedTexture.CopyFrom(source);
        }
        else
        {
            retainedTexture.CopyBaseLevelFrom(source);
            retainedTexture.GenerateMipmaps2DLinear();
        }
        _context.RetainResource(retainedTexture);
        return retainedTexture;
    }

    private static uint CalculateMipLevelCount(uint width, uint height)
    {
        var dimension = Math.Max(width, height);
        uint count = 1;
        while (dimension > 1)
        {
            dimension /= 2;
            count++;
        }

        return count;
    }

    public void DrawTextBlob(SKTextBlob textBlob, float x, float y, SKPaint paint)
    {
        var brush = paint.ToBrush();
        if (brush == null)
        {
            return;
        }

        var pushedBlendMode = PushPaintBlendMode(paint);
        try
        {
            foreach (var run in textBlob.Runs)
            {
                var positions = new Vector2[run.GlyphPositions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = new Vector2(run.GlyphPositions[i].X, run.GlyphPositions[i].Y);
                }

                _context.DrawGlyphRun(
                    run.GlyphIndices,
                    positions,
                    run.Font.Typeface.Font,
                    run.Font.Size,
                    brush,
                    new Vector2(x, y),
                    _currentMatrix.ToMatrix4x4()
                );
            }
        }
        finally
        {
            PopPaintBlendMode(pushedBlendMode);
        }
    }

    public void DrawText(SKTextBlob textBlob, float x, float y, SKPaint paint)
    {
        DrawTextBlob(textBlob, x, y, paint);
    }

    public void Flush()
    {
        _flush?.Invoke();
    }

    internal void ReleaseLayerTexturesAfterFlush()
    {
        foreach (var texture in _ownedLayerTextures)
        {
            texture.Dispose();
        }

        _ownedLayerTextures.Clear();
    }

    public void Dispose()
    {
        try
        {
            _flush?.Invoke();
        }
        finally
    {
        ReleaseLayerTexturesAfterFlush();
    }
}
}
