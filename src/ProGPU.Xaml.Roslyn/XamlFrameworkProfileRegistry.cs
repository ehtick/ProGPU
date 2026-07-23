using System;
using System.Collections.Generic;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>Factory contract used by hosts to compose framework packages without core switches.</summary>
public interface IXamlFrameworkProfileFactory
{
    string Id { get; }
    int ContractVersion { get; }
    IRoslynXamlFrameworkProfile CreateProfile();
}

/// <summary>Immutable, deterministic registry of framework profile factories.</summary>
public sealed class XamlFrameworkProfileRegistry
{
    private readonly IReadOnlyDictionary<string, IXamlFrameworkProfileFactory> _factories;

    public XamlFrameworkProfileRegistry(IEnumerable<IXamlFrameworkProfileFactory> factories)
    {
        if (factories == null) throw new ArgumentNullException(nameof(factories));
        var map = new Dictionary<string, IXamlFrameworkProfileFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var factory in factories)
        {
            if (factory == null) throw new ArgumentException("A profile factory cannot be null.", nameof(factories));
            if (factory.ContractVersion != XamlFrameworkContract.CurrentVersion)
                throw new ArgumentException(
                    $"Profile '{factory.Id}' uses contract version {factory.ContractVersion}; version {XamlFrameworkContract.CurrentVersion} is required.",
                    nameof(factories));
            if (map.ContainsKey(factory.Id))
                throw new ArgumentException($"A framework profile named '{factory.Id}' is already registered.", nameof(factories));
            map.Add(factory.Id, factory);
        }
        _factories = map;
    }

    public static XamlFrameworkProfileRegistry BuiltIn { get; } =
        new XamlFrameworkProfileRegistry(new IXamlFrameworkProfileFactory[] { new WinUiXamlProfileFactory() });

    public IEnumerable<string> ProfileIds => _factories.Keys;

    public bool TryCreate(string id, out IRoslynXamlFrameworkProfile? profile)
    {
        if (id != null && _factories.TryGetValue(id, out var factory))
        {
            profile = factory.CreateProfile();
            if (profile.ContractVersion != factory.ContractVersion ||
                !string.Equals(profile.Id, factory.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Framework profile factory '{factory.Id}' returned an incompatible profile.");
            return true;
        }
        profile = null;
        return false;
    }
}

public sealed class WinUiXamlProfileFactory : IXamlFrameworkProfileFactory
{
    public string Id => "WinUI";
    public int ContractVersion => XamlFrameworkContract.CurrentVersion;
    public IRoslynXamlFrameworkProfile CreateProfile() => new WinUiXamlProfile();
}
