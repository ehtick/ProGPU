using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Infoset;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Resources;

public enum XamlResourceManifestKeyKind
{
    Text,
    Type,
    StaticMember
}

public enum XamlResourcePartitionKind
{
    None,
    Theme
}

/// <summary>Compilation-independent conditional resource partition descriptor.</summary>
public sealed class XamlResourceManifestPartition : IEquatable<XamlResourceManifestPartition>
{
    public XamlResourceManifestPartition(XamlResourcePartitionKind kind, XamlResourceManifestKey key)
    {
        if (kind == XamlResourcePartitionKind.None) throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }
    public XamlResourcePartitionKind Kind { get; }
    public XamlResourceManifestKey Key { get; }
    public bool Equals(XamlResourceManifestPartition? other) => other != null &&
        Kind == other.Kind && Key.Equals(other.Key);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceManifestPartition);
    public override int GetHashCode() => unchecked((Kind.GetHashCode() * 397) ^ Key.GetHashCode());
}

/// <summary>
/// Compilation-independent resource-key signature used by incremental project indexing.
/// Its identity is deliberately compatible with the semantic resource key when a CLR
/// namespace can be derived without loading the framework compilation.
/// </summary>
public sealed class XamlResourceManifestKey : IEquatable<XamlResourceManifestKey>
{
    public XamlResourceManifestKey(
        XamlResourceManifestKeyKind kind,
        string text,
        string identity,
        string? expressionIdentity = null)
    {
        Kind = kind;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ExpressionIdentity = expressionIdentity ?? identity;
    }
    public XamlResourceManifestKeyKind Kind { get; }
    public string Text { get; }
    public string Identity { get; }
    public string ExpressionIdentity { get; }
    public static XamlResourceManifestKey FromText(string text) =>
        new XamlResourceManifestKey(
            XamlResourceManifestKeyKind.Text,
            text,
            XamlResourceConstantIdentity.Create(null, text, Microsoft.CodeAnalysis.SpecialType.System_String),
            "text:" + text);
    public bool Equals(XamlResourceManifestKey? other) => other != null &&
        string.Equals(Identity, other.Identity, StringComparison.Ordinal);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceManifestKey);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
}

public sealed class XamlResourceManifestDefinition
{
    public XamlResourceManifestDefinition(string key, string typeName, ulong stableId, bool isProviderVisible)
        : this(XamlResourceManifestKey.FromText(key), typeName, stableId, isProviderVisible) { }
    public XamlResourceManifestDefinition(
        XamlResourceManifestKey resourceKey,
        string typeName,
        ulong stableId,
        bool isProviderVisible,
        XamlResourceManifestPartition? partition = null)
    { ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey)); TypeName = typeName; StableId = stableId; IsProviderVisible = isProviderVisible; Partition = partition; }
    public XamlResourceManifestKey ResourceKey { get; }
    public string Key => ResourceKey.Text;
    public string TypeName { get; }
    public ulong StableId { get; }
    public bool IsProviderVisible { get; }
    public XamlResourceManifestPartition? Partition { get; }
}

public sealed class XamlResourceManifestReference
{
    public XamlResourceManifestReference(string key, XamlResourceReferenceKind kind, ulong stableId)
        : this(XamlResourceManifestKey.FromText(key), kind, stableId) { }
    public XamlResourceManifestReference(
        XamlResourceManifestKey resourceKey,
        XamlResourceReferenceKind kind,
        ulong stableId)
    { ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey)); Kind = kind; StableId = stableId; }
    public XamlResourceManifestKey ResourceKey { get; }
    public string Key => ResourceKey.Text;
    public XamlResourceReferenceKind Kind { get; }
    public ulong StableId { get; }
}

public sealed class XamlResourceManifestImport : IEquatable<XamlResourceManifestImport>
{
    public XamlResourceManifestImport(
        string source,
        TextSpan sourceSpan,
        LinePositionSpan lineSpan,
        ulong stableId,
        ulong scopeOwnerStableId = 0,
        bool isProviderVisible = true,
        ulong scopeIdentity = 0,
        XamlResourceManifestPartition? partition = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SourceSpan = sourceSpan;
        LineSpan = lineSpan;
        StableId = stableId;
        ScopeOwnerStableId = scopeOwnerStableId;
        IsProviderVisible = isProviderVisible;
        ScopeIdentity = scopeIdentity;
        Partition = partition;
    }
    public string Source { get; }
    public TextSpan SourceSpan { get; }
    public LinePositionSpan LineSpan { get; }
    public ulong StableId { get; }
    public ulong ScopeOwnerStableId { get; }
    public bool IsProviderVisible { get; }
    /// <summary>
    /// Whitespace-insensitive preorder identity of the lexical resource scope. Runtime graph
    /// matching uses <see cref="ScopeOwnerStableId"/>; incremental fingerprints use this value.
    /// </summary>
    public ulong ScopeIdentity { get; }
    public XamlResourceManifestPartition? Partition { get; }
    public bool Equals(XamlResourceManifestImport? other) => other != null &&
        string.Equals(Source, other.Source, StringComparison.Ordinal) &&
        SourceSpan.Equals(other.SourceSpan) && LineSpan.Equals(other.LineSpan) && StableId == other.StableId &&
        ScopeOwnerStableId == other.ScopeOwnerStableId && IsProviderVisible == other.IsProviderVisible &&
        ScopeIdentity == other.ScopeIdentity && Equals(Partition, other.Partition);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceManifestImport);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (StringComparer.Ordinal.GetHashCode(Source) * 397) ^ SourceSpan.GetHashCode();
            hash = (hash * 397) ^ StableId.GetHashCode();
            hash = (hash * 397) ^ ScopeOwnerStableId.GetHashCode();
            hash = (hash * 397) ^ IsProviderVisible.GetHashCode();
            hash = (hash * 397) ^ ScopeIdentity.GetHashCode();
            return (hash * 397) ^ (Partition?.GetHashCode() ?? 0);
        }
    }
}

/// <summary>A compilation-independent resource signature produced from the XAML infoset.</summary>
public sealed class XamlResourceDocumentManifest : IEquatable<XamlResourceDocumentManifest>
{
    public XamlResourceDocumentManifest(
        string documentPath,
        ImmutableArray<XamlResourceManifestDefinition> definitions,
        ImmutableArray<string> imports,
        ImmutableArray<XamlResourceManifestReference> references,
        string providerFingerprint)
        : this(
            documentPath,
            definitions,
            imports.Select(static source => new XamlResourceManifestImport(
                source, default, default, stableId: 0)).ToImmutableArray(),
            references,
            providerFingerprint,
            documentPath,
            default,
            default,
            isCompiledResourceProvider: true)
    {
    }

