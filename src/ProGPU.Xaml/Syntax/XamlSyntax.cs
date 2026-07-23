using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Syntax;

public static class XamlNamespaces
{
    public const string Language2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string Presentation2006 = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string Xml = "http://www.w3.org/XML/1998/namespace";
    public const string Xmlns = "http://www.w3.org/2000/xmlns/";
}

public enum XamlSyntaxKind
{
    Element,
    Attribute,
    Text
}

public enum XamlTokenKind
{
    LessThan,
    LessThanSlash,
    GreaterThan,
    SlashGreaterThan,
    Name,
    Equals,
    StringLiteral,
    Text,
    Whitespace,
    Comment,
    CData,
    ProcessingInstruction,
    DocumentType,
    BadToken,
    EndOfFile
}

public readonly struct XamlSyntaxToken : IEquatable<XamlSyntaxToken>
{
    private readonly SourceText _source;

    internal XamlSyntaxToken(XamlTokenKind kind, SourceText source, TextSpan span, bool isMissing = false)
    {
        Kind = kind;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Span = span;
        IsMissing = isMissing;
    }

    public XamlTokenKind Kind { get; }
    public int RawKind => (int)Kind;
    public string Text => Span.Length == 0 ? string.Empty : _source.ToString(Span);
    public string ValueText => Kind == XamlTokenKind.StringLiteral && Text.Length >= 2
        ? Text.Substring(1, Text.Length - 2)
        : Text;
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;
    public bool IsTrivia => Kind == XamlTokenKind.Whitespace || Kind == XamlTokenKind.Comment || Kind == XamlTokenKind.ProcessingInstruction;
    public bool IsMissing { get; }

    public bool Equals(XamlSyntaxToken other) => Kind == other.Kind && Span == other.Span && IsMissing == other.IsMissing && ReferenceEquals(_source, other._source);
    public override bool Equals(object? obj) => obj is XamlSyntaxToken other && Equals(other);
    public override int GetHashCode() => unchecked((((int)Kind * 397) ^ Span.GetHashCode()) * 397 ^ IsMissing.GetHashCode());
    public override string ToString() => Text;
}

public sealed class XamlNamespaceDeclaration
{
    internal XamlNamespaceDeclaration(string prefix, string namespaceUri, TextSpan span)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        Span = span;
    }

    public string Prefix { get; }
    public string NamespaceUri { get; }
    public TextSpan Span { get; }
}

internal abstract class GreenXamlNode
{
    protected GreenXamlNode(XamlSyntaxKind kind, TextSpan span, ulong stableId)
    {
        Kind = kind;
        Span = span;
        StableId = stableId;
    }

    public XamlSyntaxKind Kind { get; }
    public TextSpan Span { get; }
    public ulong StableId { get; }
}

internal sealed class GreenXamlAttribute : GreenXamlNode
{
    public GreenXamlAttribute(
        string prefix,
        string namespaceUri,
        string localName,
        string value,
        TextSpan span,
        TextSpan valueSpan,
        ulong stableId)
        : base(XamlSyntaxKind.Attribute, span, stableId)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Value = value;
        ValueSpan = valueSpan;
    }

    public string Prefix { get; }
    public string NamespaceUri { get; }
    public string LocalName { get; }
    public string Value { get; }
    public TextSpan ValueSpan { get; }
}

internal sealed class GreenXamlText : GreenXamlNode
{
    public GreenXamlText(string text, bool isCData, TextSpan span, ulong stableId)
        : base(XamlSyntaxKind.Text, span, stableId)
    {
        Text = text;
        IsCData = isCData;
    }

    public string Text { get; }
    public bool IsCData { get; }
}

internal sealed class GreenXamlElement : GreenXamlNode
{
    public GreenXamlElement(
        string prefix,
        string namespaceUri,
        string localName,
        ImmutableArray<GreenXamlAttribute> attributes,
        ImmutableArray<GreenXamlNode> children,
        ImmutableArray<XamlNamespaceDeclaration> namespaceDeclarations,
        TextSpan span,
        ulong stableId)
        : base(XamlSyntaxKind.Element, span, stableId)
    {
        Prefix = prefix;
        NamespaceUri = namespaceUri;
        LocalName = localName;
        Attributes = attributes;
        Children = children;
        NamespaceDeclarations = namespaceDeclarations;
    }

    public string Prefix { get; }
    public string NamespaceUri { get; }
    public string LocalName { get; }
    public ImmutableArray<GreenXamlAttribute> Attributes { get; }
    public ImmutableArray<GreenXamlNode> Children { get; }
    public ImmutableArray<XamlNamespaceDeclaration> NamespaceDeclarations { get; }
}

