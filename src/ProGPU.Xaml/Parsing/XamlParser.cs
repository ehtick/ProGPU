using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

public static class XamlParser
{
    public static XamlSyntaxTree Parse(
        SourceText text,
        string path = "",
        XamlParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        options = XamlParseOptions.Snapshot(options ?? new XamlParseOptions());
        var lexed = new XamlLosslessLexer(text, path, options, cancellationToken).Lex();
        var parser = new StructureParser(text, path, lexed.Tokens, lexed.Diagnostics, options, cancellationToken);
        var tree = parser.Parse();

        if (options.Extensions.Count == 0) return tree;
        var diagnostics = tree.GetDiagnostics().ToBuilder();
        foreach (var extension in options.Extensions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.AddRange(extension.Analyze(tree));
        }
        return new XamlSyntaxTree(text, path, tree.GetRoot()?.Green as GreenXamlElement, tree.Tokens, diagnostics.ToImmutable(), options);
    }

    private sealed class StructureParser
    {
        private readonly SourceText _text;
        private readonly string _path;
        private readonly ImmutableArray<XamlSyntaxToken> _tokens;
        private readonly ImmutableArray<Diagnostic>.Builder _diagnostics;
        private readonly XamlParseOptions _options;
        private readonly CancellationToken _cancellationToken;
        private int _index;
        private int _nodeCount;

        public StructureParser(
            SourceText text,
            string path,
            ImmutableArray<XamlSyntaxToken> tokens,
            ImmutableArray<Diagnostic> lexerDiagnostics,
            XamlParseOptions options,
            CancellationToken cancellationToken)
        {
            _text = text;
            _path = path;
            _tokens = tokens;
            _diagnostics = lexerDiagnostics.ToBuilder();
            _options = options;
            _cancellationToken = cancellationToken;
        }

        public XamlSyntaxTree Parse()
        {
            SkipDocumentTrivia();
            GreenXamlElement? root = null;
            var namespaces = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["xml"] = XamlNamespaces.Xml
            };
            if (Current.Kind == XamlTokenKind.LessThan)
            {
                root = ParseElement(namespaces, 0, 0, 0);
            }
            else
            {
                AddDiagnostic("PGXAML1003", "The XAML document does not contain a root element.", Current.Span, "6.1.1.1");
            }

            SkipDocumentTrivia();
            if (Current.Kind != XamlTokenKind.EndOfFile)
            {
                AddDiagnostic("PGXAML1002", "A XAML document can contain only one root element.", Current.Span, "6.1.1.2");
            }
            if (_options.Mode == XamlParseMode.Strict && HasErrors()) root = null;
            return new XamlSyntaxTree(_text, _path, root, _tokens, _diagnostics.ToImmutable(), _options);
        }

