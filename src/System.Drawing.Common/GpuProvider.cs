using ProGPU.Backend;
using ProGPU.Scene;
using System.Collections.Generic;
using Silk.NET.WebGPU;

namespace System.Drawing;

internal static class GpuProvider
{
    private static WgpuContext? _context;
    private static readonly Dictionary<WgpuContext, Compositor> _compositors = new();

    static GpuProvider()
    {
        WgpuContext.Disposing += OnContextDisposing;
    }

    public static WgpuContext Context
    {
        get
        {
            var current = WgpuContext.Current;
            if (current != null && !current.IsDisposed)
            {
                return current;
            }

            if (WgpuContext.TryGetFirstActiveContext(out var active))
            {
                return active;
            }
            if (_context != null && !_context.IsDisposed)
            {
                return _context;
            }
            if (_context != null)
            {
                try { _context.Dispose(); } catch {}
            }
            _context = new WgpuContext();
            _context.Initialize(null);
            return _context;
        }
    }

    public static Compositor Compositor
    {
        get
        {
            return GetCompositor(Context);
        }
    }

    public static Compositor GetCompositor(WgpuContext context)
    {
        if (context == null || context.IsDisposed)
        {
            throw new InvalidOperationException("Cannot create a compositor for a disposed or missing WebGPU context.");
        }

        lock (_compositors)
        {
            if (_compositors.TryGetValue(context, out var compositor) && !compositor.IsDisposed)
            {
                return compositor;
            }

            if (compositor != null)
            {
                try { compositor.Dispose(); } catch { }
            }

            compositor = new Compositor(context, TextureFormat.Rgba8Unorm);
            _compositors[context] = compositor;
            return compositor;
        }
    }

    private static void OnContextDisposing(WgpuContext context)
    {
        lock (_compositors)
        {
            if (!_compositors.Remove(context, out var compositor))
            {
                return;
            }

            try { compositor.Dispose(); } catch { }
        }
    }
}
