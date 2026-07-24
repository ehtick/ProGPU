using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using ProGPU.Xaml.Binding;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Serialization;

public enum XamlConstructorRepresentationMode
{
    PreserveMemberAssignments,
    PreferSingleMappedMember
}

public sealed class XamlSerializationPlanOptions
{
    public XamlConstructorRepresentationMode ConstructorRepresentation { get; set; } =
        XamlConstructorRepresentationMode.PreserveMemberAssignments;
}

public enum XamlSerializationDisposition
{
    Omit,
    Directive,
    Attribute,
    Element,
    Content,
    ConstructorArgument
}

public enum XamlSerializationPlanIssueKind
{
    InvalidMemberPolicy,
    AmbiguousConstructorRepresentation,
    InvalidConstructorValueCardinality
}

public sealed class XamlSerializationPlanIssue
{
    public XamlSerializationPlanIssue(
        XamlSerializationPlanIssueKind kind,
        ulong objectStableId,
        ulong? memberStableId,
        string message)
    {
        Kind = kind;
        ObjectStableId = objectStableId;
        MemberStableId = memberStableId;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public XamlSerializationPlanIssueKind Kind { get; }
    public ulong ObjectStableId { get; }
    public ulong? MemberStableId { get; }
    public string Message { get; }
}

public sealed class XamlMemberSerializationPlan
{
    internal XamlMemberSerializationPlan(
        XamlBoundMember source,
        int sourceIndex,
        int serializationIndex,
        XamlSerializationDisposition disposition,
        XamlConstructorArgumentShapeInfo? constructorArgument,
        XamlValueSerializerShapeInfo? valueSerializer)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        SourceIndex = sourceIndex;
        SerializationIndex = serializationIndex;
        Disposition = disposition;
        ConstructorArgument = constructorArgument;
        ValueSerializer = valueSerializer;
    }

    public XamlBoundMember Source { get; }
    public XamlMemberInfo? Member => Source.Member.Symbol;
    public int SourceIndex { get; }
    public int SerializationIndex { get; }
    public XamlSerializationDisposition Disposition { get; }
    public bool IsIncluded => Disposition != XamlSerializationDisposition.Omit;
    public XamlConstructorArgumentShapeInfo? ConstructorArgument { get; }
    public XamlValueSerializerShapeInfo? ValueSerializer { get; }
    public XamlDefaultValueInfo? DefaultValue => Member?.DefaultValue;
    public IMethodSymbol? ShouldSerializeMethod =>
        Member?.SerializationPolicy.ShouldSerializeMethod;
    public IMethodSymbol? ResetMethod =>
        Member?.SerializationPolicy.ResetMethod;
    public bool RequiresRuntimeShouldSerialize => ShouldSerializeMethod != null;
}

public sealed class XamlObjectSerializationPlan
{
    internal XamlObjectSerializationPlan(
        XamlBoundObject source,
        ImmutableArray<XamlMemberSerializationPlan> members,
        XamlConstructorInfo? constructor,
        ImmutableArray<XamlSerializationPlanIssue> issues)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Members = members;
        Constructor = constructor;
        Issues = issues;
    }

    public XamlBoundObject Source { get; }
    public XamlTypeInfo? Type => Source.Type.Symbol;
    public ImmutableArray<XamlMemberSerializationPlan> Members { get; }
    public XamlConstructorInfo? Constructor { get; }
    public bool UsesConstructorArgument => Constructor != null;
    public ImmutableArray<XamlSerializationPlanIssue> Issues { get; }
    public bool IsValid => Issues.IsEmpty;
}

public sealed class XamlSerializationPlanGraph
{
    internal XamlSerializationPlanGraph(
        XamlObjectSerializationPlan? root,
        IReadOnlyDictionary<ulong, XamlObjectSerializationPlan> objects,
        ImmutableArray<XamlSerializationPlanIssue> issues)
    {
        Root = root;
        Objects = objects ?? throw new ArgumentNullException(nameof(objects));
        Issues = issues;
    }

    public XamlObjectSerializationPlan? Root { get; }
    public IReadOnlyDictionary<ulong, XamlObjectSerializationPlan> Objects { get; }
    public ImmutableArray<XamlSerializationPlanIssue> Issues { get; }
    public bool IsValid => Issues.IsEmpty;
}

/// <summary>
/// Builds the canonical save/editor view of a bound object graph. The planner is pure:
/// it never reads runtime property values, invokes ShouldSerialize/Reset, constructs
/// framework attributes, or changes load-path binding.
/// </summary>
public sealed class XamlSerializationPlanner
{
    public XamlSerializationPlanGraph Build(
        XamlBoundDocument document,
        XamlSerializationPlanOptions? options = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        options ??= new XamlSerializationPlanOptions();
        var objects = new Dictionary<ulong, XamlObjectSerializationPlan>();
        var issues = ImmutableArray.CreateBuilder<XamlSerializationPlanIssue>();
        var root = document.Root == null
            ? null
            : BuildObject(document.Root, options, objects, issues);
        return new XamlSerializationPlanGraph(
            root,
            objects,
            issues.ToImmutable());
    }

