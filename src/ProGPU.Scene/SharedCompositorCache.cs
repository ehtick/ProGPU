using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace ProGPU.Scene;

public static class SharedCompositorCache
{
    private static readonly object s_defaultScope = new();
    private static readonly Dictionary<WgpuContext, Dictionary<object, Dictionary<TextureFormat, Compositor>>> s_compositors = new();

    static SharedCompositorCache()
    {
        WgpuContext.Disposing += Remove;
    }

    public static Compositor GetOrCreate(WgpuContext context, TextureFormat renderFormat)
    {
        return GetOrCreate(context, renderFormat, s_defaultScope);
    }

    public static Compositor GetOrCreate(WgpuContext context, TextureFormat renderFormat, object scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scope);
        lock (context.RenderLock)
        {
            if (context.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(WgpuContext));
            }

            lock (s_compositors)
            {
                if (!s_compositors.TryGetValue(context, out var scopedCompositors))
                {
                    scopedCompositors = new Dictionary<object, Dictionary<TextureFormat, Compositor>>(
                        ReferenceEqualityComparer.Instance);
                    s_compositors.Add(context, scopedCompositors);
                }

                if (!scopedCompositors.TryGetValue(scope, out var formatCompositors))
                {
                    formatCompositors = new Dictionary<TextureFormat, Compositor>();
                    scopedCompositors.Add(scope, formatCompositors);
                }

                if (formatCompositors.TryGetValue(renderFormat, out var compositor))
                {
                    if (!compositor.IsDisposed)
                    {
                        return compositor;
                    }

                    formatCompositors.Remove(renderFormat);
                }

                compositor = new Compositor(context, renderFormat);
                formatCompositors.Add(renderFormat, compositor);
                return compositor;
            }
        }
    }

    public static void Remove(WgpuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (context.RenderLock)
        {
            Dictionary<object, Dictionary<TextureFormat, Compositor>>? scopedCompositors;
            lock (s_compositors)
            {
                if (!s_compositors.Remove(context, out scopedCompositors))
                {
                    return;
                }
            }

            foreach (var formatCompositors in scopedCompositors.Values)
            {
                DisposeCompositors(formatCompositors);
            }
        }
    }

    public static void Remove(WgpuContext context, object scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scope);
        lock (context.RenderLock)
        {
            Dictionary<TextureFormat, Compositor>? formatCompositors;
            lock (s_compositors)
            {
                if (!s_compositors.TryGetValue(context, out var scopedCompositors) ||
                    !scopedCompositors.Remove(scope, out formatCompositors))
                {
                    return;
                }

                if (scopedCompositors.Count == 0)
                {
                    s_compositors.Remove(context);
                }
            }

            DisposeCompositors(formatCompositors);
        }
    }

    private static void DisposeCompositors(Dictionary<TextureFormat, Compositor> formatCompositors)
    {
        foreach (var compositor in formatCompositors.Values)
        {
            try
            {
                compositor.Dispose();
            }
            catch
            {
                // Context disposal must continue even if an already-faulted compositor cannot clean up fully.
            }
        }
    }
}
