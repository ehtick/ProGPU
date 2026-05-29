using System;

namespace Microsoft.UI.Xaml;

public class ThemeResource
{
    public string ResourceKey { get; }

    public ThemeResource(string resourceKey)
    {
        ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey));
    }

    public override string ToString()
    {
        return $"ThemeResource: {ResourceKey}";
    }
}
