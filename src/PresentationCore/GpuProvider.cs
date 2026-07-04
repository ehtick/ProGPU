using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.WebGPU;

namespace System.Windows.Media;

internal static class GpuProvider
{
    private static WgpuContext? _context;
    private static Compositor? _compositor;

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
            var ctx = Context;
            if (_compositor != null && !_compositor.IsDisposed && _compositor.Context == ctx)
            {
                return _compositor;
            }
            if (_compositor != null)
            {
                try { _compositor.Dispose(); } catch {}
            }
            _compositor = new Compositor(ctx, TextureFormat.Rgba8Unorm);
            return _compositor;
        }
    }
}
