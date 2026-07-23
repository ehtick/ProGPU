using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Infoset;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Tooling;

public enum XamlInspectionEntryKind
{
    SyntaxObject,
    SyntaxAttribute,
    SyntaxText,
    Token,
    InfosetObject,
    InfosetMember,
    InfosetText,
    Diagnostic
}

public sealed class XamlInspectionEntry
{
    internal XamlInspectionEntry(
        XamlInspectionEntryKind kind,
        int depth,
        string name,
        string value,
        TextSpan sourceSpan,
        ulong stableId = 0)
    {
        Kind = kind;
        Depth = depth;
        Name = name;
        Value = value;
        SourceSpan = sourceSpan;
        StableId = stableId;
    }

    public XamlInspectionEntryKind Kind { get; }
    public int Depth { get; }
    public string Name { get; }
    public string Value { get; }
    public TextSpan SourceSpan { get; }
    public ulong StableId { get; }
    public bool HasStableId =>
        Kind != XamlInspectionEntryKind.Token &&
        Kind != XamlInspectionEntryKind.Diagnostic;
}

public sealed class XamlInspectionProjection
{
    internal XamlInspectionProjection(
        ImmutableArray<XamlInspectionEntry> entries,
        int totalEntryCount)
    {
        Entries = entries;
        TotalEntryCount = totalEntryCount;
    }

    public ImmutableArray<XamlInspectionEntry> Entries { get; }
    public int TotalEntryCount { get; }
    public bool IsTruncated => Entries.Length != TotalEntryCount;
}

public sealed class XamlInspectionStatistics
{
    internal XamlInspectionStatistics(
        int tokens,
        int syntaxObjects,
        int syntaxAttributes,
        int syntaxTextValues,
        int infosetObjects,
        int infosetMembers,
        int infosetTextValues,
        int diagnostics,
        int errors)
    {
        Tokens = tokens;
        SyntaxObjects = syntaxObjects;
        SyntaxAttributes = syntaxAttributes;
        SyntaxTextValues = syntaxTextValues;
        InfosetObjects = infosetObjects;
        InfosetMembers = infosetMembers;
        InfosetTextValues = infosetTextValues;
        Diagnostics = diagnostics;
        Errors = errors;
    }

    public int Tokens { get; }
    public int SyntaxObjects { get; }
    public int SyntaxAttributes { get; }
    public int SyntaxTextValues { get; }
    public int InfosetObjects { get; }
    public int InfosetMembers { get; }
    public int InfosetTextValues { get; }
    public int Diagnostics { get; }
    public int Errors { get; }
}

public sealed class XamlDocumentInspection
{
    internal XamlDocumentInspection(
        XamlSyntaxTree syntaxTree,
        XamlInfosetDocument infoset,
        XamlInspectionProjection syntax,
        XamlInspectionProjection tokens,
        XamlInspectionProjection infosetProjection,
        XamlInspectionProjection diagnostics,
        XamlInspectionStatistics statistics)
    {
        SyntaxTree = syntaxTree;
        Infoset = infoset;
        Syntax = syntax;
        Tokens = tokens;
        InfosetProjection = infosetProjection;
        Diagnostics = diagnostics;
        Statistics = statistics;
    }

    public XamlSyntaxTree SyntaxTree { get; }
    public XamlInfosetDocument Infoset { get; }
    public XamlInspectionProjection Syntax { get; }
    public XamlInspectionProjection Tokens { get; }
    public XamlInspectionProjection InfosetProjection { get; }
    public XamlInspectionProjection Diagnostics { get; }
    public XamlInspectionStatistics Statistics { get; }
}

public sealed class XamlDocumentInspectionOptions
{
    public const int MaximumSupportedProjectionEntries = 2 * 1024 * 1024;
    public const int MaximumSupportedPreviewLength = 16 * 1024;

    public XamlParseOptions ParseOptions { get; set; } = new XamlParseOptions
    {
        Mode = XamlParseMode.Recovering
    };

    public XamlInfosetConversionOptions? InfosetOptions { get; set; }
    public int MaximumProjectionEntries { get; set; } = 100 * 1024;
    public int MaximumPreviewLength { get; set; } = 256;
}

