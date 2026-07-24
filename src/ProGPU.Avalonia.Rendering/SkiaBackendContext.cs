using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform;
#if AVALONIA11
using Avalonia.Controls.Platform.Surfaces;
#else
using Avalonia.Platform.Surfaces;
#endif
using ProGPU.Backend;

namespace Avalonia.ProGpu
{
    internal class SkiaContext : IPlatformRenderInterfaceContext
    {
        public SkiaContext(object? gpu)
        {
            PublicFeatures = new Dictionary<Type, object>();
        }

        public void Dispose()
        {
        }

        public IRenderTarget CreateRenderTarget(
#if AVALONIA11
            IEnumerable<object>
#else
            IEnumerable<IPlatformRenderSurface>
#endif
            surfaces)
        {
            if (surfaces is not IList)
                surfaces = surfaces.ToList();

            foreach (var surface in surfaces)
            {
                if (surface is IFramebufferPlatformSurface framebufferSurface)
                    return new FramebufferRenderTarget(framebufferSurface);
            }

            throw new NotSupportedException(
                "Don't know how to create a ProGpu render target from any of the provided surfaces");
        }

#if !AVALONIA11
        public bool IsReadyToCreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
        {
            if (surfaces is not IList)
                surfaces = surfaces.ToList();

            foreach (var surface in surfaces)
            {
                if (surface is IFramebufferPlatformSurface)
                {
                    return surface.IsReady;
                }
            }

            return false;
        }

        public PixelSize? MaxOffscreenRenderTargetPixelSize => new PixelSize(8192, 8192);
#endif

        public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize pixelSize,
#if AVALONIA11
            double scaling)
#else
            Vector scaling,
            bool enableTextAntialiasing)
#endif
        {
            PixelFormat? preferredFormat = null;
            var currentContext = WgpuContext.Current;
            if (currentContext != null)
            {
                preferredFormat = currentContext.SwapChainFormat == Silk.NET.WebGPU.TextureFormat.Rgba8Unorm
                    ? PixelFormats.Rgba8888
                    : PixelFormats.Bgra8888;
            }

            var createInfo = new SurfaceRenderTarget.CreateInfo
            {
                Width = pixelSize.Width,
                Height = pixelSize.Height,
                Dpi =
#if AVALONIA11
                    new Vector(scaling * 96, scaling * 96),
#else
                    scaling * 96,
#endif
                Format = preferredFormat,
                DisableTextLcdRendering =
#if AVALONIA11
                    false
#else
                    !enableTextAntialiasing
#endif
            };

            return new SurfaceRenderTarget(createInfo);
        }

        public bool IsLost => false;
        public IReadOnlyDictionary<Type, object> PublicFeatures { get; }

        public object? TryGetFeature(Type featureType) => null;
    }
}