public abstract class XamlSyntaxNode
{
    private readonly ImmutableArray<SyntaxAnnotation> _annotations;

    internal XamlSyntaxNode(
        GreenXamlNode green,
        XamlSyntaxTree syntaxTree,
        XamlSyntaxNode? parent,
        ImmutableArray<SyntaxAnnotation> annotations = default)
    {
        Green = green;
        SyntaxTree = syntaxTree;
        Parent = parent;
        _annotations = annotations.IsDefault ? ImmutableArray<SyntaxAnnotation>.Empty : annotations;
    }

    internal GreenXamlNode Green { get; }
    public XamlSyntaxTree SyntaxTree { get; }
    public XamlSyntaxNode? Parent { get; }
    public XamlSyntaxKind Kind => Green.Kind;
    public int RawKind => (int)Kind;
    public TextSpan Span => Green.Span;
    public TextSpan FullSpan => Green.Span;
    public int SpanStart => Span.Start;
    public int FullWidth => FullSpan.Length;
    public ulong StableId => Green.StableId;
    public bool ContainsDiagnostics => SyntaxTree.GetDiagnostics(Span).Any();

    public Location GetLocation() => SyntaxTree.GetLocation(Span);
    public abstract ImmutableArray<XamlSyntaxNode> ChildNodes();
    public abstract TResult Accept<TResult>(XamlSyntaxVisitor<TResult> visitor);

    public IEnumerable<XamlSyntaxNode> Ancestors(bool ascendOutOfTrivia = true)
    {
        for (var current = Parent; current != null; current = current.Parent)
        {
            yield return current;
        }
    }

    public IEnumerable<XamlSyntaxNode> DescendantNodes(bool descendIntoChildren = true)
    {
        if (!descendIntoChildren)
        {
            yield break;
        }
        var stack = new Stack<XamlSyntaxNode>(ChildNodes().Reverse());
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            yield return node;
            var children = node.ChildNodes();
            for (var index = children.Length - 1; index >= 0; index--)
            {
                stack.Push(children[index]);
            }
        }
    }

    public string ToFullString() => SyntaxTree.GetText().ToString(FullSpan);
    public override string ToString() => SyntaxTree.GetText().ToString(Span);

    public bool IsEquivalentTo(XamlSyntaxNode? other) =>
        other != null && Kind == other.Kind && string.Equals(ToFullString(), other.ToFullString(), StringComparison.Ordinal);

    public XamlSyntaxNode WithAdditionalAnnotations(params SyntaxAnnotation[] annotations)
    {
        if (annotations == null)
        {
            throw new ArgumentNullException(nameof(annotations));
        }

        return annotations.Length == 0 ? this : WithAnnotationsCore(_annotations.AddRange(annotations));
    }

    protected abstract XamlSyntaxNode WithAnnotationsCore(ImmutableArray<SyntaxAnnotation> annotations);

    public bool HasAnnotation(SyntaxAnnotation annotation) => _annotations.Contains(annotation);
    public IEnumerable<SyntaxAnnotation> GetAnnotations(string annotationKind) =>
        _annotations.Where(annotation => annotation.Kind == annotationKind);
}

public sealed class XamlAttributeSyntax : XamlSyntaxNode
{
    internal XamlAttributeSyntax(
        GreenXamlAttribute green,
        XamlSyntaxTree tree,
        XamlSyntaxNode parent,
        ImmutableArray<SyntaxAnnotation> annotations = default)
        : base(green, tree, parent, annotations) => AttributeGreen = green;

    private GreenXamlAttribute AttributeGreen { get; }
    public string Prefix => AttributeGreen.Prefix;
    public string NamespaceUri => AttributeGreen.NamespaceUri;
    public string LocalName => AttributeGreen.LocalName;
    public string Value => AttributeGreen.Value;
    public TextSpan ValueSpan => AttributeGreen.ValueSpan;
    public string QualifiedName => string.IsNullOrEmpty(Prefix) ? LocalName : Prefix + ":" + LocalName;
    public override ImmutableArray<XamlSyntaxNode> ChildNodes() => ImmutableArray<XamlSyntaxNode>.Empty;
    public override TResult Accept<TResult>(XamlSyntaxVisitor<TResult> visitor) => visitor.VisitAttribute(this);
    protected override XamlSyntaxNode WithAnnotationsCore(ImmutableArray<SyntaxAnnotation> annotations) =>
        new XamlAttributeSyntax(AttributeGreen, SyntaxTree, Parent!, annotations);
}

