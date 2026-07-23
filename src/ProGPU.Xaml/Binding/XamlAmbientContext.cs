using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Binding;

public enum XamlAmbientValueOrigin
{
    Type,
    Member
}

/// <summary>
/// One compile-time ambient value together with the exact schema descriptor that made it
/// ambient. The value remains a bound node; runtime object materialization is owned by the
/// framework object writer.
/// </summary>
public sealed class XamlAmbientValueInfo
{
    public XamlAmbientValueInfo(
        XamlAmbientValueOrigin origin,
        ulong ownerStableId,
        XamlBoundValue value,
        XamlTypeInfo ambientType,
        XamlMemberInfo? ambientMember = null,
        ulong? memberStableId = null)
    {
        Origin = origin;
        OwnerStableId = ownerStableId;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        AmbientType = ambientType ?? throw new ArgumentNullException(nameof(ambientType));
        AmbientMember = ambientMember;
        MemberStableId = memberStableId;
    }

    public XamlAmbientValueOrigin Origin { get; }
    public ulong OwnerStableId { get; }
    public XamlBoundValue Value { get; }
    public XamlTypeInfo AmbientType { get; }
    public XamlMemberInfo? AmbientMember { get; }
    public ulong? MemberStableId { get; }
    public bool IsTypeAmbient => Origin == XamlAmbientValueOrigin.Type;
    public bool IsMemberAmbient => Origin == XamlAmbientValueOrigin.Member;
}

/// <summary>
/// Immutable nearest-first ambient snapshot for one bound node. Deferred boundary identities
/// let a factory capture exactly the context that was active when its content was compiled.
/// </summary>
public sealed class XamlAmbientContextInfo
{
    public XamlAmbientContextInfo(
        ulong nodeStableId,
        ImmutableArray<XamlAmbientValueInfo> values,
        ImmutableArray<ulong> deferredBoundaryStableIds)
    {
        NodeStableId = nodeStableId;
        Values = values.IsDefault ? ImmutableArray<XamlAmbientValueInfo>.Empty : values;
        DeferredBoundaryStableIds = deferredBoundaryStableIds.IsDefault
            ? ImmutableArray<ulong>.Empty
            : deferredBoundaryStableIds;
    }

    public ulong NodeStableId { get; }
    public ImmutableArray<XamlAmbientValueInfo> Values { get; }
    public ImmutableArray<ulong> DeferredBoundaryStableIds { get; }

    public XamlAmbientValueInfo? FindFirst(
        Func<XamlAmbientValueInfo, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        foreach (var value in Values)
            if (predicate(value)) return value;
        return null;
    }
}

public sealed class XamlAmbientContextGraph
{
    public XamlAmbientContextGraph(
        ImmutableDictionary<ulong, XamlAmbientContextInfo> contexts)
    {
        Contexts = contexts ??
            throw new ArgumentNullException(nameof(contexts));
    }

    public ImmutableDictionary<ulong, XamlAmbientContextInfo> Contexts { get; }

    public bool TryGetContext(
        ulong stableId,
        out XamlAmbientContextInfo context) =>
        Contexts.TryGetValue(stableId, out context!);

    public XamlAmbientContextInfo GetContext(ulong stableId) =>
        Contexts.TryGetValue(stableId, out var context)
            ? context
            : throw new KeyNotFoundException(
                "No ambient context is indexed for stable node '" + stableId + "'.");
}

/// <summary>
/// Builds a framework-neutral, schema-scope-aware ambient stack over the bound tree.
/// Building is O(N + A), where N is the number of bound nodes and A is the total number of
/// ambient entries copied into immutable snapshots.
/// </summary>
public sealed class XamlAmbientContextGraphBuilder
{
    public XamlAmbientContextGraph Build(XamlBoundDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        var contexts = ImmutableDictionary.CreateBuilder<ulong, XamlAmbientContextInfo>();
        if (document.Root != null)
            VisitObject(
                document.Root,
                ImmutableArray<XamlAmbientValueInfo>.Empty,
                ImmutableArray<ulong>.Empty,
                contexts);
        return new XamlAmbientContextGraph(contexts.ToImmutable());
    }

