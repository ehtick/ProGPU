using System;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml;

/// <summary>Reflection-free URI dispatcher for source-generated classless XAML dictionaries.</summary>
public static class XamlResourceProviderRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ProviderEntry> Providers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ProviderEntry?> Suffixes = new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic]
    private static HashSet<string>? _activeBuilds;

    public static void Register(string resourceUri, Func<ResourceDictionary> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceUri);
        ArgumentNullException.ThrowIfNull(factory);
        var identity = ResourceIdentity.Parse(resourceUri);
        var key = identity.Canonical;
        if (key.Length == 0) throw new ArgumentException("The resource URI has no path.", nameof(resourceUri));
        lock (Gate)
        {
            if (Providers.ContainsKey(key))
                throw new InvalidOperationException($"A compiled XAML resource provider is already registered for '{resourceUri}'.");
            var entry = new ProviderEntry(key, factory);
            Providers.Add(key, entry);
            if (!identity.IsQualified) IndexSuffixes(entry);
        }
    }

    public static bool TryCreate(Uri source, out ResourceDictionary? dictionary)
    {
        ArgumentNullException.ThrowIfNull(source);
        var identity = ResourceIdentity.Parse(source.OriginalString);
        var key = identity.Canonical;
        ProviderEntry? entry;
        lock (Gate)
        {
            if (!Providers.TryGetValue(key, out entry) && identity.AllowsCurrentPackageFallback)
            {
                if (!Providers.TryGetValue(identity.Path, out entry))
                    Suffixes.TryGetValue(identity.Path, out entry);
            }
            else if (entry == null && !identity.IsQualified)
            {
                Suffixes.TryGetValue(identity.Path, out entry);
            }
        }
        if (entry == null)
        {
            dictionary = null;
            return false;
        }

        var active = _activeBuilds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!active.Add(entry.Key))
            throw new InvalidOperationException($"A cycle was detected while loading compiled XAML resource '{source}'.");
        try
        {
            dictionary = entry.Factory() ??
                throw new InvalidOperationException($"The compiled XAML resource provider for '{source}' returned null.");
            return true;
        }
        finally
        {
            active.Remove(entry.Key);
            if (active.Count == 0) _activeBuilds = null;
        }
    }

    public static ResourceDictionary Create(Uri source) =>
        TryCreate(source, out var dictionary)
            ? dictionary!
            : throw new KeyNotFoundException($"No compiled XAML resource provider is registered for '{source}'.");

    private static void IndexSuffixes(ProviderEntry entry)
    {
        AddSuffix(entry.Key, entry);
        for (var index = 0; index < entry.Key.Length; index++)
        {
            if (entry.Key[index] == '/' && index + 1 < entry.Key.Length)
                AddSuffix(entry.Key.Substring(index + 1), entry);
        }
    }

    private static void AddSuffix(string suffix, ProviderEntry entry)
    {
        if (!Suffixes.TryGetValue(suffix, out var existing)) Suffixes.Add(suffix, entry);
        else if (existing != null && !string.Equals(existing.Key, entry.Key, StringComparison.OrdinalIgnoreCase))
            Suffixes[suffix] = null;
    }

    private readonly struct ResourceIdentity
    {
        private ResourceIdentity(string scheme, string authority, string path)
        {
            Scheme = scheme;
            Authority = authority;
            Path = path;
        }

        private string Scheme { get; }
        private string Authority { get; }
        public string Path { get; }
        public bool IsQualified => Scheme.Length != 0 || Authority.Length != 0;
        public bool AllowsCurrentPackageFallback =>
            string.Equals(Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase) && Authority.Length == 0;
        public string Canonical => Scheme.Length == 0
            ? Path
            : Scheme + "://" + Authority + "/" + Path;

        public static ResourceIdentity Parse(string value)
        {
            value = value.Trim().Replace('\\', '/');
            var query = value.IndexOfAny('?', '#');
            if (query >= 0) value = value.Substring(0, query);
            var scheme = string.Empty;
            var authority = string.Empty;
            var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                scheme = value.Substring(0, schemeIndex).ToLowerInvariant();
                var remainder = value.Substring(schemeIndex + 3);
                var slash = remainder.IndexOf('/');
                authority = slash < 0 ? remainder : remainder.Substring(0, slash);
                value = slash < 0 ? string.Empty : remainder.Substring(slash + 1);
            }
            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var normalized = new List<string>(segments.Length);
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index] == ".") continue;
                if (segments[index] == "..")
                {
                    if (normalized.Count != 0) normalized.RemoveAt(normalized.Count - 1);
                    continue;
                }
                normalized.Add(segments[index]);
            }
            return new ResourceIdentity(scheme, authority, string.Join('/', normalized));
        }
    }

    private sealed class ProviderEntry
    {
        public ProviderEntry(string key, Func<ResourceDictionary> factory)
        { Key = key; Factory = factory; }
        public string Key { get; }
        public Func<ResourceDictionary> Factory { get; }
    }
}