public sealed class XamlTextSyntax : XamlSyntaxNode
{
    internal XamlTextSyntax(
        GreenXamlText green,
        XamlSyntaxTree tree,
        XamlSyntaxNode parent,
        ImmutableArray<SyntaxAnnotation> annotations = default)
        : base(green, tree, parent, annotations) => TextGreen = green;

    private GreenXamlText TextGreen { get; }
    public string Text => TextGreen.Text;
    public bool IsCData => TextGreen.IsCData;
    public override ImmutableArray<XamlSyntaxNode> ChildNodes() => ImmutableArray<XamlSyntaxNode>.Empty;
    public override TResult Accept<TResult>(XamlSyntaxVisitor<TResult> visitor) => visitor.VisitText(this);
    protected override XamlSyntaxNode WithAnnotationsCore(ImmutableArray<SyntaxAnnotation> annotations) =>
        new XamlTextSyntax(TextGreen, SyntaxTree, Parent!, annotations);
}

public sealed class XamlObjectSyntax : XamlSyntaxNode
{
    private ImmutableArray<XamlAttributeSyntax> _attributes;
    private ImmutableArray<XamlSyntaxNode> _children;

    internal XamlObjectSyntax(
        GreenXamlElement green,
        XamlSyntaxTree tree,
        XamlSyntaxNode? parent,
        ImmutableArray<SyntaxAnnotation> annotations = default)
        : base(green, tree, parent, annotations) => ElementGreen = green;

    private GreenXamlElement ElementGreen { get; }
    public string Prefix => ElementGreen.Prefix;
    public string NamespaceUri => ElementGreen.NamespaceUri;
    public string LocalName => ElementGreen.LocalName;
    public string QualifiedName => string.IsNullOrEmpty(Prefix) ? LocalName : Prefix + ":" + LocalName;

    [Obsolete("Dotted-name classification belongs to the XML-to-XAML infoset conversion pass.")]
    public bool IsMemberElement => LocalName.IndexOf('.') >= 0;

    public ImmutableArray<XamlAttributeSyntax> Attributes
    {
        get
        {
            if (_attributes.IsDefault)
            {
                var builder = ImmutableArray.CreateBuilder<XamlAttributeSyntax>(ElementGreen.Attributes.Length);
                foreach (var green in ElementGreen.Attributes)
                {
                    builder.Add(new XamlAttributeSyntax(green, SyntaxTree, this));
                }
                ImmutableInterlocked.InterlockedInitialize(ref _attributes, builder.MoveToImmutable());
            }
            return _attributes;
        }
    }

    public ImmutableArray<XamlSyntaxNode> Children
    {
        get
        {
            if (_children.IsDefault)
            {
                var builder = ImmutableArray.CreateBuilder<XamlSyntaxNode>(ElementGreen.Children.Length);
                foreach (var green in ElementGreen.Children)
                {
                    builder.Add(green is GreenXamlElement element
                        ? new XamlObjectSyntax(element, SyntaxTree, this)
                        : new XamlTextSyntax((GreenXamlText)green, SyntaxTree, this));
                }
                ImmutableInterlocked.InterlockedInitialize(ref _children, builder.MoveToImmutable());
            }
            return _children;
        }
    }

    public ImmutableArray<XamlNamespaceDeclaration> NamespaceDeclarations => ElementGreen.NamespaceDeclarations;

    public XamlAttributeSyntax? FindAttribute(string namespaceUri, string localName)
    {
        foreach (var attribute in Attributes)
        {
            if (string.Equals(attribute.NamespaceUri, namespaceUri, StringComparison.Ordinal) &&
                string.Equals(attribute.LocalName, localName, StringComparison.Ordinal))
            {
                return attribute;
            }
        }
        return null;
    }

    public override ImmutableArray<XamlSyntaxNode> ChildNodes()
    {
        var builder = ImmutableArray.CreateBuilder<XamlSyntaxNode>(Attributes.Length + Children.Length);
        builder.AddRange(Attributes);
        builder.AddRange(Children);
        return builder.MoveToImmutable();
    }

    public override TResult Accept<TResult>(XamlSyntaxVisitor<TResult> visitor) => visitor.VisitObject(this);
    protected override XamlSyntaxNode WithAnnotationsCore(ImmutableArray<SyntaxAnnotation> annotations) =>
        new XamlObjectSyntax(ElementGreen, SyntaxTree, Parent, annotations);
}