/// <summary>
/// Produces bounded, immutable tooling projections over the canonical lossless syntax tree
/// and schema-neutral infoset. The projections are presentation-neutral and retain source
/// spans and stable identities so playgrounds, CLI hosts, and IDE adapters can share them.
/// </summary>
public sealed class XamlDocumentInspectionService
{
    public XamlDocumentInspection Inspect(
        SourceText source,
        string path = "",
        XamlDocumentInspectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        options = options ?? new XamlDocumentInspectionOptions();
        if (options.ParseOptions == null) throw new ArgumentNullException(nameof(options.ParseOptions));
        ValidateLimit(
            options.MaximumProjectionEntries,
            XamlDocumentInspectionOptions.MaximumSupportedProjectionEntries,
            nameof(options.MaximumProjectionEntries));
        ValidateLimit(
            options.MaximumPreviewLength,
            XamlDocumentInspectionOptions.MaximumSupportedPreviewLength,
            nameof(options.MaximumPreviewLength));

        var maximumEntries = options.MaximumProjectionEntries;
        var maximumPreviewLength = options.MaximumPreviewLength;
        var syntaxTree = XamlParser.Parse(
            source,
            path ?? string.Empty,
            options.ParseOptions,
            cancellationToken);
        var infosetOptions = options.InfosetOptions ?? new XamlInfosetConversionOptions
        {
            Mode = syntaxTree.Options.Mode
        };
        var infoset = new XamlInfosetConverter().Convert(
            syntaxTree.Document,
            infosetOptions,
            cancellationToken);

        var syntax = ProjectSyntax(
            syntaxTree.GetRoot(cancellationToken),
            maximumEntries,
            maximumPreviewLength,
            cancellationToken,
            out var syntaxObjects,
            out var syntaxAttributes,
            out var syntaxTextValues);
        var tokens = ProjectTokens(
            syntaxTree,
            maximumEntries,
            maximumPreviewLength,
            cancellationToken);
        var infosetProjection = ProjectInfoset(
            infoset.Root,
            maximumEntries,
            maximumPreviewLength,
            cancellationToken,
            out var infosetObjects,
            out var infosetMembers,
            out var infosetTextValues);
        var diagnostics = ProjectDiagnostics(
            infoset.Diagnostics,
            maximumEntries,
            maximumPreviewLength,
            cancellationToken,
            out var errors);
        var statistics = new XamlInspectionStatistics(
            syntaxTree.Tokens.Length,
            syntaxObjects,
            syntaxAttributes,
            syntaxTextValues,
            infosetObjects,
            infosetMembers,
            infosetTextValues,
            infoset.Diagnostics.Length,
            errors);
        return new XamlDocumentInspection(
            syntaxTree,
            infoset,
            syntax,
            tokens,
            infosetProjection,
            diagnostics,
            statistics);
    }

