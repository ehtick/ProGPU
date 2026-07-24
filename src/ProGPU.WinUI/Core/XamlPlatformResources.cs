using System;
using System.Threading;

namespace Microsoft.UI.Xaml;

/// <summary>
/// Immutable theme state supplied to a platform-resource provider.
/// </summary>
public readonly record struct XamlPlatformResourceContext(
    ElementTheme Theme,
    VisualThemeFamily ThemeFamily,
    bool IsHighContrast);

/// <summary>
/// Typed, reflection-free seam through which a platform host supplies system XAML resources.
/// </summary>
public interface IXamlPlatformResourceProvider
{
    /// <summary>Gets whether the operating system is currently using a contrast theme.</summary>
    bool IsHighContrast { get; }

    /// <summary>Raised when system colors or contrast-theme state change.</summary>
    event EventHandler? ResourcesChanged;

    /// <summary>Attempts to resolve a platform-owned resource for the supplied theme state.</summary>
    bool TryGetResource(
        object key,
        in XamlPlatformResourceContext context,
        out object? value);
}

/// <summary>
/// Process-wide platform-resource provider selected by the active application host.
/// </summary>
public static class XamlPlatformResources
{
    private static readonly object Sync = new();
    private static IXamlPlatformResourceProvider? _provider;

    public static IXamlPlatformResourceProvider? Provider
    {
        get => Volatile.Read(ref _provider);
        set
        {
            lock (Sync)
            {
                var current = _provider;
                if (ReferenceEquals(current, value))
                {
                    return;
                }

                if (current != null)
                {
                    current.ResourcesChanged -= OnProviderResourcesChanged;
                }

                Volatile.Write(ref _provider, value);
                if (value != null)
                {
                    value.ResourcesChanged += OnProviderResourcesChanged;
                }
            }

            ThemeManager.NotifyPlatformResourcesChanged(value?.IsHighContrast ?? false);
        }
    }

    internal static bool TryGetResource(
        object key,
        in XamlPlatformResourceContext context,
        out object? value)
    {
        var provider = Volatile.Read(ref _provider);

        if (provider != null && provider.TryGetResource(key, context, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static void OnProviderResourcesChanged(object? sender, EventArgs args)
    {
        var provider = Volatile.Read(ref _provider);
        if (provider != null)
        {
            ThemeManager.NotifyPlatformResourcesChanged(provider.IsHighContrast);
        }
    }
}
