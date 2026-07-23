using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

public abstract class XamlMarkupValue
{
    protected XamlMarkupValue(TextSpan span) => Span = span;
    public TextSpan Span { get; }
}

public sealed class XamlMarkupTextValue : XamlMarkupValue
{
    public XamlMarkupTextValue(string text, TextSpan span = default) : base(span) => Text = text ?? string.Empty;
    public string Text { get; }
}

public sealed class XamlMarkupExtensionValue : XamlMarkupValue
{
    public XamlMarkupExtensionValue(XamlMarkupExtension extension) : base(extension.Span) =>
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
    public XamlMarkupExtension Extension { get; }
}

public sealed class XamlMarkupNamedArgument
{
    public XamlMarkupNamedArgument(string name, XamlMarkupValue value, TextSpan span = default)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Span = span;
    }
    public string Name { get; }
    public XamlMarkupValue Value { get; }
    public TextSpan Span { get; }
}

public sealed class XamlMarkupExtension
{
    public XamlMarkupExtension(
        string name,
        IReadOnlyList<XamlMarkupValue> positionalArguments,
        IReadOnlyList<XamlMarkupNamedArgument> namedArguments,
        TextSpan span = default)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        PositionalArguments = positionalArguments ?? throw new ArgumentNullException(nameof(positionalArguments));
        NamedArguments = namedArguments ?? throw new ArgumentNullException(nameof(namedArguments));
        Span = span;
    }
    public string Name { get; }
    public IReadOnlyList<XamlMarkupValue> PositionalArguments { get; }
    public IReadOnlyList<XamlMarkupNamedArgument> NamedArguments { get; }
    public TextSpan Span { get; }
}

