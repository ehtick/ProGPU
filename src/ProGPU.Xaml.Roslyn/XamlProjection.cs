using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Roslyn;

public enum XamlProjectionKind
{
    Construction,
    Literal,
    MemberAssignment,
    Event,
    Name,
    Resource,
    Binding
}

public sealed class XamlProjectionEntry
{
    internal XamlProjectionEntry(
        string path,
        string checksum,
        ulong stableNodeId,
        XamlProjectionKind kind,
        string? memberId,
        TextSpan sourceSpan,
        SyntaxNodeOrToken generatedNode)
    {
        Path = path;
        Checksum = checksum;
        StableNodeId = stableNodeId;
        Kind = kind;
        MemberId = memberId;
        SourceSpan = sourceSpan;
        GeneratedNode = generatedNode;
    }

    public string Path { get; }
    public string Checksum { get; }
    public ulong StableNodeId { get; }
    public XamlProjectionKind Kind { get; }
    public string? MemberId { get; }
    public TextSpan SourceSpan { get; }
    public SyntaxNodeOrToken GeneratedNode { get; }
}

public static class XamlProjectionMap
{
    public const string AnnotationKind = "ProGPU.Xaml.Source";

    public static ImmutableArray<XamlProjectionEntry> Read(SyntaxTree generatedTree)
    {
        if (generatedTree == null) throw new ArgumentNullException(nameof(generatedTree));
        var builder = ImmutableArray.CreateBuilder<XamlProjectionEntry>();
        foreach (var nodeOrToken in generatedTree.GetRoot().GetAnnotatedNodesAndTokens(AnnotationKind))
        {
            foreach (var annotation in nodeOrToken.GetAnnotations(AnnotationKind))
            {
                if (TryDecode(annotation.Data, nodeOrToken, out var entry)) builder.Add(entry!);
            }
        }
        return builder.ToImmutable();
    }

    internal static SyntaxAnnotation CreateAnnotation(
        string path,
        string checksum,
        ulong stableNodeId,
        XamlProjectionKind kind,
        ISymbol? member,
        TextSpan sourceSpan)
    {
        var memberId = member?.GetDocumentationCommentId() ?? member?.ToDisplayString();
        var data = string.Join(";", new[]
        {
            Encode(path),
            checksum,
            stableNodeId.ToString("x16", CultureInfo.InvariantCulture),
            ((int)kind).ToString(CultureInfo.InvariantCulture),
            Encode(memberId ?? string.Empty),
            sourceSpan.Start.ToString(CultureInfo.InvariantCulture),
            sourceSpan.Length.ToString(CultureInfo.InvariantCulture)
        });
        return new SyntaxAnnotation(AnnotationKind, data);
    }

    private static bool TryDecode(
        string? data,
        SyntaxNodeOrToken generatedNode,
        out XamlProjectionEntry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(data)) return false;
        var parts = data!.Split(';');
        if (parts.Length != 7 ||
            !ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var stableId) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kindValue) ||
            !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
            !Enum.IsDefined(typeof(XamlProjectionKind), kindValue)) return false;
        var member = Decode(parts[4]);
        entry = new XamlProjectionEntry(
            Decode(parts[0]), parts[1], stableId, (XamlProjectionKind)kindValue,
            member.Length == 0 ? null : member, new TextSpan(start, length), generatedNode);
        return true;
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
