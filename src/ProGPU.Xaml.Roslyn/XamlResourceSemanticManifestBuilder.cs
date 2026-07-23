using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ProGPU.Xaml.Binding;
using ProGPU.Xaml.Resources;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Enriches the compilation-independent resource manifest with canonical Roslyn-bound key
/// identities. The raw manifest remains the recovery fallback; semantic evidence replaces it
/// only for stable nodes that bound successfully.
/// </summary>
public sealed class XamlResourceSemanticManifestBuilder
{
    public XamlResourceDocumentManifest Build(
        XamlResourceDocumentManifest manifest,
        XamlBoundDocument boundDocument)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (boundDocument == null) throw new ArgumentNullException(nameof(boundDocument));
        if (boundDocument.Root == null) return manifest;

        var graph = new XamlResourceGraphBuilder().Build(boundDocument);
        var objects = new Dictionary<ulong, XamlBoundObject>();
        IndexObjects(boundDocument.Root, objects);
        var scopes = graph.Scopes.ToDictionary(static scope => scope.Id);
        var rootScopeId = graph.Scopes.IsDefaultOrEmpty ? 0UL : graph.Scopes[0].Id;

        var definitions = ImmutableArray.CreateBuilder<XamlResourceManifestDefinition>();
        var semanticDefinitionIds = new HashSet<ulong>();
        foreach (var definition in graph.Definitions.OrderBy(static item => item.Ordinal))
        {
            if (!objects.TryGetValue(definition.ValueStableId, out var value)) continue;
            var scope = scopes[definition.ScopeId];
            var isProviderVisible = manifest.IsCompiledResourceProvider &&
                (scope.Id == rootScopeId ||
                 (scope.Kind == XamlResourceScopeKind.ThemePartition && scope.ParentId == rootScopeId));
            definitions.Add(new XamlResourceManifestDefinition(
                ToManifestKey(definition.ResourceKey),
                value.Type.RequestedName.NamespaceUri + "|" + value.Type.RequestedName.LocalName,
                definition.ValueStableId,
                isProviderVisible,
                ToManifestPartition(scope)));
            semanticDefinitionIds.Add(definition.ValueStableId);
        }
        foreach (var definition in manifest.Definitions)
            if (!semanticDefinitionIds.Contains(definition.StableId))
                definitions.Add(definition);

        var references = ImmutableArray.CreateBuilder<XamlResourceManifestReference>();
        var semanticReferenceIds = new HashSet<ulong>();
        foreach (var reference in graph.References)
        {
            references.Add(new XamlResourceManifestReference(
                ToManifestKey(reference.ResourceKey),
                reference.Kind,
                reference.StableId));
            semanticReferenceIds.Add(reference.StableId);
        }
        foreach (var reference in manifest.References)
            if (!semanticReferenceIds.Contains(reference.StableId))
                references.Add(reference);

        var importScopes = new Dictionary<ulong, ImportScope>();
        IndexImportScopes(
            boundDocument.Root,
            boundDocument.Root.StableId,
            manifest.IsCompiledResourceProvider,
            importScopes,
            boundDocument.DictionaryKeyDirectiveAliases);
        var imports = manifest.ImportEntries.Select(import =>
        {
            var hasSemanticScope = importScopes.TryGetValue(import.StableId, out var scope);
            var owner = hasSemanticScope ? scope.OwnerStableId : import.ScopeOwnerStableId;
            return new XamlResourceManifestImport(
                import.Source,
                import.SourceSpan,
                import.LineSpan,
                import.StableId,
                owner,
                hasSemanticScope ? scope.IsProviderVisible : import.IsProviderVisible,
                import.ScopeIdentity,
                hasSemanticScope ? scope.Partition : import.Partition);
        }).ToImmutableArray();