public sealed class XamlMarkupParseResult
{
    internal XamlMarkupParseResult(
        XamlMarkupExtension? root,
        bool isMarkupExtension,
        ImmutableArray<XamlMarkupSyntaxToken> tokens,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Root = root;
        IsMarkupExtension = isMarkupExtension;
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public XamlMarkupExtension? Root { get; }
    public bool IsMarkupExtension { get; }
    public ImmutableArray<XamlMarkupSyntaxToken> Tokens { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool HasErrors
    {
        get
        {
            foreach (var diagnostic in Diagnostics)
                if (diagnostic.Severity == DiagnosticSeverity.Error) return true;
            return false;
        }
    }
}

/// <summary>
/// Shared framework-neutral structural parser for markup-extension values. Framework names
/// and argument semantics are intentionally resolved by later profile binders.
/// </summary>
public sealed class XamlMarkupExtensionParser
{
    public XamlMarkupParseResult Parse(
        SourceText source,
        TextSpan span,
        string path = "",
        XamlMarkupParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        options = options ?? new XamlMarkupParseOptions();
        var lexed = XamlMarkupTokenizer.Tokenize(source, span, path, options, cancellationToken);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(lexed.Diagnostics);

        var cursor = new Cursor(source, span, options, cancellationToken);
        cursor.SkipWhitespace();
        if (cursor.AtEnd || cursor.Current != '{' || cursor.StartsWith("{}"))
        {
            return new XamlMarkupParseResult(null, false, lexed.Tokens, diagnostics.ToImmutable());
        }

        XamlMarkupExtension? extension = null;
        try
        {
            extension = ParseExtension(cursor, options, depth: 0);
            cursor.SkipWhitespace();
            if (!cursor.AtEnd) throw new MarkupParseException("Unexpected text after the markup extension.", cursor.Position);
        }
        catch (MarkupParseException exception)
        {
            if (diagnostics.Count < options.MaximumDiagnostics)
            {
                var errorPosition = Math.Min(Math.Max(exception.Position, span.Start), span.End);
                diagnostics.Add(XamlDiagnostics.Create(
                    "PGXAML1101",
                    DiagnosticSeverity.Error,
                    exception.Message,
                    path,
                    source,
                    new TextSpan(errorPosition, errorPosition < span.End ? 1 : 0),
                    "8.6.7.1"));
            }
        }

        return new XamlMarkupParseResult(extension, true, lexed.Tokens, diagnostics.ToImmutable());
    }

    public bool TryParse(
        string text,
        string path,
        SourceText sourceText,
        TextSpan span,
        out XamlMarkupExtension? extension,
        out Diagnostic? diagnostic,
        XamlMarkupParseOptions? options = null)
    {
        text = text ?? string.Empty;
        var localText = SourceText.From(text);
        var result = Parse(
            localText,
            new TextSpan(0, localText.Length),
            options: options);
        extension = result.HasErrors ? null : result.Root;
        diagnostic = null;
        if (result.HasErrors)
        {
            var first = FirstError(result.Diagnostics);
            diagnostic = XamlDiagnostics.Create(
                first?.Id ?? "PGXAML1101",
                DiagnosticSeverity.Error,
                first?.GetMessage() ?? "Invalid markup extension.",
                path,
                sourceText,
                span,
                "8.6.7.1");
        }
        return result.IsMarkupExtension && !result.HasErrors;
    }

    private static XamlMarkupExtension ParseExtension(
        Cursor cursor,
        XamlMarkupParseOptions options,
        int depth)
    {
        if (depth >= options.MaximumDepth)
            throw new MarkupParseException(
                $"Markup nesting exceeds the configured limit of {options.MaximumDepth}.", cursor.Position);

        var start = cursor.Position;
        cursor.Expect('{');
        cursor.SkipWhitespace();
        var nameSpan = cursor.ReadName();
        if (nameSpan.Length == 0)
            throw new MarkupParseException("A markup extension name is required after '{'.", cursor.Position);
        var extensionName = cursor.Source.ToString(nameSpan);

        var positional = new List<XamlMarkupValue>();
        var named = new List<XamlMarkupNamedArgument>();
        cursor.SkipWhitespace();
        var argumentCount = 0;
        while (!cursor.AtEnd && cursor.Current != '}')
        {
            cursor.ThrowIfCancellationRequested();
            if (++argumentCount > options.MaximumArguments)
                throw new MarkupParseException(
                    $"Markup argument count exceeds the configured limit of {options.MaximumArguments}.", cursor.Position);

            if (cursor.Current == ',')
            {
                cursor.Advance();
                cursor.SkipWhitespace();
            }

            var possibleMemberName = cursor.PeekNamedArgumentName();
            var bracketPairs = options.BracketPairResolver?.GetBracketPairs(
                    extensionName,
                    possibleMemberName ?? string.Empty) ??
                options.BracketPairs;
            var segment = cursor.ReadArgumentSegment(bracketPairs);
            if (segment.Length == 0)
                throw new MarkupParseException("Markup extension arguments cannot be empty.", cursor.Position);

            var equals = FindTopLevelEquals(
                cursor.Source,
                segment,
                bracketPairs,
                options.MaximumDepth);
            if (equals > segment.Start)
            {
                var argumentNameSpan = Trim(cursor.Source, TextSpan.FromBounds(segment.Start, equals));
                if (argumentNameSpan.Length == 0)
                    throw new MarkupParseException("A named markup extension argument requires a name.", equals);
                var valueSpan = Trim(cursor.Source, TextSpan.FromBounds(equals + 1, segment.End));
                named.Add(new XamlMarkupNamedArgument(
                    cursor.Source.ToString(argumentNameSpan),
                    ParseValue(cursor.Source, valueSpan, options, depth + 1, cursor.CancellationToken),
                    segment));
            }
            else
            {
                positional.Add(ParseValue(cursor.Source, Trim(cursor.Source, segment), options, depth + 1,
                    cursor.CancellationToken));
            }
            cursor.SkipWhitespace();
        }

        cursor.Expect('}');
        return new XamlMarkupExtension(
            extensionName,
            positional.ToArray(),
            named.ToArray(),
            TextSpan.FromBounds(start, cursor.Position));
    }

    private static XamlMarkupValue ParseValue(
        SourceText source,
        TextSpan span,
        XamlMarkupParseOptions options,
        int depth,
        CancellationToken cancellationToken)
    {
        if (span.Length != 0 && source[span.Start] == '{')
        {
            var nested = new Cursor(source, span, options, cancellationToken);
            var extension = ParseExtension(nested, options, depth);
            nested.SkipWhitespace();
            if (!nested.AtEnd)
                throw new MarkupParseException("Unexpected text after a nested markup extension.", nested.Position);
            return new XamlMarkupExtensionValue(extension);
        }

        var quoted = span.Length >= 2 &&
            ((source[span.Start] == '\'' && source[span.End - 1] == '\'') ||
             (source[span.Start] == '"' && source[span.End - 1] == '"'));
        if (quoted)
        {
            span = new TextSpan(span.Start + 1, span.Length - 2);
        }
        return new XamlMarkupTextValue(quoted ? DecodeEscapes(source, span) : source.ToString(span), span);
    }

    private static string DecodeEscapes(SourceText source, TextSpan span)
    {
        StringBuilder? builder = null;
        var segmentStart = span.Start;
        for (var index = span.Start; index < span.End; index++)
        {
            if (source[index] != '\\' || index + 1 >= span.End) continue;
            builder = builder ?? new StringBuilder(span.Length);
            if (index > segmentStart) builder.Append(source.ToString(TextSpan.FromBounds(segmentStart, index)));
            builder.Append(source[++index]);
            segmentStart = index + 1;
        }
        if (builder == null) return source.ToString(span);
        if (segmentStart < span.End) builder.Append(source.ToString(TextSpan.FromBounds(segmentStart, span.End)));
        return builder.ToString();
    }

    private static int FindTopLevelEquals(
        SourceText source,
        TextSpan span,
        IReadOnlyDictionary<char, char> bracketPairs,
        int maximumDepth)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        var brackets = new BracketTracker(bracketPairs, maximumDepth);
        for (var index = span.Start; index < span.End; index++)
        {
            var current = source[index];
            if (escaped) { escaped = false; continue; }
            if (current == '\\') { escaped = true; continue; }
            if (quote != '\0')
            {
                if (current == quote) quote = '\0';
                continue;
            }
            if (current == '\'' || current == '"') quote = current;
            else if (current == '{') depth++;
            else if (current == '}') depth--;
            else if (depth == 0 && brackets.TryConsume(current, index)) { }
            else if (current == '=' && depth == 0 && brackets.IsEmpty) return index;
        }
        return -1;
    }

    private static TextSpan Trim(SourceText source, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        return TextSpan.FromBounds(start, end);
    }

    private static Diagnostic? FirstError(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            if (diagnostic.Severity == DiagnosticSeverity.Error) return diagnostic;
        return null;
    }

    private sealed class Cursor
    {
        private readonly int _end;
        private int _position;

        private readonly XamlMarkupParseOptions _options;

        public Cursor(
            SourceText source,
            TextSpan span,
            XamlMarkupParseOptions options,
            CancellationToken cancellationToken)
        {
            Source = source;
            _position = span.Start;
            _end = span.End;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            CancellationToken = cancellationToken;
        }

        public SourceText Source { get; }
        public CancellationToken CancellationToken { get; }
        public int Position => _position;
        public bool AtEnd => _position >= _end;
        public char Current => AtEnd ? '\0' : Source[_position];
        public void Advance() => _position++;
        public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();
        public bool StartsWith(string value)
        {
            if (_position + value.Length > _end) return false;
            for (var index = 0; index < value.Length; index++)
                if (Source[_position + index] != value[index]) return false;
            return true;
        }
        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) _position++;
        }
        public void Expect(char value)
        {
            if (AtEnd || Current != value)
                throw new MarkupParseException($"Expected '{value}' in markup extension.", _position);
            _position++;
        }
        public TextSpan ReadName()
        {
            var start = _position;
            while (!AtEnd && !char.IsWhiteSpace(Current) && Current != ',' && Current != '}') _position++;
            return TextSpan.FromBounds(start, _position);
        }
        public string? PeekNamedArgumentName()
        {
            var position = _position;
            while (position < _end && char.IsWhiteSpace(Source[position])) position++;
            var start = position;
            while (position < _end)
            {
                var current = Source[position];
                if (char.IsWhiteSpace(current) ||
                    current == '=' ||
                    current == ',' ||
                    current == '}')
                    break;
                position++;
            }
            if (position == start) return null;
            var end = position;
            while (position < _end && char.IsWhiteSpace(Source[position])) position++;
            return position < _end && Source[position] == '='
                ? Source.ToString(TextSpan.FromBounds(start, end))
                : null;
        }
        public TextSpan ReadArgumentSegment(
            IReadOnlyDictionary<char, char> bracketPairs)
        {
            var start = _position;
            var depth = 0;
            var quote = '\0';
            var escaped = false;
            var brackets = new BracketTracker(
                bracketPairs,
                _options.MaximumDepth);
            while (!AtEnd)
            {
                var current = Current;
                if (escaped) { escaped = false; _position++; continue; }
                if (current == '\\') { escaped = true; _position++; continue; }
                if (quote != '\0')
                {
                    _position++;
                    if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '\'' || current == '"') { quote = current; _position++; }
                else if (current == '{') { depth++; _position++; }
                else if (current == '}')
                {
                    if (depth == 0) break;
                    depth--;
                    _position++;
                }
                else if (depth == 0 && brackets.TryConsume(current, _position))
                    _position++;
                else if (current == ',' && depth == 0 && brackets.IsEmpty) break;
                else _position++;
            }
            if (quote != '\0' || depth != 0 || !brackets.IsEmpty)
                throw new MarkupParseException(
                    "Unterminated quote, bracket pair, or nested markup extension.",
                    _position);
            return Trim(Source, TextSpan.FromBounds(start, _position));
        }

    }

    private struct BracketTracker
    {
        private readonly IReadOnlyDictionary<char, char> _pairs;
        private readonly int _maximumDepth;
        private char[]? _closers;
        private int _count;

        public BracketTracker(
            IReadOnlyDictionary<char, char> pairs,
            int maximumDepth)
        {
            _pairs = pairs;
            _maximumDepth = maximumDepth;
        }

        public bool IsEmpty => _count == 0;

        public bool TryConsume(char value, int position)
        {
            if (_count != 0 && _closers![_count - 1] == value)
            {
                _count--;
                return true;
            }
            if (!_pairs.TryGetValue(value, out var closing)) return false;
            if (_count >= _maximumDepth)
                throw new MarkupParseException(
                    $"Markup bracket nesting exceeds the configured limit of {_maximumDepth}.",
                    position);
            if (_closers == null)
                _closers = new char[Math.Min(8, Math.Max(1, _maximumDepth))];
            else if (_count == _closers.Length)
                Array.Resize(
                    ref _closers,
                    Math.Min(_maximumDepth, checked(_closers.Length * 2)));
            _closers[_count++] = closing;
            return true;
        }
    }

    private sealed class MarkupParseException : Exception
    {
        public MarkupParseException(string message, int position) : base(message) => Position = position;
        public int Position { get; }
    }
}
