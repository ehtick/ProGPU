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
    private readonly IReadOnlyDictionary<string, RegisteredFactory> _factories;
    private readonly IReadOnlyList<string> _profileIds;

    public XamlFrameworkProfileRegistry(IEnumerable<IXamlFrameworkProfileFactory> factories)
    {
        if (factories == null) throw new ArgumentNullException(nameof(factories));
        var map = new Dictionary<string, RegisteredFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var factory in factories)
        {
            if (factory == null) throw new ArgumentException("A profile factory cannot be null.", nameof(factories));
            if (string.IsNullOrWhiteSpace(factory.Id))
                throw new ArgumentException("A framework profile factory requires a non-empty ID.", nameof(factories));
            if (factory.ContractVersion != XamlFrameworkContract.CurrentVersion)
                throw new ArgumentException(
                    $"Profile '{factory.Id}' uses contract version {factory.ContractVersion}; version {XamlFrameworkContract.CurrentVersion} is required.",
                    nameof(factories));
            if (map.ContainsKey(factory.Id))
                throw new ArgumentException($"A framework profile named '{factory.Id}' is already registered.", nameof(factories));
            map.Add(factory.Id, new RegisteredFactory(factory));
        }
        _factories = map;
        var profileIds = new List<string>(map.Keys);
        profileIds.Sort(StringComparer.OrdinalIgnoreCase);
        _profileIds = profileIds.AsReadOnly();
    }

    public static XamlFrameworkProfileRegistry BuiltIn { get; } =
        new XamlFrameworkProfileRegistry(new IXamlFrameworkProfileFactory[] { new WinUiXamlProfileFactory() });

    public IReadOnlyList<string> ProfileIds => _profileIds;

    public bool TryCreate(string id, out IRoslynXamlFrameworkProfile? profile)
    {
        if (id != null && _factories.TryGetValue(id, out var factory))
        {
            profile = factory.CreateProfile();
            return true;
        }
        profile = null;
        return false;
    }

    private sealed class RegisteredFactory
    {
        private readonly IXamlFrameworkProfileFactory _factory;

        public RegisteredFactory(IXamlFrameworkProfileFactory factory)
        {
            _factory = factory;
            Id = factory.Id;
            ContractVersion = factory.ContractVersion;
        }

        public string Id { get; }
        public int ContractVersion { get; }

        public IRoslynXamlFrameworkProfile CreateProfile()
        {
            var profile = _factory.CreateProfile();
            if (profile == null ||
                profile.ContractVersion != ContractVersion ||
                !string.Equals(profile.Id, Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Framework profile factory '{Id}' returned an incompatible profile.");
            return profile;
        }
    }
}

public sealed class WinUiXamlProfileFactory : IXamlFrameworkProfileFactory
{
    public string Id => "WinUI";
    public int ContractVersion => XamlFrameworkContract.CurrentVersion;
    public IRoslynXamlFrameworkProfile CreateProfile() => new WinUiXamlProfile();
}
