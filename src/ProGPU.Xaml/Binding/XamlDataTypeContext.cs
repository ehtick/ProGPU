using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Binding;

public enum XamlDataTypeSourceOrigin
{
    AttributedMember,
    AncestorItems
}

/// <summary>
/// A bound value that can establish compiled-binding type information. The exact schema
/// descriptor and owning member are retained; no framework object is materialized here.
/// </summary>
public sealed class XamlDataTypeSourceValueInfo
{
    public XamlDataTypeSourceValueInfo(
        XamlDataTypeSourceOrigin origin,
        ulong ownerStableId,
        XamlBoundMember member,
        XamlBoundValue value,
        XamlDataTypeSourceInfo? dataTypeSource = null,
        XamlItemsDataTypeInheritanceInfo? itemsInheritance = null)
    {
        Origin = origin;
        OwnerStableId = ownerStableId;
        Member = member ?? throw new ArgumentNullException(nameof(member));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        DataTypeSource = dataTypeSource;
        ItemsInheritance = itemsInheritance;
    }

    public XamlDataTypeSourceOrigin Origin { get; }
    public ulong OwnerStableId { get; }
    public XamlBoundMember Member { get; }
    public XamlBoundValue Value { get; }
    public XamlDataTypeSourceInfo? DataTypeSource { get; }
    public XamlItemsDataTypeInheritanceInfo? ItemsInheritance { get; }
}

/// <summary>
/// Immutable data-type scope snapshot for one bound node. Sources are nearest-first.
/// Scope and items requests are attached only to the member that declared the annotation.
/// </summary>
public sealed class XamlDataTypeContextInfo
{
    public XamlDataTypeContextInfo(
        ulong nodeStableId,
        ImmutableArray<XamlDataTypeSourceValueInfo> sources,
        XamlDataTypeInheritanceInfo? scopeInheritance = null,
        XamlItemsDataTypeInheritanceInfo? itemsInheritance = null)
    {
        NodeStableId = nodeStableId;
        Sources = sources.IsDefault
            ? ImmutableArray<XamlDataTypeSourceValueInfo>.Empty
            : sources;
        ScopeInheritance = scopeInheritance;
        ItemsInheritance = itemsInheritance;
    }

    public ulong NodeStableId { get; }
    public ImmutableArray<XamlDataTypeSourceValueInfo> Sources { get; }
    public XamlDataTypeInheritanceInfo? ScopeInheritance { get; }
    public XamlItemsDataTypeInheritanceInfo? ItemsInheritance { get; }
}

public sealed class XamlDataTypeContextGraph
{
    public XamlDataTypeContextGraph(
        ImmutableDictionary<ulong, XamlDataTypeContextInfo> contexts)
    {
        Contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    }

    public ImmutableDictionary<ulong, XamlDataTypeContextInfo> Contexts { get; }

    public bool TryGetContext(
        ulong stableId,
        out XamlDataTypeContextInfo context) =>
        Contexts.TryGetValue(stableId, out context!);

    public XamlDataTypeContextInfo GetContext(ulong stableId) =>
        Contexts.TryGetValue(stableId, out var context)
            ? context
            : throw new KeyNotFoundException(
                "No compiled-binding data-type context is indexed for stable node '" +
                stableId + "'.");
}

/// <summary>
/// Builds data-type scope snapshots in O(N + S + A*D), where N is bound nodes, S is
/// attributed source values, A is item-inheritance requests, and D is ancestor depth.
/// </summary>
public sealed class XamlDataTypeContextGraphBuilder
{
    public XamlDataTypeContextGraph Build(XamlBoundDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        var contexts =
            ImmutableDictionary.CreateBuilder<ulong, XamlDataTypeContextInfo>();
        if (document.Root != null)
            VisitObject(
                document.Root,
                ImmutableArray<XamlDataTypeSourceValueInfo>.Empty,
                ImmutableArray<XamlBoundObject>.Empty,
                null,
                null,
                contexts);
        return new XamlDataTypeContextGraph(contexts.ToImmutable());
    }