        private GreenXamlElement? ParseElement(
            IReadOnlyDictionary<string, string> inheritedNamespaces,
            int depth,
            ulong parentId,
            int siblingIndex)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!TryReserveNode(Current.Span))
            {
                SkipElement();
                return null;
            }
            if (depth > _options.MaximumDepth)
            {
                AddDiagnostic("PGXAML1004", $"XAML nesting exceeds the configured limit of {_options.MaximumDepth}.", Current.Span, "6.1.1.2");
                SkipElement();
                return null;
            }
            if (Current.Kind != XamlTokenKind.LessThan) return null;
            var start = Consume().Span.Start;
            SkipTagWhitespace();
            var nameToken = Match(XamlTokenKind.Name, "PGXAML1005", "An element name is required after '<'.");
            SplitQualifiedName(nameToken.Text, out var prefix, out var localName);
            var rawAttributes = new List<RawAttribute>();
            var attributeNames = new HashSet<string>(StringComparer.Ordinal);
            var declarations = ImmutableArray.CreateBuilder<XamlNamespaceDeclaration>();
            var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var inheritedNamespace in inheritedNamespaces)
            {
                namespaces.Add(inheritedNamespace.Key, inheritedNamespace.Value);
            }

            while (Current.Kind != XamlTokenKind.GreaterThan &&
                   Current.Kind != XamlTokenKind.SlashGreaterThan &&
                   Current.Kind != XamlTokenKind.EndOfFile)
            {
                SkipTagWhitespace();
                if (Current.Kind == XamlTokenKind.GreaterThan || Current.Kind == XamlTokenKind.SlashGreaterThan) break;
                if (Current.Kind != XamlTokenKind.Name)
                {
                    AddDiagnostic("PGXAML1006", "An attribute name was expected.", Current.Span, "8.6.1");
                    Consume();
                    continue;
                }
                var attributeName = Consume();
                SkipTagWhitespace();
                Match(XamlTokenKind.Equals, "PGXAML1007", "Expected '=' after the attribute name.");
                SkipTagWhitespace();
                var valueToken = Match(XamlTokenKind.StringLiteral, "PGXAML1008", "An XML attribute value must be quoted.");
                var value = DecodeEntities(valueToken.ValueText, valueToken.Span);
                if (value.Length > _options.MaximumAttributeLength)
                {
                    AddDiagnostic("PGXAML1009", $"Attribute value exceeds the configured limit of {_options.MaximumAttributeLength} characters.", valueToken.Span, "8.6.1");
                }
                var span = TextSpan.FromBounds(attributeName.Span.Start, valueToken.Span.End);
                var valueSpan = valueToken.Span.Length >= 2
                    ? new TextSpan(valueToken.Span.Start + 1, valueToken.Span.Length - 2)
                    : valueToken.Span;
                if (!attributeNames.Add(attributeName.Text))
                {
                    AddDiagnostic("PGXAML1018", $"Attribute '{attributeName.Text}' is specified more than once.",
                        attributeName.Span, "8.6.1");
                    continue;
                }
                if (!TryReserveNode(attributeName.Span)) continue;
                if (string.Equals(attributeName.Text, "xmlns", StringComparison.Ordinal))
                {
                    namespaces[string.Empty] = value;
                    declarations.Add(new XamlNamespaceDeclaration(string.Empty, value, span));
                }
                else if (attributeName.Text.StartsWith("xmlns:", StringComparison.Ordinal))
                {
                    var declaredPrefix = attributeName.Text.Substring("xmlns:".Length);
                    namespaces[declaredPrefix] = value;
                    declarations.Add(new XamlNamespaceDeclaration(declaredPrefix, value, span));
                }
                else rawAttributes.Add(new RawAttribute(attributeName.Text, value, span, valueSpan));
            }

            var namespaceUri = ResolveNamespace(namespaces, prefix, nameToken.Span, isAttribute: false);
            var stableId = XamlStableId.Combine(parentId, XamlSyntaxKind.Element, namespaceUri, localName, siblingIndex);
            var attributes = ImmutableArray.CreateBuilder<GreenXamlAttribute>(rawAttributes.Count);
            for (var attributeIndex = 0; attributeIndex < rawAttributes.Count; attributeIndex++)
            {
                var raw = rawAttributes[attributeIndex];
                SplitQualifiedName(raw.Name, out var attributePrefix, out var attributeLocalName);
                var attributeNamespace = ResolveNamespace(namespaces, attributePrefix, raw.Span, isAttribute: true);
                attributes.Add(new GreenXamlAttribute(
                    attributePrefix,
                    attributeNamespace,
                    attributeLocalName,
                    raw.Value,
                    raw.Span,
                    raw.ValueSpan,
                    XamlStableId.Combine(stableId, XamlSyntaxKind.Attribute, attributeNamespace, attributeLocalName, attributeIndex)));
            }

            var children = ImmutableArray.CreateBuilder<GreenXamlNode>();
            int end;
            if (Current.Kind == XamlTokenKind.SlashGreaterThan)
            {
                end = Consume().Span.End;
            }
            else
            {
                Match(XamlTokenKind.GreaterThan, "PGXAML1010", "Expected '>' to close the start tag.");
                while (Current.Kind != XamlTokenKind.LessThanSlash && Current.Kind != XamlTokenKind.EndOfFile)
                {
                    if (Current.Kind == XamlTokenKind.LessThan)
                    {
                        var child = ParseElement(namespaces, depth + 1, stableId, children.Count);
                        if (child != null) children.Add(child);
                    }
                    else if (Current.Kind == XamlTokenKind.Text || Current.Kind == XamlTokenKind.Whitespace || Current.Kind == XamlTokenKind.CData)
                    {
                        var token = Consume();
                        var isCData = token.Kind == XamlTokenKind.CData;
                        var rawText = isCData && token.Text.Length >= 12
                            ? token.Text.Substring(9, token.Text.Length - 12)
                            : token.Text;
                        var value = isCData ? rawText : DecodeEntities(rawText, token.Span);
                        if (!TryReserveNode(token.Span)) continue;
                        children.Add(new GreenXamlText(
                            value,
                            isCData,
                            token.Span,
                            XamlStableId.Combine(stableId, XamlSyntaxKind.Text, string.Empty, string.Empty, children.Count)));
                    }
                    else Consume();
                }

                if (Current.Kind == XamlTokenKind.LessThanSlash)
                {
                    Consume();
                    SkipTagWhitespace();
                    var closeName = Match(XamlTokenKind.Name, "PGXAML1011", "An end-tag name is required.");
                    if (!string.Equals(closeName.Text, nameToken.Text, StringComparison.Ordinal))
                    {
                        AddDiagnostic("PGXAML1012", $"End tag '{closeName.Text}' does not match start tag '{nameToken.Text}'.", closeName.Span, "8.6.1");
                    }
                    SkipTagWhitespace();
                    end = Match(XamlTokenKind.GreaterThan, "PGXAML1013", "Expected '>' to close the end tag.").Span.End;
                }
                else
                {
                    end = Current.Span.Start;
                    AddDiagnostic("PGXAML1014", $"Element '{nameToken.Text}' is missing an end tag.", nameToken.Span, "8.6.1");
                }
            }

            return new GreenXamlElement(
                prefix,
                namespaceUri,
                localName,
                attributes.ToImmutable(),
                children.ToImmutable(),
                declarations.ToImmutable(),
                TextSpan.FromBounds(start, Math.Max(start, end)),
                stableId);
        }

        private string ResolveNamespace(
            IReadOnlyDictionary<string, string> namespaces,
            string prefix,
            TextSpan span,
            bool isAttribute)
        {
            if (prefix.Length == 0 && isAttribute) return string.Empty;
            if (namespaces.TryGetValue(prefix, out var value)) return value;
            if (prefix.Length != 0) AddDiagnostic("PGXAML1015", $"XML namespace prefix '{prefix}' is not declared.", span, "8.6.9");
            return string.Empty;
        }

        private string DecodeEntities(string value, TextSpan span)
        {
            if (value.IndexOf('&') < 0) return value;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if ((index & 0x0fff) == 0)
                    _cancellationToken.ThrowIfCancellationRequested();
                if (value[index] != '&') { builder.Append(value[index]); continue; }
                var semicolon = value.IndexOf(';', index + 1);
                if (semicolon < 0)
                {
                    AddDiagnostic("PGXAML1016", "An XML entity reference is missing ';'.", span, "8.6.1");
                    builder.Append('&');
                    continue;
                }
                var entity = value.Substring(index + 1, semicolon - index - 1);
                switch (entity)
                {
                    case "lt": builder.Append('<'); break;
                    case "gt": builder.Append('>'); break;
                    case "amp": builder.Append('&'); break;
                    case "quot": builder.Append('"'); break;
                    case "apos": builder.Append('\''); break;
                    default:
                        if (TryDecodeNumericEntity(entity, out var decoded)) builder.Append(decoded);
                        else
                        {
                            AddDiagnostic("PGXAML1017", $"Entity '&{entity};' is not a predefined or numeric XML entity.", span, "8.6.1");
                            builder.Append('&').Append(entity).Append(';');
                        }
                        break;
                }
                index = semicolon;
            }
            return builder.ToString();
        }

        private static bool TryDecodeNumericEntity(string entity, out string value)
        {
            value = string.Empty;
            if (!entity.StartsWith("#", StringComparison.Ordinal)) return false;
            var hexadecimal = entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase);
            var digits = entity.Substring(hexadecimal ? 2 : 1);
            if (!int.TryParse(digits, hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var scalar) ||
                scalar < 0 || scalar > 0x10ffff || (scalar >= 0xd800 && scalar <= 0xdfff)) return false;
            value = char.ConvertFromUtf32(scalar);
            return true;
        }

        private XamlSyntaxToken Match(XamlTokenKind kind, string id, string message)
        {
            if (Current.Kind == kind) return Consume();
            AddDiagnostic(id, message, Current.Span, "8.6.1");
            return new XamlSyntaxToken(kind, _text, new TextSpan(Current.Span.Start, 0), isMissing: true);
        }

        private void SkipDocumentTrivia()
        {
            while (Current.IsTrivia || Current.Kind == XamlTokenKind.DocumentType) Consume();
        }
        private void SkipTagWhitespace() { while (Current.Kind == XamlTokenKind.Whitespace) Consume(); }
        private void SkipElement() { while (Current.Kind != XamlTokenKind.EndOfFile && Current.Kind != XamlTokenKind.LessThanSlash) Consume(); }
        private XamlSyntaxToken Consume() { var token = Current; if (_index < _tokens.Length - 1) _index++; return token; }
        private XamlSyntaxToken Current => _tokens[Math.Min(_index, _tokens.Length - 1)];
        private bool HasErrors() { foreach (var diagnostic in _diagnostics) if (diagnostic.Severity == DiagnosticSeverity.Error) return true; return false; }
        private bool TryReserveNode(TextSpan span)
        {
            if (_nodeCount >= _options.MaximumNodes)
            {
                AddDiagnostic("PGXAML1019", $"XAML node count exceeds the configured limit of {_options.MaximumNodes}.",
                    span, "8.6.1");
                return false;
            }
            _nodeCount++;
            return true;
        }
        private void AddDiagnostic(string id, string message, TextSpan span, string section)
        {
            if (_diagnostics.Count >= _options.MaximumDiagnostics) return;
            _diagnostics.Add(XamlDiagnostics.Create(id, DiagnosticSeverity.Error, message, _path, _text, span, section));
        }

        private static void SplitQualifiedName(string name, out string prefix, out string localName)
        {
            var colon = name.IndexOf(':');
            if (colon < 0) { prefix = string.Empty; localName = name; }
            else { prefix = name.Substring(0, colon); localName = name.Substring(colon + 1); }
        }

        private sealed class RawAttribute
        {
            public RawAttribute(string name, string value, TextSpan span, TextSpan valueSpan)
            {
                Name = name;
                Value = value;
                Span = span;
                ValueSpan = valueSpan;
            }
            public string Name { get; }
            public string Value { get; }
            public TextSpan Span { get; }
            public TextSpan ValueSpan { get; }
        }
    }
}

public sealed class XamlXmlParser
{
    public XamlDocumentSyntax Parse(string path, string text) =>
        ParseSyntaxTree(path, SourceText.From(text ?? string.Empty, Encoding.UTF8)).Document;

    public XamlSyntaxTree ParseSyntaxTree(
        string path,
        SourceText text,
        XamlParseOptions? options = null,
        CancellationToken cancellationToken = default) =>
        XamlParser.Parse(text, path, options, cancellationToken);
}