    private static void VisitObject(
        XamlBoundObject value,
        ImmutableArray<XamlAmbientValueInfo> inherited,
        ImmutableArray<ulong> deferredBoundaries,
        ImmutableDictionary<ulong, XamlAmbientContextInfo>.Builder contexts)
    {
        var active = inherited;
        if (value.Type.Symbol?.IsAmbient == true)
        {
            active = Prepend(
                new XamlAmbientValueInfo(
                    XamlAmbientValueOrigin.Type,
                    value.StableId,
                    value,
                    value.Type.Symbol),
                active);
        }

        var orderedMembers = ImmutableArray.CreateRange(
            XamlBoundMemberOrdering.Order(value.Members));
        var ownerAmbientValues = ImmutableArray.CreateBuilder<XamlAmbientValueInfo>();
        foreach (var member in orderedMembers)
        {
            var symbol = member.Member.Symbol;
            if (symbol?.IsAmbient == true)
                ownerAmbientValues.AddRange(CreateMemberEntries(value, member, symbol));
        }
        var ownerContext = ownerAmbientValues.Count == 0
            ? active
            : Prepend(ownerAmbientValues.ToImmutable(), active);

        SetContext(value.StableId, ownerContext, deferredBoundaries, contexts);
        foreach (var member in orderedMembers)
        {
            SetContext(member.StableId, ownerContext, deferredBoundaries, contexts);
            var symbol = member.Member.Symbol;
            var childBoundaries = symbol?.Kind == XamlMemberKind.DeferredContent
                ? deferredBoundaries.Add(member.StableId)
                : deferredBoundaries;

            foreach (var child in member.Values)
            {
                SetContext(child.StableId, ownerContext, childBoundaries, contexts);
                if (child is XamlBoundObject childObject)
                    VisitObject(childObject, ownerContext, childBoundaries, contexts);
                else if (child is XamlBoundCompiledBinding compiled)
                    VisitObject(compiled.Extension, ownerContext, childBoundaries, contexts);
            }
        }
    }

    private static ImmutableArray<XamlAmbientValueInfo> CreateMemberEntries(
        XamlBoundObject owner,
        XamlBoundMember member,
        XamlMemberInfo symbol)
    {
        var result = ImmutableArray.CreateBuilder<XamlAmbientValueInfo>(
            member.Values.Length);
        foreach (var value in member.Values)
        {
            result.Add(new XamlAmbientValueInfo(
                XamlAmbientValueOrigin.Member,
                owner.StableId,
                value,
                symbol.ValueType,
                symbol,
                member.StableId));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<XamlAmbientValueInfo> Prepend(
        XamlAmbientValueInfo value,
        ImmutableArray<XamlAmbientValueInfo> existing)
    {
        var result = ImmutableArray.CreateBuilder<XamlAmbientValueInfo>(
            existing.Length + 1);
        result.Add(value);
        result.AddRange(existing);
        return result.ToImmutable();
    }

    private static ImmutableArray<XamlAmbientValueInfo> Prepend(
        ImmutableArray<XamlAmbientValueInfo> values,
        ImmutableArray<XamlAmbientValueInfo> existing)
    {
        var result = ImmutableArray.CreateBuilder<XamlAmbientValueInfo>(
            values.Length + existing.Length);
        result.AddRange(values);
        result.AddRange(existing);
        return result.ToImmutable();
    }

    private static void SetContext(
        ulong stableId,
        ImmutableArray<XamlAmbientValueInfo> values,
        ImmutableArray<ulong> deferredBoundaries,
        ImmutableDictionary<ulong, XamlAmbientContextInfo>.Builder contexts)
    {
        contexts[stableId] = new XamlAmbientContextInfo(
            stableId,
            values,
            deferredBoundaries);
    }
}