    private static void VisitObject(
        XamlBoundObject value,
        ImmutableArray<XamlDataTypeSourceValueInfo> inherited,
        ImmutableArray<XamlBoundObject> ancestors,
        XamlDataTypeInheritanceInfo? incomingScopeInheritance,
        XamlItemsDataTypeInheritanceInfo? incomingItemsInheritance,
        ImmutableDictionary<ulong, XamlDataTypeContextInfo>.Builder contexts)
    {
        var active = inherited;
        foreach (var member in XamlBoundMemberOrdering.Order(value.Members))
        {
            var source = member.Member.Symbol?.DataTypeSource;
            if (source?.IsValid != true) continue;
            var additions =
                ImmutableArray.CreateBuilder<XamlDataTypeSourceValueInfo>();
            foreach (var item in member.Values)
                additions.Add(new XamlDataTypeSourceValueInfo(
                    XamlDataTypeSourceOrigin.AttributedMember,
                    value.StableId,
                    member,
                    item,
                    source));
            active = Prepend(additions.ToImmutable(), active);
        }

        SetContext(
            value.StableId,
            active,
            incomingScopeInheritance,
            incomingItemsInheritance,
            contexts);
        var lineage = ancestors.Add(value);
        foreach (var member in XamlBoundMemberOrdering.Order(value.Members))
        {
            var symbol = member.Member.Symbol;
            var memberSources = active;
            var items = symbol?.ItemsDataTypeInheritance;
            if (items?.IsValid == true)
                memberSources = Prepend(
                    FindAncestorItemSources(lineage, items),
                    memberSources);
            SetContext(
                member.StableId,
                memberSources,
                symbol?.DataTypeInheritance,
                items,
                contexts);
            foreach (var child in member.Values)
            {
                SetContext(
                    child.StableId,
                    memberSources,
                    symbol?.DataTypeInheritance,
                    items,
                    contexts);
                if (child is XamlBoundObject childObject)
                    VisitObject(
                        childObject,
                        memberSources,
                        lineage,
                        symbol?.DataTypeInheritance,
                        items,
                        contexts);
                else if (child is XamlBoundBinding binding)
                    VisitObject(
                        binding.Extension,
                        memberSources,
                        lineage,
                        symbol?.DataTypeInheritance,
                        items,
                        contexts);
                else if (child is XamlBoundCompiledBinding compiled)
                    VisitObject(
                        compiled.Extension,
                        memberSources,
                        lineage,
                        symbol?.DataTypeInheritance,
                        items,
                        contexts);
            }
        }
    }

    private static ImmutableArray<XamlDataTypeSourceValueInfo> FindAncestorItemSources(
        ImmutableArray<XamlBoundObject> lineage,
        XamlItemsDataTypeInheritanceInfo inheritance)
    {
        for (var index = lineage.Length - 1; index >= 0; index--)
        {
            var ancestor = lineage[index];
            var ancestorType = ancestor.Type.Symbol?.Symbol;
            if (ancestorType == null ||
                inheritance.LookupType == null ||
                !IsAssignableTo(ancestorType, inheritance.LookupType))
                continue;
            foreach (var member in ancestor.Members)
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        member.Member.Symbol?.Symbol,
                        inheritance.AncestorItemsProperty))
                    continue;
                var result =
                    ImmutableArray.CreateBuilder<XamlDataTypeSourceValueInfo>();
                foreach (var value in member.Values)
                    result.Add(new XamlDataTypeSourceValueInfo(
                        XamlDataTypeSourceOrigin.AncestorItems,
                        ancestor.StableId,
                        member,
                        value,
                        itemsInheritance: inheritance));
                return result.ToImmutable();
            }
        }
        return ImmutableArray<XamlDataTypeSourceValueInfo>.Empty;
    }

    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target)) return true;
        if (source is not INamedTypeSymbol named) return false;
        foreach (var contract in named.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(contract, target)) return true;
        for (var current = named.BaseType; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, target)) return true;
        return false;
    }

    private static ImmutableArray<XamlDataTypeSourceValueInfo> Prepend(
        ImmutableArray<XamlDataTypeSourceValueInfo> values,
        ImmutableArray<XamlDataTypeSourceValueInfo> existing)
    {
        if (values.IsEmpty) return existing;
        var result =
            ImmutableArray.CreateBuilder<XamlDataTypeSourceValueInfo>(
                values.Length + existing.Length);
        result.AddRange(values);
        result.AddRange(existing);
        return result.ToImmutable();
    }

    private static void SetContext(
        ulong stableId,
        ImmutableArray<XamlDataTypeSourceValueInfo> sources,
        XamlDataTypeInheritanceInfo? scopeInheritance,
        XamlItemsDataTypeInheritanceInfo? itemsInheritance,
        ImmutableDictionary<ulong, XamlDataTypeContextInfo>.Builder contexts)
    {
        contexts[stableId] = new XamlDataTypeContextInfo(
            stableId,
            sources,
            scopeInheritance,
            itemsInheritance);
    }
}