        return manifest.WithSemanticResources(
            definitions.ToImmutable(),
            references.ToImmutable(),
            imports);
    }

    private static XamlResourceManifestKey ToManifestKey(XamlResourceKeyInfo key) => new(
        key.Kind switch
        {
            XamlResourceKeyKind.Text => XamlResourceManifestKeyKind.Text,
            XamlResourceKeyKind.Type => XamlResourceManifestKeyKind.Type,
            XamlResourceKeyKind.StaticMember => XamlResourceManifestKeyKind.StaticMember,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        },
        key.Text,
        key.Identity,
        key.ExpressionIdentity);

    private static XamlResourceManifestPartition? ToManifestPartition(XamlResourceScopeInfo scope) =>
        scope.Kind == XamlResourceScopeKind.ThemePartition && scope.PartitionKey != null
            ? new XamlResourceManifestPartition(
                XamlResourcePartitionKind.Theme,
                ToManifestKey(scope.PartitionKey))
            : null;

    private static void IndexObjects(
        XamlBoundObject value,
        IDictionary<ulong, XamlBoundObject> result)
    {
        result[value.StableId] = value;
        foreach (var member in value.Members)
            foreach (var child in member.Values.OfType<XamlBoundObject>())
                IndexObjects(child, result);
    }

    private static void IndexImportScopes(
        XamlBoundObject value,
        ulong inheritedScopeOwner,
        bool providerVisibleContext,
        IDictionary<ulong, ImportScope> result,
        IReadOnlyList<string> dictionaryKeyDirectiveAliases,
        XamlResourceManifestPartition? inheritedPartition = null)
    {
        var scopeOwner = value.Members.Any(member =>
            GetResourceRole(member) == Schema.XamlResourceMemberRole.LexicalResources)
            ? value.StableId
            : inheritedScopeOwner;

        foreach (var member in value.Members)
        {
            if (GetResourceRole(member) == Schema.XamlResourceMemberRole.Source)
                foreach (var text in member.Values.OfType<XamlBoundText>())
                    result[text.StableId] = new ImportScope(scopeOwner, providerVisibleContext, inheritedPartition);

            foreach (var child in member.Values.OfType<XamlBoundObject>())
            {
                var role = GetResourceRole(member);
                var isMergedDictionary = value.Type.Symbol?.IsDictionary == true &&
                    role == Schema.XamlResourceMemberRole.MergedDictionaries;
                var isThemeDictionary = value.Type.Symbol?.IsDictionary == true &&
                    role == Schema.XamlResourceMemberRole.ThemeDictionaries;
                var childPartition = inheritedPartition;
                if (isThemeDictionary)
                {
                    var key = XamlResourceKeyFactory.GetDictionaryKey(
                        child,
                        member.Member.Symbol?.ValueType.CollectionShape?.KeyType,
                        dictionaryKeyDirectiveAliases);
                    childPartition = key == null
                        ? null
                        : new XamlResourceManifestPartition(XamlResourcePartitionKind.Theme, ToManifestKey(key));
                }
                var childScope = value.Type.Symbol?.IsDictionary == true &&
                                 member.Member.Kind == XamlBoundReferenceKind.DictionaryItems &&
                                 child.Type.Symbol?.IsDictionary == true &&
                                 !isMergedDictionary && !isThemeDictionary
                    ? child.StableId
                    : scopeOwner;
                IndexImportScopes(
                    child,
                    childScope,
                    providerVisibleContext && (isMergedDictionary || isThemeDictionary),
                    result,
                    dictionaryKeyDirectiveAliases,
                    childPartition);
            }
        }
    }

    private static Schema.XamlResourceMemberRole GetResourceRole(XamlBoundMember member)
    {
        var symbol = member.Member.Symbol;
        if (symbol == null) return Schema.XamlResourceMemberRole.None;
        if (symbol.ResourceRole != Schema.XamlResourceMemberRole.None) return symbol.ResourceRole;
        return symbol.IsAmbient && symbol.ValueType.IsDictionary
            ? Schema.XamlResourceMemberRole.LexicalResources
            : Schema.XamlResourceMemberRole.None;
    }

    private readonly struct ImportScope
    {
        public ImportScope(
            ulong ownerStableId,
            bool isProviderVisible,
            XamlResourceManifestPartition? partition)
        {
            OwnerStableId = ownerStableId;
            IsProviderVisible = isProviderVisible;
            Partition = partition;
        }

        public ulong OwnerStableId { get; }
        public bool IsProviderVisible { get; }
        public XamlResourceManifestPartition? Partition { get; }
    }
}