    public XamlResourceDocumentManifest(
        string documentPath,
        ImmutableArray<XamlResourceManifestDefinition> definitions,
        ImmutableArray<XamlResourceManifestImport> importEntries,
        ImmutableArray<XamlResourceManifestReference> references,
        string providerFingerprint)
        : this(documentPath, definitions, importEntries, references, providerFingerprint,
            documentPath, default, default, isCompiledResourceProvider: true)
    {
    }

    public XamlResourceDocumentManifest(
        string documentPath,
        ImmutableArray<XamlResourceManifestDefinition> definitions,
        ImmutableArray<XamlResourceManifestImport> importEntries,
        ImmutableArray<XamlResourceManifestReference> references,
        string providerFingerprint,
        string resourceUri,
        TextSpan documentSpan,
        LinePositionSpan documentLineSpan,
        bool isCompiledResourceProvider)
    {
        DocumentPath = CanonicalPath(documentPath);
        ResourceUri = CanonicalResourceUri(resourceUri);
        DocumentSpan = documentSpan;
        DocumentLineSpan = documentLineSpan;
        IsCompiledResourceProvider = isCompiledResourceProvider;
        Definitions = definitions;
        ImportEntries = importEntries;
        Imports = importEntries.Select(static import => import.Source).ToImmutableArray();
        References = references;
        ProviderFingerprint = providerFingerprint;
    }
    public string DocumentPath { get; }
    public string ResourceUri { get; }
    public TextSpan DocumentSpan { get; }
    public LinePositionSpan DocumentLineSpan { get; }
    public bool IsCompiledResourceProvider { get; }
    public ImmutableArray<XamlResourceManifestDefinition> Definitions { get; }
    public ImmutableArray<string> Imports { get; }
    public ImmutableArray<XamlResourceManifestImport> ImportEntries { get; }
    public ImmutableArray<XamlResourceManifestReference> References { get; }
    public string ProviderFingerprint { get; }
    public XamlResourceDocumentManifest WithSemanticResources(
        ImmutableArray<XamlResourceManifestDefinition> definitions,
        ImmutableArray<XamlResourceManifestReference> references,
        ImmutableArray<XamlResourceManifestImport>? importEntries = null)
    {
        var imports = importEntries ?? ImportEntries;
        return new XamlResourceDocumentManifest(
            DocumentPath,
            definitions,
            imports,
            references,
            XamlResourceDocumentManifestBuilder.ComputeProviderFingerprint(definitions, imports),
            ResourceUri,
            DocumentSpan,
            DocumentLineSpan,
            IsCompiledResourceProvider);
    }
    public bool Equals(XamlResourceDocumentManifest? other) => other != null &&
        string.Equals(DocumentPath, other.DocumentPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ResourceUri, other.ResourceUri, StringComparison.OrdinalIgnoreCase) &&
        IsCompiledResourceProvider == other.IsCompiledResourceProvider &&
        string.Equals(ProviderFingerprint, other.ProviderFingerprint, StringComparison.Ordinal);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceDocumentManifest);
    public override int GetHashCode() => unchecked(
        (((StringComparer.OrdinalIgnoreCase.GetHashCode(DocumentPath) * 397) ^
        StringComparer.OrdinalIgnoreCase.GetHashCode(ResourceUri)) * 397) ^
        StringComparer.Ordinal.GetHashCode(ProviderFingerprint));

    internal static string CanonicalPath(string path)
    {
        try { path = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException ||
                                           exception is PathTooLongException || exception is System.Security.SecurityException)
        { }
        return path.Replace('\\', '/');
    }

    internal static string CanonicalResourceUri(string path)
        => XamlResourceUriIdentity.Parse(path).Canonical;
}