public sealed class XamlDocumentSyntax
{
    internal XamlDocumentSyntax(XamlSyntaxTree tree)
    {
        SyntaxTree = tree;
        Root = tree.GetRoot();
    }

    public XamlSyntaxTree SyntaxTree { get; }
    public string Path => SyntaxTree.FilePath;
    public XamlObjectSyntax? Root { get; }
    public ImmutableArray<Diagnostic> Diagnostics => SyntaxTree.GetDiagnostics();
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}

public sealed class XamlSyntaxTree
{
    private readonly GreenXamlElement? _greenRoot;
    private XamlObjectSyntax? _root;

    internal XamlSyntaxTree(
        SourceText text,
        string filePath,
        GreenXamlElement? greenRoot,
        ImmutableArray<XamlSyntaxToken> tokens,
        ImmutableArray<Diagnostic> diagnostics,
        ProGPU.Xaml.Parsing.XamlParseOptions options)
    {
        Text = text;
        FilePath = filePath;
        _greenRoot = greenRoot;
        Tokens = tokens;
        Diagnostics = diagnostics;
        Options = options;
    }

    private SourceText Text { get; }
    public string FilePath { get; }
    public ProGPU.Xaml.Parsing.XamlParseOptions Options { get; }
    public ImmutableArray<XamlSyntaxToken> Tokens { get; }
    private ImmutableArray<Diagnostic> Diagnostics { get; }
    public XamlDocumentSyntax Document => new XamlDocumentSyntax(this);

    public SourceText GetText(CancellationToken cancellationToken = default) => Text;
    public bool TryGetText(out SourceText text) { text = Text; return true; }
    public XamlObjectSyntax? GetRoot(CancellationToken cancellationToken = default)
    {
        if (_greenRoot == null) return null;
        return LazyInitializer.EnsureInitialized(ref _root, () => new XamlObjectSyntax(_greenRoot, this, null));
    }
    public bool TryGetRoot(out XamlObjectSyntax? root) { root = GetRoot(); return root != null; }
    public ImmutableArray<Diagnostic> GetDiagnostics(CancellationToken cancellationToken = default) => Diagnostics;
    public IEnumerable<Diagnostic> GetDiagnostics(TextSpan span) => Diagnostics.Where(diagnostic => diagnostic.Location.SourceSpan.IntersectsWith(span));
    public Location GetLocation(TextSpan span) => Location.Create(FilePath, span, Text.Lines.GetLinePositionSpan(span));
    public FileLinePositionSpan GetLineSpan(TextSpan span) => new FileLinePositionSpan(FilePath, Text.Lines.GetLinePositionSpan(span));

    public XamlSyntaxTree WithChangedText(SourceText newText, CancellationToken cancellationToken = default) =>
        ProGPU.Xaml.Parsing.XamlParser.Parse(newText, FilePath, Options, cancellationToken);

    public IReadOnlyList<TextChange> GetChanges(XamlSyntaxTree oldTree) => Text.GetTextChanges(oldTree.Text).ToArray();
    public bool IsEquivalentTo(XamlSyntaxTree? other, bool topLevel = false) =>
        other != null && Tokens.Where(token => !token.IsTrivia).Select(token => (token.Kind, token.Text))
            .SequenceEqual(other.Tokens.Where(token => !token.IsTrivia).Select(token => (token.Kind, token.Text)));
    public override string ToString() => Text.ToString();
}

public abstract class XamlSyntaxVisitor<TResult>
{
    public virtual TResult Visit(XamlSyntaxNode node) => node.Accept(this);
    public abstract TResult VisitObject(XamlObjectSyntax node);
    public abstract TResult VisitAttribute(XamlAttributeSyntax node);
    public abstract TResult VisitText(XamlTextSyntax node);
}

public abstract class XamlSyntaxWalker : XamlSyntaxVisitor<object?>
{
    public override object? VisitObject(XamlObjectSyntax node)
    {
        foreach (var child in node.ChildNodes()) Visit(child);
        return null;
    }
    public override object? VisitAttribute(XamlAttributeSyntax node) => null;
    public override object? VisitText(XamlTextSyntax node) => null;
}

internal static class XamlStableId
{
    public static ulong Combine(ulong parent, XamlSyntaxKind kind, string namespaceUri, string name, int siblingIndex)
    {
        unchecked
        {
            var hash = parent == 0 ? 14695981039346656037UL : parent;
            hash = Add(hash, ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            hash = Add(hash, namespaceUri);
            hash = Add(hash, name);
            return Add(hash, siblingIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static ulong Add(ulong hash, string value)
    {
        unchecked
        {
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
