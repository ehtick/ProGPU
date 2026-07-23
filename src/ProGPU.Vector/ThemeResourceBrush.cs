using System.Numerics;

namespace ProGPU.Vector;

public class ThemeResourceBrush : Brush
{
    public object ResourceKey { get; }
    public object? LookupRoot { get; }

    public ThemeResourceBrush(string resourceKey)
        : this(null, (object)resourceKey)
    {
    }

    public ThemeResourceBrush(object resourceKey)
        : this(null, resourceKey)
    {
    }

    public ThemeResourceBrush(object? lookupRoot, string resourceKey)
        : this(lookupRoot, (object)resourceKey)
    {
    }

    public ThemeResourceBrush(object? lookupRoot, object resourceKey)
    {
        LookupRoot = lookupRoot;
        ResourceKey = resourceKey ?? throw new System.ArgumentNullException(nameof(resourceKey));
    }

    public override string ToString()
    {
        return $"ThemeResourceBrush: {ResourceKey}";
    }
}