/// <summary>
/// Framework-neutral logical resource identity. It recognizes public URI forms without
/// assigning framework behavior beyond scheme/authority/path isolation.
/// </summary>
public readonly struct XamlResourceUriIdentity : IEquatable<XamlResourceUriIdentity>
{
    private XamlResourceUriIdentity(string scheme, string authority, string path, bool isExplicit)
    {
        Scheme = scheme;
        Authority = authority;
        Path = path;
        IsExplicit = isExplicit;
    }

    public string Scheme { get; }
    public string Authority { get; }
    public string Path { get; }
    public bool IsExplicit { get; }
    public bool HasAuthority => Authority.Length != 0;
    public bool IsQualified => Scheme.Length != 0 || HasAuthority;
    public string Canonical => Scheme.Length == 0
        ? Path
        : Scheme + "://" + Authority + (Path.StartsWith("/", StringComparison.Ordinal) ? Path : "/" + Path);

    public static XamlResourceUriIdentity Parse(string? value)
    {
        value = (value ?? string.Empty).Trim().Replace('\\', '/');
        var suffix = value.IndexOfAny(new[] { '?', '#' });
        if (suffix >= 0) value = value.Substring(0, suffix);

        if (value.StartsWith("pack://application:,,,", StringComparison.OrdinalIgnoreCase))
        {
            var packPath = value.Substring("pack://application:,,,".Length);
            var parsed = ParseComponentOrPath(packPath, isExplicit: true);
            return string.Equals(parsed.Scheme, "component", StringComparison.Ordinal)
                ? parsed
                : new XamlResourceUriIdentity("pack-application", string.Empty, parsed.Path, isExplicit: true);
        }

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            var scheme = value.Substring(0, schemeSeparator).ToLowerInvariant();
            var remainder = value.Substring(schemeSeparator + 3);
            var slash = remainder.IndexOf('/');
            var authority = slash < 0 ? remainder : remainder.Substring(0, slash);
            var path = slash < 0 ? "/" : remainder.Substring(slash);
            if (string.Equals(scheme, "pack", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(authority, "application:,,,", StringComparison.OrdinalIgnoreCase))
                return ParseComponentOrPath(path, isExplicit: true);
            return new XamlResourceUriIdentity(scheme, authority, NormalizePath(path), isExplicit: true);
        }

        return ParseComponentOrPath(value, isExplicit: false);
    }

    public XamlResourceUriIdentity Resolve(string source)
    {
        var parsed = Parse(source);
        if (parsed.IsExplicit) return parsed;
        if (source.TrimStart().StartsWith("/", StringComparison.Ordinal))
            return new XamlResourceUriIdentity(Scheme, Authority, parsed.Path, IsQualified);
        var slash = Path.LastIndexOf('/');
        var directory = slash < 0 ? "/" : Path.Substring(0, slash + 1);
        return new XamlResourceUriIdentity(Scheme, Authority, NormalizePath(directory + source), IsQualified);
    }

    public XamlResourceUriIdentity WithoutScheme() =>
        new XamlResourceUriIdentity(string.Empty, string.Empty, Path, isExplicit: false);

    public bool Equals(XamlResourceUriIdentity other) =>
        string.Equals(Canonical, other.Canonical, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is XamlResourceUriIdentity other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Canonical);
    public override string ToString() => Canonical;
    public static bool operator ==(XamlResourceUriIdentity left, XamlResourceUriIdentity right) => left.Equals(right);
    public static bool operator !=(XamlResourceUriIdentity left, XamlResourceUriIdentity right) => !left.Equals(right);

    private static XamlResourceUriIdentity ParseComponentOrPath(string value, bool isExplicit)
    {
        var normalized = NormalizePath(value);
        var firstSlash = normalized.IndexOf('/', 1);
        var firstSegment = firstSlash < 0 ? normalized.Substring(1) : normalized.Substring(1, firstSlash - 1);
        const string componentSuffix = ";component";
        if (firstSegment.EndsWith(componentSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var authority = firstSegment.Substring(0, firstSegment.Length - componentSuffix.Length);
            var path = firstSlash < 0 ? "/" : normalized.Substring(firstSlash);
            return new XamlResourceUriIdentity("component", authority, path, isExplicit: true);
        }
        return new XamlResourceUriIdentity(string.Empty, string.Empty, normalized, isExplicit);
    }

    private static string NormalizePath(string value)
    {
        var segments = new List<string>();
        foreach (var segment in value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count != 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return "/" + string.Join("/", segments);
    }
}

public sealed class XamlResourceDocumentManifestBuilder
{
    public XamlResourceDocumentManifest Build(XamlInfosetDocument document)
        => Build(document, document?.Path ?? throw new ArgumentNullException(nameof(document)));

    public XamlResourceDocumentManifest Build(XamlInfosetDocument document, string resourceUri)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (resourceUri == null) throw new ArgumentNullException(nameof(resourceUri));
        var definitions = ImmutableArray.CreateBuilder<XamlResourceManifestDefinition>();
        var imports = ImmutableArray.CreateBuilder<XamlResourceManifestImport>();
        var references = ImmutableArray.CreateBuilder<XamlResourceManifestReference>();
        var isCompiledResourceProvider = document.Root != null && IsResourceDictionary(document.Root) &&
            string.IsNullOrWhiteSpace(GetDirectiveText(document.Root, "Class"));
        if (document.Root != null)
        {
            var rootIdentity = XamlStableId.Combine(
                0,
                XamlSyntaxKind.Element,
                document.Root.TypeName.NamespaceUri,
                document.Root.TypeName.LocalName,
                0);
            Visit(
                document.Root,
                definitions,
                imports,
                references,
                isDocumentRoot: true,
                scopeOwnerStableId: document.Root.StableId,
                scopeIdentity: rootIdentity,
                objectIdentity: rootIdentity,
                providerVisibleContext: isCompiledResourceProvider,
                partition: null);
        }

        return new XamlResourceDocumentManifest(
            document.Path,
            definitions.ToImmutable(),
            imports.ToImmutable(),
            references.ToImmutable(),
            ComputeProviderFingerprint(definitions, imports),
            resourceUri,
            document.Root == null ? default : new TextSpan(document.Root.SourceSpan.Start, 1),
            document.Root == null
                ? default
                : document.SourceText.Lines.GetLinePositionSpan(new TextSpan(document.Root.SourceSpan.Start, 1)),
            isCompiledResourceProvider);
    }

    internal static string ComputeProviderFingerprint(
        IEnumerable<XamlResourceManifestDefinition> definitions,
        IEnumerable<XamlResourceManifestImport> imports)
    {
        var hash = new StableHash64();
        foreach (var definition in definitions)
        {
            hash.Add(definition.IsProviderVisible ? "D+" : "D-");
            hash.Add(definition.ResourceKey.Identity);
            hash.Add(definition.TypeName);
            hash.Add(definition.Partition?.Kind.ToString() ?? "None");
            hash.Add(definition.Partition?.Key.Identity ?? string.Empty);
        }
        foreach (var import in imports)
        {
            hash.Add("I");
            hash.Add(import.Source);
            hash.Add(import.ScopeIdentity.ToString(CultureInfo.InvariantCulture));
            hash.Add(import.IsProviderVisible ? "+" : "-");
            hash.Add(import.Partition?.Kind.ToString() ?? "None");
            hash.Add(import.Partition?.Key.Identity ?? string.Empty);
        }
        return hash.Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static void Visit(
        XamlInfosetObject value,
        ImmutableArray<XamlResourceManifestDefinition>.Builder definitions,
        ImmutableArray<XamlResourceManifestImport>.Builder imports,
        ImmutableArray<XamlResourceManifestReference>.Builder references,
        bool isDocumentRoot,
        ulong scopeOwnerStableId,
        ulong scopeIdentity,
        ulong objectIdentity,
        bool providerVisibleContext,
        XamlResourceManifestPartition? partition)
    {
        var createsOwnedScope = HasResourcesMember(value) && value.StableId != scopeOwnerStableId;
        var currentScopeOwner = createsOwnedScope ? value.StableId : scopeOwnerStableId;
        var currentScopeIdentity = createsOwnedScope
            ? XamlStableId.Combine(
                objectIdentity,
                XamlSyntaxKind.Attribute,
                string.Empty,
                "ResourcesScope",
                0)
            : scopeIdentity;
        if (IsResourceDictionary(value))
        {
            var source = GetTextMember(value, "Source");
            if (source != null && !string.IsNullOrWhiteSpace(source.Text))
            {
                imports.Add(new XamlResourceManifestImport(
                    source.Text.Trim(),
                    source.SourceSpan,
                    source.Document.SourceText.Lines.GetLinePositionSpan(source.SourceSpan),
                    source.StableId,
                    currentScopeOwner,
                    providerVisibleContext,
                    currentScopeIdentity,
                    partition));
            }
            foreach (var member in value.Members.Where(static member => member.Name.IsImplicit))
            foreach (var item in member.Values.OfType<XamlInfosetObject>())
            {
                var key = GetDirectiveKey(item);
                if (key != null)
                    definitions.Add(new XamlResourceManifestDefinition(
                        key, item.TypeName.NamespaceUri + "|" + item.TypeName.LocalName, item.StableId,
                        isProviderVisible: isDocumentRoot || providerVisibleContext,
                        partition));
            }
        }

        if (value.IsMarkupExtension && TryGetReferenceKind(value.TypeName.LocalName, out var kind))
        {
            var key = CreateKey(value.Members.SelectMany(static member => member.Values).FirstOrDefault());
            if (key != null)
                references.Add(new XamlResourceManifestReference(key, kind, value.StableId));
        }

        foreach (var member in value.Members)
        {
            var childIndex = 0;
            foreach (var child in member.Values.OfType<XamlInfosetObject>())
            {
                var childObjectIdentity = XamlStableId.Combine(
                    objectIdentity,
                    XamlSyntaxKind.Element,
                    member.Name.NamespaceUri,
                    member.Name.LocalName,
                    childIndex++);
                var isMergedDictionary = IsResourceDictionary(value) &&
                    string.Equals(member.Name.LocalName, "MergedDictionaries", StringComparison.Ordinal);
                var isThemeDictionaryMember = IsResourceDictionary(value) &&
                    string.Equals(member.Name.LocalName, "ThemeDictionaries", StringComparison.Ordinal);
                var childPartition = partition;
                if (isThemeDictionaryMember && IsResourceDictionary(child))
                {
                    var partitionKey = GetDirectiveKey(child);
                    childPartition = partitionKey == null
                        ? null
                        : new XamlResourceManifestPartition(XamlResourcePartitionKind.Theme, partitionKey);
                }
                var childScopeOwner = IsResourceDictionary(value) && IsResourceDictionary(child) &&
                                      !isMergedDictionary && !isThemeDictionaryMember
                    ? child.StableId
                    : currentScopeOwner;
                var childScopeIdentity = childScopeOwner == currentScopeOwner
                    ? currentScopeIdentity
                    : XamlStableId.Combine(
                        childObjectIdentity,
                        XamlSyntaxKind.Attribute,
                        string.Empty,
                        "DictionaryScope",
                        0);
                Visit(
                    child,
                    definitions,
                    imports,
                    references,
                    isDocumentRoot: false,
                    childScopeOwner,
                    childScopeIdentity,
                    childObjectIdentity,
                    providerVisibleContext && (isMergedDictionary || isThemeDictionaryMember),
                    childPartition);
            }
        }
    }

    private static bool HasResourcesMember(XamlInfosetObject value) =>
        value.Members.Any(static member =>
            string.Equals(member.Name.LocalName, "Resources", StringComparison.Ordinal));

    private static bool IsResourceDictionary(XamlInfosetObject value) =>
        string.Equals(value.TypeName.LocalName, "ResourceDictionary", StringComparison.Ordinal);

    private static XamlInfosetText? GetTextMember(XamlInfosetObject value, string name) =>
        value.Members.FirstOrDefault(member => string.Equals(member.Name.LocalName, name, StringComparison.Ordinal))?
            .Values.OfType<XamlInfosetText>().FirstOrDefault();

    private static string? GetDirectiveText(XamlInfosetObject value, string name) =>
        value.Members.FirstOrDefault(member => member.Name.IsDirective &&
            string.Equals(member.Name.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
            string.Equals(member.Name.LocalName, name, StringComparison.Ordinal))?
            .Values.OfType<XamlInfosetText>().FirstOrDefault()?.Text;

    private static XamlResourceManifestKey? GetDirectiveKey(XamlInfosetObject value, string name = "Key")
    {
        var member = value.Members.FirstOrDefault(candidate => candidate.Name.IsDirective &&
            string.Equals(candidate.Name.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
            string.Equals(candidate.Name.LocalName, name, StringComparison.Ordinal));
        return member == null || member.Values.Length != 1 ? null : CreateKey(member.Values[0]);
    }

    private static XamlResourceManifestKey? CreateKey(XamlInfosetValue? value)
    {
        if (value is XamlInfosetText text)
        {
            var key = text.Text.Trim();
            return key.Length == 0 ? null : XamlResourceManifestKey.FromText(key);
        }
        if (value is not XamlInfosetObject extension || !extension.IsMarkupExtension ||
            !string.Equals(extension.TypeName.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal))
            return null;
        var argument = extension.Members.SelectMany(static member => member.Values)
            .OfType<XamlInfosetText>().FirstOrDefault()?.Text.Trim();
        if (string.IsNullOrEmpty(argument)) return null;
        if (string.Equals(extension.TypeName.LocalName, "Type", StringComparison.Ordinal) ||
            string.Equals(extension.TypeName.LocalName, "TypeExtension", StringComparison.Ordinal))
        {
            var typeIdentity = CreateClrTypeIdentity(argument!, extension.NamespaceMappings);
            return new XamlResourceManifestKey(
                XamlResourceManifestKeyKind.Type,
                argument!,
                typeIdentity ?? "type-xaml:" + ResolveQualifiedName(argument!, extension.NamespaceMappings));
        }
        if (string.Equals(extension.TypeName.LocalName, "Static", StringComparison.Ordinal) ||
            string.Equals(extension.TypeName.LocalName, "StaticExtension", StringComparison.Ordinal))
        {
            var separator = argument!.LastIndexOf('.');
            if (separator <= 0 || separator == argument.Length - 1) return null;
            var typeIdentity = CreateClrTypeIdentity(argument.Substring(0, separator), extension.NamespaceMappings);
            return new XamlResourceManifestKey(
                XamlResourceManifestKeyKind.StaticMember,
                argument,
                typeIdentity == null
                    ? "static-xaml:" + ResolveQualifiedName(argument, extension.NamespaceMappings)
                    : "static:" + typeIdentity.Substring("type:".Length) + argument.Substring(separator));
        }
        return null;
    }

    private static string? CreateClrTypeIdentity(
        string qualifiedName,
        ImmutableArray<XamlNamespaceMapping> mappings)
    {
        var separator = qualifiedName.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : qualifiedName.Substring(0, separator);
        var localName = separator < 0 ? qualifiedName : qualifiedName.Substring(separator + 1);
        var namespaceUri = GetNamespaceUri(prefix, mappings);
        if (namespaceUri == null) return null;
        string? clrNamespace = null;
        if (namespaceUri.StartsWith("using:", StringComparison.Ordinal))
            clrNamespace = namespaceUri.Substring("using:".Length);
        else if (namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            clrNamespace = namespaceUri.Substring("clr-namespace:".Length);
            var assembly = clrNamespace.IndexOf(';');
            if (assembly >= 0) clrNamespace = clrNamespace.Substring(0, assembly);
        }
        else if (string.Equals(namespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal))
        {
            clrNamespace = "System";
            localName = localName switch
            {
                "String" => "String",
                "Boolean" => "Boolean",
                "Byte" => "Byte",
                "Char" => "Char",
                "Decimal" => "Decimal",
                "Double" => "Double",
                "Int16" => "Int16",
                "Int32" => "Int32",
                "Int64" => "Int64",
                "Object" => "Object",
                "Single" => "Single",
                "TimeSpan" => "TimeSpan",
                "Uri" => "Uri",
                _ => localName
            };
        }
        if (clrNamespace == null) return null;
        return "type:global::" + (clrNamespace.Length == 0 ? string.Empty : clrNamespace + ".") + localName;
    }

    private static string ResolveQualifiedName(
        string qualifiedName,
        ImmutableArray<XamlNamespaceMapping> mappings)
    {
        var separator = qualifiedName.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : qualifiedName.Substring(0, separator);
        var localName = separator < 0 ? qualifiedName : qualifiedName.Substring(separator + 1);
        var namespaceUri = GetNamespaceUri(prefix, mappings) ?? string.Empty;
        return namespaceUri + "|" + localName;
    }

    private static string? GetNamespaceUri(
        string prefix,
        ImmutableArray<XamlNamespaceMapping> mappings)
    {
        foreach (var mapping in mappings)
            if (string.Equals(mapping.Prefix, prefix, StringComparison.Ordinal))
                return mapping.NamespaceUri;
        return null;
    }

    private static bool TryGetReferenceKind(string name, out XamlResourceReferenceKind kind)
    {
        if (string.Equals(name, "StaticResource", StringComparison.Ordinal))
        { kind = XamlResourceReferenceKind.Static; return true; }
        if (string.Equals(name, "ThemeResource", StringComparison.Ordinal))
        { kind = XamlResourceReferenceKind.Theme; return true; }
        kind = default;
        return false;
    }

    private struct StableHash64
    {
        private ulong _value;
        public ulong Value => _value == 0 ? 14695981039346656037UL : _value;
        public void Add(string value)
        {
            if (_value == 0) _value = 14695981039346656037UL;
            for (var index = 0; index < value.Length; index++)
            { _value ^= value[index]; _value *= 1099511628211UL; }
            _value ^= 0xFF; _value *= 1099511628211UL;
        }
    }
}

public sealed class XamlResourceDependencySlice : IEquatable<XamlResourceDependencySlice>
{
    public XamlResourceDependencySlice(string documentPath, ImmutableArray<string> providerPaths, string fingerprint)
        : this(
            documentPath,
            providerPaths,
            ImmutableArray<XamlExternalResourceDefinition>.Empty,
            ImmutableArray<XamlResourceImportIssue>.Empty,
            ImmutableArray<XamlResourceProviderIssue>.Empty,
            fingerprint)
    {
    }

    public XamlResourceDependencySlice(
        string documentPath,
        ImmutableArray<string> providerPaths,
        ImmutableArray<XamlExternalResourceDefinition> externalDefinitions,
        string fingerprint)
        : this(
            documentPath,
            providerPaths,
            externalDefinitions,
            ImmutableArray<XamlResourceImportIssue>.Empty,
            ImmutableArray<XamlResourceProviderIssue>.Empty,
            fingerprint)
    {
    }

    public XamlResourceDependencySlice(
        string documentPath,
        ImmutableArray<string> providerPaths,
        ImmutableArray<XamlExternalResourceDefinition> externalDefinitions,
        ImmutableArray<XamlResourceImportIssue> importIssues,
        string fingerprint)
        : this(documentPath, providerPaths, externalDefinitions, importIssues,
            ImmutableArray<XamlResourceProviderIssue>.Empty, fingerprint)
    {
    }

    public XamlResourceDependencySlice(
        string documentPath,
        ImmutableArray<string> providerPaths,
        ImmutableArray<XamlExternalResourceDefinition> externalDefinitions,
        ImmutableArray<XamlResourceImportIssue> importIssues,
        ImmutableArray<XamlResourceProviderIssue> providerIssues,
        string fingerprint)
    {
        DocumentPath = documentPath;
        ProviderPaths = providerPaths;
        ExternalDefinitions = externalDefinitions;
        ImportIssues = importIssues;
        ProviderIssues = providerIssues;
        Fingerprint = fingerprint;
    }
    public string DocumentPath { get; }
    public ImmutableArray<string> ProviderPaths { get; }
    /// <summary>Definitions in framework-neutral merged-dictionary lookup precedence.</summary>
    public ImmutableArray<XamlExternalResourceDefinition> ExternalDefinitions { get; }
    public ImmutableArray<XamlResourceImportIssue> ImportIssues { get; }
    public ImmutableArray<XamlResourceProviderIssue> ProviderIssues { get; }
    public string Fingerprint { get; }
    public bool Equals(XamlResourceDependencySlice? other) => other != null &&
        string.Equals(DocumentPath, other.DocumentPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal) &&
        ProviderPaths.SequenceEqual(other.ProviderPaths, StringComparer.OrdinalIgnoreCase) &&
        ExternalDefinitions.SequenceEqual(other.ExternalDefinitions) &&
        ImportIssues.SequenceEqual(other.ImportIssues) &&
        ProviderIssues.SequenceEqual(other.ProviderIssues);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceDependencySlice);
    public override int GetHashCode() => unchecked(
        (StringComparer.OrdinalIgnoreCase.GetHashCode(DocumentPath) * 397) ^
        StringComparer.Ordinal.GetHashCode(Fingerprint));
}

public enum XamlResourceImportIssueKind
{
    Missing,
    Ambiguous,
    Cycle
}

public sealed class XamlResourceImportIssue : IEquatable<XamlResourceImportIssue>
{
    public XamlResourceImportIssue(
        XamlResourceImportIssueKind kind,
        string importingDocumentPath,
        XamlResourceManifestImport import,
        ImmutableArray<string> candidatePaths)
    {
        Kind = kind;
        ImportingDocumentPath = importingDocumentPath ?? throw new ArgumentNullException(nameof(importingDocumentPath));
        Import = import ?? throw new ArgumentNullException(nameof(import));
        CandidatePaths = candidatePaths;
    }
    public XamlResourceImportIssueKind Kind { get; }
    public string ImportingDocumentPath { get; }
    public XamlResourceManifestImport Import { get; }
    public ImmutableArray<string> CandidatePaths { get; }
    public bool Equals(XamlResourceImportIssue? other) => other != null && Kind == other.Kind &&
        string.Equals(ImportingDocumentPath, other.ImportingDocumentPath, StringComparison.OrdinalIgnoreCase) &&
        Import.Equals(other.Import) &&
        CandidatePaths.SequenceEqual(other.CandidatePaths, StringComparer.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceImportIssue);
    public override int GetHashCode() => unchecked(
        (((Kind.GetHashCode() * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ImportingDocumentPath)) * 397) ^
        Import.GetHashCode());
}

public sealed class XamlResourceProviderIssue : IEquatable<XamlResourceProviderIssue>
{
    public XamlResourceProviderIssue(
        string documentPath,
        string resourceUri,
        TextSpan sourceSpan,
        LinePositionSpan lineSpan,
        ImmutableArray<string> candidateDocumentPaths)
    {
        DocumentPath = documentPath ?? throw new ArgumentNullException(nameof(documentPath));
        ResourceUri = resourceUri ?? throw new ArgumentNullException(nameof(resourceUri));
        SourceSpan = sourceSpan;
        LineSpan = lineSpan;
        CandidateDocumentPaths = candidateDocumentPaths;
    }
    public string DocumentPath { get; }
    public string ResourceUri { get; }
    public TextSpan SourceSpan { get; }
    public LinePositionSpan LineSpan { get; }
    public ImmutableArray<string> CandidateDocumentPaths { get; }
    public bool Equals(XamlResourceProviderIssue? other) => other != null &&
        string.Equals(DocumentPath, other.DocumentPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ResourceUri, other.ResourceUri, StringComparison.OrdinalIgnoreCase) &&
        SourceSpan.Equals(other.SourceSpan) && LineSpan.Equals(other.LineSpan) &&
        CandidateDocumentPaths.SequenceEqual(other.CandidateDocumentPaths, StringComparer.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceProviderIssue);
    public override int GetHashCode() => unchecked(
        ((StringComparer.OrdinalIgnoreCase.GetHashCode(DocumentPath) * 397) ^
         StringComparer.OrdinalIgnoreCase.GetHashCode(ResourceUri)) * 397 ^ SourceSpan.GetHashCode());
}

public sealed class XamlExternalResourceDefinition : IEquatable<XamlExternalResourceDefinition>
{
    public XamlExternalResourceDefinition(string key, string typeName, string providerPath)
        : this(XamlResourceManifestKey.FromText(key), typeName, providerPath, consumerScopeOwnerStableId: 0) { }
    public XamlExternalResourceDefinition(
        XamlResourceManifestKey resourceKey,
        string typeName,
        string providerPath,
        ulong consumerScopeOwnerStableId = 0,
        XamlResourceManifestPartition? partition = null,
        ulong stableId = 0)
    {
        ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        ProviderPath = providerPath ?? throw new ArgumentNullException(nameof(providerPath));
        ConsumerScopeOwnerStableId = consumerScopeOwnerStableId;
        Partition = partition;
        StableId = stableId;
    }
    public XamlResourceManifestKey ResourceKey { get; }
    public string Key => ResourceKey.Text;
    public string TypeName { get; }
    public string ProviderPath { get; }
    public ulong ConsumerScopeOwnerStableId { get; }
    public XamlResourceManifestPartition? Partition { get; }
    public ulong StableId { get; }
    public bool Equals(XamlExternalResourceDefinition? other) => other != null &&
        ResourceKey.Equals(other.ResourceKey) &&
        string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
        string.Equals(ProviderPath, other.ProviderPath, StringComparison.OrdinalIgnoreCase) &&
        ConsumerScopeOwnerStableId == other.ConsumerScopeOwnerStableId &&
        Equals(Partition, other.Partition) && StableId == other.StableId;
    public override bool Equals(object? obj) => Equals(obj as XamlExternalResourceDefinition);
    public override int GetHashCode() => unchecked(
        (((ResourceKey.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(TypeName)) * 397) ^
        (((StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderPath) * 397) ^ ConsumerScopeOwnerStableId.GetHashCode()) * 397) ^
        (((Partition?.GetHashCode() ?? 0) * 397) ^ StableId.GetHashCode()));
}

/// <summary>Indexes direct/transitive dictionary providers without imposing global key visibility.</summary>
public sealed class XamlResourceProjectIndex
{
    private readonly Dictionary<string, XamlResourceDocumentManifest> _byPath;
    private readonly Dictionary<string, List<string>> _bySuffix;
    private readonly Dictionary<string, List<string>> _byResourceUri;

    public XamlResourceProjectIndex(IEnumerable<XamlResourceDocumentManifest> manifests)
    {
        if (manifests == null) throw new ArgumentNullException(nameof(manifests));
        _byPath = new Dictionary<string, XamlResourceDocumentManifest>(StringComparer.OrdinalIgnoreCase);
        _bySuffix = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _byResourceUri = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests.OrderBy(static value => value.DocumentPath, StringComparer.OrdinalIgnoreCase))
        {
            _byPath[manifest.DocumentPath] = manifest;
            if (manifest.IsCompiledResourceProvider)
            {
                IndexSuffixes(manifest.DocumentPath);
                AddIndexValue(_byResourceUri, manifest.ResourceUri, manifest.DocumentPath);
            }
        }
    }

    public XamlResourceDependencySlice GetDependencySlice(string documentPath)
    {
        var canonical = XamlResourceDocumentManifest.CanonicalPath(documentPath);
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<TraversalFrame>();
        var issues = ImmutableArray.CreateBuilder<XamlResourceImportIssue>();
        var providerIssues = GetProviderIssues(canonical);
        VisitProviders(canonical, providers, active, stack, issues, null, null);
        var paths = providers.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        var hash = new SliceHash64();
        for (var index = 0; index < paths.Length; index++)
        { hash.Add(paths[index]); hash.Add(_byPath[paths[index]].ProviderFingerprint); }
        for (var index = 0; index < issues.Count; index++)
        {
            hash.Add("?");
            hash.Add(issues[index].Kind.ToString());
            hash.Add(issues[index].ImportingDocumentPath);
            hash.Add(issues[index].Import.Source);
            for (var candidate = 0; candidate < issues[index].CandidatePaths.Length; candidate++)
                hash.Add(issues[index].CandidatePaths[candidate]);
        }
        for (var index = 0; index < providerIssues.Length; index++)
        {
            hash.Add("D");
            hash.Add(providerIssues[index].ResourceUri);
            for (var candidate = 0; candidate < providerIssues[index].CandidateDocumentPaths.Length; candidate++)
                hash.Add(providerIssues[index].CandidateDocumentPaths[candidate]);
        }
        var externalDefinitions = BuildExternalDefinitions(canonical);
        return new XamlResourceDependencySlice(canonical, paths, externalDefinitions, issues.ToImmutable(), providerIssues,
            hash.Value.ToString("X16", CultureInfo.InvariantCulture));
    }

    private ImmutableArray<XamlResourceProviderIssue> GetProviderIssues(string documentPath)
    {
        if (!_byPath.TryGetValue(documentPath, out var manifest) ||
            !_byResourceUri.TryGetValue(manifest.ResourceUri, out var matches) || matches.Count < 2)
            return ImmutableArray<XamlResourceProviderIssue>.Empty;
        return ImmutableArray.Create(new XamlResourceProviderIssue(
            documentPath,
            manifest.ResourceUri,
            manifest.DocumentSpan,
            manifest.DocumentLineSpan,
            matches.ToImmutableArray()));
    }

    private ImmutableArray<XamlExternalResourceDefinition> BuildExternalDefinitions(string documentPath)
    {
        var result = ImmutableArray.CreateBuilder<XamlExternalResourceDefinition>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { documentPath };
        if (!_byPath.TryGetValue(documentPath, out var document)) return result.ToImmutable();
        for (var index = document.ImportEntries.Length - 1; index >= 0; index--)
        {
            var import = document.ImportEntries[index];
            var resolution = ResolveImport(documentPath, import);
            if (resolution.Kind == ImportResolutionKind.Resolved)
                AppendProviderDefinitions(
                    resolution.ProviderPath!,
                    import.ScopeOwnerStableId,
                    import.Partition,
                    visited,
                    result);
        }
        return result.ToImmutable();
    }

    private void AppendProviderDefinitions(
        string providerPath,
        ulong consumerScopeOwnerStableId,
        XamlResourceManifestPartition? consumerPartition,
        HashSet<string> visited,
        ImmutableArray<XamlExternalResourceDefinition>.Builder result)
    {
        var visitIdentity = providerPath + "\0" + consumerScopeOwnerStableId.ToString(CultureInfo.InvariantCulture) +
                            "\0" + (consumerPartition?.Kind.ToString() ?? "None") +
                            "\0" + (consumerPartition?.Key.Identity ?? string.Empty);
        if (!visited.Add(visitIdentity) || !_byPath.TryGetValue(providerPath, out var provider)) return;
        for (var index = 0; index < provider.Definitions.Length; index++)
        {
            var definition = provider.Definitions[index];
            if (!definition.IsProviderVisible) continue;
            result.Add(new XamlExternalResourceDefinition(
                definition.ResourceKey,
                definition.TypeName,
                providerPath,
                consumerScopeOwnerStableId,
                definition.Partition ?? consumerPartition,
                definition.StableId));
        }
        for (var index = provider.ImportEntries.Length - 1; index >= 0; index--)
        {
            var import = provider.ImportEntries[index];
            if (!import.IsProviderVisible) continue;
            var resolution = ResolveImport(providerPath, import);
            if (resolution.Kind == ImportResolutionKind.Resolved)
                AppendProviderDefinitions(
                    resolution.ProviderPath!,
                    consumerScopeOwnerStableId,
                    import.Partition ?? consumerPartition,
                    visited,
                    result);
        }
    }

    private void VisitProviders(
        string path,
        HashSet<string> providers,
        HashSet<string> active,
        List<TraversalFrame> stack,
        ImmutableArray<XamlResourceImportIssue>.Builder issues,
        string? incomingDocumentPath,
        XamlResourceManifestImport? incomingImport)
    {
        if (!_byPath.TryGetValue(path, out var manifest)) return;
        providers.Add(path);
        if (!active.Add(path)) return;
        stack.Add(new TraversalFrame(path, incomingDocumentPath, incomingImport));
        for (var index = 0; index < manifest.ImportEntries.Length; index++)
        {
            var import = manifest.ImportEntries[index];
            var resolution = ResolveImport(path, import);
            if (resolution.Kind == ImportResolutionKind.Missing)
            {
                issues.Add(new XamlResourceImportIssue(
                    XamlResourceImportIssueKind.Missing, path, import, ImmutableArray<string>.Empty));
            }
            else if (resolution.Kind == ImportResolutionKind.Ambiguous)
            {
                issues.Add(new XamlResourceImportIssue(
                    XamlResourceImportIssueKind.Ambiguous, path, import, resolution.CandidatePaths));
            }
            else if (active.Contains(resolution.ProviderPath!))
            {
                AddCycleIssues(path, import, resolution.ProviderPath!, stack, issues);
            }
            else if (!providers.Contains(resolution.ProviderPath!))
            {
                VisitProviders(
                    resolution.ProviderPath!, providers, active, stack, issues, path, import);
            }
        }
        stack.RemoveAt(stack.Count - 1);
        active.Remove(path);
    }

    private static void AddCycleIssues(
        string importingPath,
        XamlResourceManifestImport import,
        string cycleTarget,
        IReadOnlyList<TraversalFrame> stack,
        ImmutableArray<XamlResourceImportIssue>.Builder issues)
    {
        AddIssueIfMissing(issues, new XamlResourceImportIssue(
            XamlResourceImportIssueKind.Cycle,
            importingPath,
            import,
            ImmutableArray.Create(cycleTarget)));
        var targetIndex = -1;
        for (var index = 0; index < stack.Count; index++)
        {
            if (string.Equals(stack[index].Path, cycleTarget, StringComparison.OrdinalIgnoreCase))
            { targetIndex = index; break; }
        }
        for (var index = targetIndex + 1; index < stack.Count; index++)
        {
            var frame = stack[index];
            if (frame.IncomingDocumentPath == null || frame.IncomingImport == null) continue;
            AddIssueIfMissing(issues, new XamlResourceImportIssue(
                XamlResourceImportIssueKind.Cycle,
                frame.IncomingDocumentPath,
                frame.IncomingImport,
                ImmutableArray.Create(frame.Path)));
        }
    }

    private static void AddIssueIfMissing(
        ImmutableArray<XamlResourceImportIssue>.Builder issues,
        XamlResourceImportIssue issue)
    {
        for (var index = 0; index < issues.Count; index++)
            if (issues[index].Equals(issue)) return;
        issues.Add(issue);
    }

    private ImportResolution ResolveImport(string importingPath, XamlResourceManifestImport import)
    {
        var source = import.Source;
        var sourceIdentity = XamlResourceUriIdentity.Parse(source);
        var allowsCurrentPackageFallback =
            (string.Equals(
                sourceIdentity.Scheme,
                "ms-appx",
                StringComparison.OrdinalIgnoreCase) && !sourceIdentity.HasAuthority) ||
            string.Equals(sourceIdentity.Scheme, "pack-application", StringComparison.OrdinalIgnoreCase);
        var normalized = NormalizeSource(source);
        if (normalized.Length == 0) return ImportResolution.Missing;
        if (_byPath.TryGetValue(importingPath, out var importingManifest))
        {
            var importingIdentity = XamlResourceUriIdentity.Parse(importingManifest.ResourceUri);
            var logical = importingIdentity.Resolve(source);
            if (TryResolveLogical(logical.Canonical, out var logicalResolution))
                return logicalResolution;

            // An authority-free ms-appx URI denotes the current package. Project-logical
            // providers intentionally remain scheme-free unless MSBuild supplies an authority.
            if (string.Equals(logical.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase) &&
                !logical.HasAuthority && TryResolveLogical(logical.WithoutScheme().Canonical, out logicalResolution))
                return logicalResolution;
            if (string.Equals(logical.Scheme, "pack-application", StringComparison.OrdinalIgnoreCase) &&
                TryResolveLogical(logical.WithoutScheme().Canonical, out logicalResolution))
                return logicalResolution;

            // A qualified base is a closed lookup domain. Relative and root-relative imports
            // must not escape it through physical paths or global suffix matching.
            if ((logical.IsQualified || sourceIdentity.IsExplicit) && !allowsCurrentPackageFallback)
                return ImportResolution.Missing;
        }
        if (!sourceIdentity.IsExplicit)
        {
            var directory = Path.GetDirectoryName(importingPath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(directory))
            {
                var relative = XamlResourceDocumentManifest.CanonicalPath(Path.Combine(
                    directory!, normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (_byPath.ContainsKey(relative)) return ImportResolution.Resolved(relative);
            }
        }
        if (!_bySuffix.TryGetValue(normalized, out var matches) || matches.Count == 0)
            return ImportResolution.Missing;
        if (matches.Count == 1) return ImportResolution.Resolved(matches[0]);
        return ImportResolution.Ambiguous(matches.ToImmutableArray());
    }

    private bool TryResolveLogical(string logicalUri, out ImportResolution resolution)
    {
        if (_byResourceUri.TryGetValue(logicalUri, out var matches))
        {
            if (matches.Count == 1)
            {
                resolution = ImportResolution.Resolved(matches[0]);
                return true;
            }
            if (matches.Count > 1)
            {
                resolution = ImportResolution.Ambiguous(matches.ToImmutableArray());
                return true;
            }
        }
        resolution = default;
        return false;
    }

    private void IndexSuffixes(string path)
    {
        var normalized = path.TrimStart('/');
        AddSuffix(normalized, path);
        for (var index = 0; index < normalized.Length; index++)
        {
            if (normalized[index] == '/' && index + 1 < normalized.Length)
                AddSuffix(normalized.Substring(index + 1), path);
        }
    }

    private void AddSuffix(string suffix, string path)
    {
        AddIndexValue(_bySuffix, suffix, path);
    }

    private static void AddIndexValue(Dictionary<string, List<string>> index, string key, string path)
    {
        if (!index.TryGetValue(key, out var matches))
        {
            matches = new List<string>();
            index.Add(key, matches);
        }
        if (!matches.Contains(path, StringComparer.OrdinalIgnoreCase)) matches.Add(path);
    }

    private enum ImportResolutionKind { Resolved, Missing, Ambiguous }

    private readonly struct TraversalFrame
    {
        public TraversalFrame(
            string path,
            string? incomingDocumentPath,
            XamlResourceManifestImport? incomingImport)
        { Path = path; IncomingDocumentPath = incomingDocumentPath; IncomingImport = incomingImport; }
        public string Path { get; }
        public string? IncomingDocumentPath { get; }
        public XamlResourceManifestImport? IncomingImport { get; }
    }

    private readonly struct ImportResolution
    {
        private ImportResolution(
            ImportResolutionKind kind,
            string? providerPath,
            ImmutableArray<string> candidatePaths)
        { Kind = kind; ProviderPath = providerPath; CandidatePaths = candidatePaths; }
        public ImportResolutionKind Kind { get; }
        public string? ProviderPath { get; }
        public ImmutableArray<string> CandidatePaths { get; }
        public static ImportResolution Missing => new(
            ImportResolutionKind.Missing, null, ImmutableArray<string>.Empty);
        public static ImportResolution Resolved(string path) => new(
            ImportResolutionKind.Resolved, path, ImmutableArray.Create(path));
        public static ImportResolution Ambiguous(ImmutableArray<string> paths) => new(
            ImportResolutionKind.Ambiguous, null, paths);
    }

    private static string NormalizeSource(string source)
    {
        return XamlResourceUriIdentity.Parse(source).Path.TrimStart('/');
    }

    private struct SliceHash64
    {
        private ulong _value;
        public ulong Value => _value == 0 ? 14695981039346656037UL : _value;
        public void Add(string value)
        {
            if (_value == 0) _value = 14695981039346656037UL;
            for (var index = 0; index < value.Length; index++)
            { _value ^= value[index]; _value *= 1099511628211UL; }
            _value ^= 0xFE; _value *= 1099511628211UL;
        }
    }
}
