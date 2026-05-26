using System.Numerics;

namespace ProGPU.Vector;

public class ThemeResourceBrush : Brush
{
    public string ResourceKey { get; }

    public ThemeResourceBrush(string resourceKey)
    {
        ResourceKey = resourceKey ?? throw new System.ArgumentNullException(nameof(resourceKey));
    }

    public override string ToString()
    {
        return $"ThemeResourceBrush: {ResourceKey}";
    }
}