    private static XamlInspectionProjection ProjectSyntax(
        XamlObjectSyntax? root,
        int maximumEntries,
        int maximumPreviewLength,
        CancellationToken cancellationToken,
        out int objects,
        out int attributes,
        out int textValues)
    {
        var builder = ImmutableArray.CreateBuilder<XamlInspectionEntry>(
            Math.Min(maximumEntries, 1024));
        objects = 0;
        attributes = 0;
        textValues = 0;
        var total = 0;
        if (root == null)
            return new XamlInspectionProjection(builder.ToImmutable(), total);

        var stack = new Stack<SyntaxFrame>();
        stack.Push(new SyntaxFrame(root, 0));
        while (stack.Count != 0)
        {
            if ((total & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Pop();
            var node = frame.Node;
            switch (node)
            {
                case XamlObjectSyntax value:
                    objects++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.SyntaxObject,
                        frame.Depth,
                        value.QualifiedName,
                        value.NamespaceUri,
                        value.Span,
                        value.StableId);
                    break;
                case XamlAttributeSyntax value:
                    attributes++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.SyntaxAttribute,
                        frame.Depth,
                        value.QualifiedName,
                        Preview(value.Value, maximumPreviewLength),
                        value.Span,
                        value.StableId);
                    break;
                case XamlTextSyntax value:
                    textValues++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.SyntaxText,
                        frame.Depth,
                        value.IsCData ? "CDATA" : "Text",
                        Preview(value.Text, maximumPreviewLength),
                        value.Span,
                        value.StableId);
                    break;
            }
            total++;

            var children = node.ChildNodes();
            for (var index = children.Length - 1; index >= 0; index--)
                stack.Push(new SyntaxFrame(children[index], frame.Depth + 1));
        }
        return new XamlInspectionProjection(builder.ToImmutable(), total);
    }

    private static XamlInspectionProjection ProjectTokens(
        XamlSyntaxTree syntaxTree,
        int maximumEntries,
        int maximumPreviewLength,
        CancellationToken cancellationToken)
    {
        var tokens = syntaxTree.Tokens;
        var builder = ImmutableArray.CreateBuilder<XamlInspectionEntry>(
            Math.Min(maximumEntries, tokens.Length));
        var source = syntaxTree.GetText(cancellationToken);
        for (var index = 0; index < tokens.Length; index++)
        {
            if ((index & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var token = tokens[index];
            Add(
                builder,
                maximumEntries,
                XamlInspectionEntryKind.Token,
                0,
                token.Kind.ToString(),
                Preview(source, token.Span, maximumPreviewLength),
                token.Span);
        }
        return new XamlInspectionProjection(builder.ToImmutable(), tokens.Length);
    }

    private static XamlInspectionProjection ProjectInfoset(
        XamlInfosetObject? root,
        int maximumEntries,
        int maximumPreviewLength,
        CancellationToken cancellationToken,
        out int objects,
        out int members,
        out int textValues)
    {
        var builder = ImmutableArray.CreateBuilder<XamlInspectionEntry>(
            Math.Min(maximumEntries, 1024));
        objects = 0;
        members = 0;
        textValues = 0;
        var total = 0;
        if (root == null)
            return new XamlInspectionProjection(builder.ToImmutable(), total);

        var stack = new Stack<InfosetFrame>();
        stack.Push(new InfosetFrame(root, 0));
        while (stack.Count != 0)
        {
            if ((total & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Pop();
            var value = frame.Value;
            switch (value)
            {
                case XamlInfosetObject objectValue:
                    objects++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.InfosetObject,
                        frame.Depth,
                        objectValue.TypeName.DisplayName,
                        objectValue.IsMarkupExtension ? "markup-extension" : objectValue.TypeName.NamespaceUri,
                        objectValue.SourceSpan,
                        objectValue.StableId);
                    for (var index = objectValue.Members.Length - 1; index >= 0; index--)
                        stack.Push(new InfosetFrame(objectValue.Members[index], frame.Depth + 1));
                    break;
                case XamlInfosetMember memberValue:
                    members++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.InfosetMember,
                        frame.Depth,
                        memberValue.Name.DisplayName,
                        memberValue.Origin.ToString(),
                        memberValue.SourceSpan,
                        memberValue.StableId);
                    for (var index = memberValue.Values.Length - 1; index >= 0; index--)
                        stack.Push(new InfosetFrame(memberValue.Values[index], frame.Depth + 1));
                    break;
                case XamlInfosetText textValue:
                    textValues++;
                    Add(
                        builder,
                        maximumEntries,
                        XamlInspectionEntryKind.InfosetText,
                        frame.Depth,
                        textValue.IsCData ? "CDATA" : "Text",
                        Preview(textValue.Text, maximumPreviewLength),
                        textValue.SourceSpan,
                        textValue.StableId);
                    break;
            }
            total++;
        }
        return new XamlInspectionProjection(builder.ToImmutable(), total);
    }

    private static XamlInspectionProjection ProjectDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        int maximumEntries,
        int maximumPreviewLength,
        CancellationToken cancellationToken,
        out int errors)
    {
        var builder = ImmutableArray.CreateBuilder<XamlInspectionEntry>(
            Math.Min(maximumEntries, diagnostics.Length));
        errors = 0;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            if ((index & 0xff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var diagnostic = diagnostics[index];
            if (diagnostic.Severity == DiagnosticSeverity.Error) errors++;
            var span = diagnostic.Location.IsInSource
                ? diagnostic.Location.SourceSpan
                : default;
            Add(
                builder,
                maximumEntries,
                XamlInspectionEntryKind.Diagnostic,
                0,
                diagnostic.Id + " " + diagnostic.Severity,
                Preview(diagnostic.GetMessage(), maximumPreviewLength),
                span);
        }
        return new XamlInspectionProjection(builder.ToImmutable(), diagnostics.Length);
    }

    private static void Add(
        ImmutableArray<XamlInspectionEntry>.Builder builder,
        int maximumEntries,
        XamlInspectionEntryKind kind,
        int depth,
        string name,
        string value,
        TextSpan sourceSpan,
        ulong stableId = 0)
    {
        if (builder.Count < maximumEntries)
            builder.Add(new XamlInspectionEntry(
                kind,
                depth,
                name,
                value,
                sourceSpan,
                stableId));
    }

    private static string Preview(
        SourceText source,
        TextSpan span,
        int maximumLength)
    {
        var length = Math.Min(span.Length, maximumLength);
        var value = length == 0
            ? string.Empty
            : source.ToString(new TextSpan(span.Start, length));
        return Preview(value, maximumLength, span.Length > length);
    }

    private static string Preview(string value, int maximumLength) =>
        Preview(value, maximumLength, value.Length > maximumLength);

    private static string Preview(
        string value,
        int maximumLength,
        bool forceEllipsis)
    {
        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        var truncated = forceEllipsis || value.Length > maximumLength;
        for (var index = 0; index < value.Length; index++)
        {
            string? escaped = null;
            switch (value[index])
            {
                case '\r':
                    escaped = "\\r";
                    break;
                case '\n':
                    escaped = "\\n";
                    break;
                case '\t':
                    escaped = "\\t";
                    break;
            }
            var required = escaped == null ? 1 : escaped.Length;
            if (builder.Length + required > maximumLength)
            {
                truncated = true;
                break;
            }
            if (escaped == null)
                builder.Append(value[index]);
            else
                builder.Append(escaped);
        }
        if (truncated)
        {
            if (builder.Length == maximumLength)
                builder[builder.Length - 1] = '…';
            else
                builder.Append('…');
        }
        return builder.ToString();
    }

    private static void ValidateLimit(int value, int maximum, string parameterName)
    {
        if (value <= 0 || value > maximum)
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be between 1 and {maximum}.");
    }

    private readonly struct SyntaxFrame
    {
        public SyntaxFrame(XamlSyntaxNode node, int depth)
        {
            Node = node;
            Depth = depth;
        }

        public XamlSyntaxNode Node { get; }
        public int Depth { get; }
    }

    private readonly struct InfosetFrame
    {
        public InfosetFrame(XamlInfosetValue value, int depth)
        {
            Value = value;
            Depth = depth;
        }

        public XamlInfosetValue Value { get; }
        public int Depth { get; }
    }
}