    private static XamlObjectSerializationPlan BuildObject(
        XamlBoundObject value,
        XamlSerializationPlanOptions options,
        Dictionary<ulong, XamlObjectSerializationPlan> objects,
        ImmutableArray<XamlSerializationPlanIssue>.Builder graphIssues)
    {
        if (objects.TryGetValue(value.StableId, out var existing))
            return existing;

        var sourceIndices = new Dictionary<ulong, int>();
        for (var index = 0; index < value.Members.Length; index++)
            sourceIndices[value.Members[index].StableId] = index;
        var ordered = XamlBoundMemberOrdering.Order(value.Members).ToArray();
        var localIssues = ImmutableArray.CreateBuilder<XamlSerializationPlanIssue>();
        XamlBoundMember? constructorMember = null;
        XamlConstructorArgumentShapeInfo? constructorShape = null;
        if (options.ConstructorRepresentation ==
            XamlConstructorRepresentationMode.PreferSingleMappedMember)
        {
            var candidates = ordered.Where(static member =>
                member.Member.Symbol?.SerializationPolicy.Include == true &&
                member.Member.Symbol.ConstructorArgument?.IsValid == true)
                .ToArray();
            if (candidates.Length == 1)
            {
                constructorMember = candidates[0];
                constructorShape = constructorMember.Member.Symbol!.ConstructorArgument;
                if (constructorMember.Values.Length != 1)
                {
                    localIssues.Add(new XamlSerializationPlanIssue(
                        XamlSerializationPlanIssueKind.InvalidConstructorValueCardinality,
                        value.StableId,
                        constructorMember.StableId,
                        $"Constructor-mapped member '{constructorMember.Member.Symbol.Name}' must have exactly one serialized value."));
                    constructorMember = null;
                    constructorShape = null;
                }
            }
            else if (candidates.Length > 1)
            {
                localIssues.Add(new XamlSerializationPlanIssue(
                    XamlSerializationPlanIssueKind.AmbiguousConstructorRepresentation,
                    value.StableId,
                    null,
                    "More than one supplied member offers an alternative one-argument constructor representation."));
            }
        }

        var members = ImmutableArray.CreateBuilder<XamlMemberSerializationPlan>(
            ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var source = ordered[index];
            var member = source.Member.Symbol;
            var disposition = GetDisposition(
                source,
                ReferenceEquals(source, constructorMember));
            if (member != null && !member.SerializationPolicy.IsValid)
            {
                localIssues.Add(new XamlSerializationPlanIssue(
                    XamlSerializationPlanIssueKind.InvalidMemberPolicy,
                    value.StableId,
                    source.StableId,
                    $"Member '{member.Name}' has invalid serialization metadata."));
            }
            members.Add(new XamlMemberSerializationPlan(
                source,
                sourceIndices[source.StableId],
                index,
                disposition,
                ReferenceEquals(source, constructorMember)
                    ? constructorShape
                    : null,
                member?.ValueSerializer ?? member?.ValueType.ValueSerializer));

            foreach (var child in source.Values)
            {
                var childObject = child switch
                {
                    XamlBoundObject direct => direct,
                    XamlBoundBinding binding => binding.Extension,
                    XamlBoundCompiledBinding compiledBinding =>
                        compiledBinding.Extension,
                    _ => null
                };
                if (childObject != null)
                    BuildObject(childObject, options, objects, graphIssues);
            }
        }

        var plan = new XamlObjectSerializationPlan(
            value,
            members.ToImmutable(),
            constructorShape == null
                ? null
                : value.Type.Symbol?.Constructors.FirstOrDefault(candidate =>
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Symbol,
                        constructorShape.Constructor)),
            localIssues.ToImmutable());
        objects[value.StableId] = plan;
        graphIssues.AddRange(plan.Issues);
        return plan;
    }

    private static XamlSerializationDisposition GetDisposition(
        XamlBoundMember source,
        bool isConstructorArgument)
    {
        if (isConstructorArgument)
            return XamlSerializationDisposition.ConstructorArgument;
        var member = source.Member.Symbol;
        if (member == null)
            return source.Member.Kind == XamlBoundReferenceKind.Directive
                ? XamlSerializationDisposition.Directive
                : XamlSerializationDisposition.Element;
        if (!member.SerializationPolicy.Include)
            return XamlSerializationDisposition.Omit;
        if (member.SerializationPolicy.IsContent)
            return XamlSerializationDisposition.Content;
        if (member.SerializationPolicy.PreferAttribute)
            return XamlSerializationDisposition.Attribute;
        return source.Origin == Infoset.XamlMemberOrigin.Attribute
            ? XamlSerializationDisposition.Attribute
            : XamlSerializationDisposition.Element;
    }
}
