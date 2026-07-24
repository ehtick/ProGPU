using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Diagnostics;

namespace ProGPU.Xaml.Parsing;

public sealed class XamlTypeNameParseOptions
{
    public int MaximumDepth { get; set; } = 64;
    public int MaximumTypeCount { get; set; } = 4096;
    public int MaximumDiagnostics { get; set; } = 64;
}

public sealed class XamlTypeNameSyntax
{
    internal XamlTypeNameSyntax(string prefix, string name, ImmutableArray<XamlTypeNameSyntax> typeArguments, TextSpan span)
    {
        Prefix = prefix;
        Name = name;
        TypeArguments = typeArguments;
        Span = span;
    }
    public string Prefix { get; }
    public string Name { get; }
    public string QualifiedName => Prefix.Length == 0 ? Name : Prefix + ":" + Name;
    public ImmutableArray<XamlTypeNameSyntax> TypeArguments { get; }
    public TextSpan Span { get; }
}

public sealed class XamlTypeNameParseResult
{
    internal XamlTypeNameParseResult(ImmutableArray<XamlTypeNameSyntax> types, ImmutableArray<Diagnostic> diagnostics)
    {
        Types = types;
        Diagnostics = diagnostics;
    }
    public ImmutableArray<XamlTypeNameSyntax> Types { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool HasErrors
    {
        get { foreach (var diagnostic in Diagnostics) if (diagnostic.Severity == DiagnosticSeverity.Error) return true; return false; }
    }
}

/// <summary>Linear, span-based parser for x:XamlType and x:TypeArguments text syntax.</summary>
public sealed class XamlTypeNameParser
{
    public XamlTypeNameParseResult Parse(
        SourceText source,
        TextSpan span,
        string path = "",
        XamlTypeNameParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (span.Start < 0 || span.End > source.Length) throw new ArgumentOutOfRangeException(nameof(span));
        options ??= new XamlTypeNameParseOptions();
        var cursor = new Cursor(source, span, path, options, cancellationToken);
        var values = cursor.ParseList(0, terminator: '\0');
        if (values.IsEmpty) cursor.Error("At least one XAML type name is required.", span.Start);
        cursor.SkipWhitespace();
        if (!cursor.AtEnd) cursor.Error("Unexpected text after the type argument list.", cursor.Position);
        return new XamlTypeNameParseResult(values, cursor.Diagnostics.ToImmutable());
    }

    public XamlTypeNameParseResult Parse(
        string text,
        string path = "",
        XamlTypeNameParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var source = SourceText.From(text ?? string.Empty);
        return Parse(source, new TextSpan(0, source.Length), path, options, cancellationToken);
    }

    private sealed class Cursor
    {
        private readonly SourceText _source;
        private readonly TextSpan _span;
        private readonly string _path;
        private readonly XamlTypeNameParseOptions _options;
        private readonly CancellationToken _cancellationToken;
        private int _typeCount;

        public Cursor(SourceText source, TextSpan span, string path, XamlTypeNameParseOptions options, CancellationToken cancellationToken)
        {
            _source = source;
            _span = span;
            _path = path;
            _options = options;
            _cancellationToken = cancellationToken;
            Position = span.Start;
            Diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        }

        public int Position { get; private set; }
        public bool AtEnd => Position >= _span.End;
        public ImmutableArray<Diagnostic>.Builder Diagnostics { get; }

        public ImmutableArray<XamlTypeNameSyntax> ParseList(int depth, char terminator)
        {
            var result = ImmutableArray.CreateBuilder<XamlTypeNameSyntax>();
            SkipWhitespace();
            if (terminator != '\0' && !AtEnd && _source[Position] == terminator)
                Error("A generic type argument list requires at least one type name.", Position);
            while (!AtEnd && (terminator == '\0' || _source[Position] != terminator))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var value = ParseType(depth);
                if (value != null) result.Add(value);
                SkipWhitespace();
                if (!AtEnd && _source[Position] == ',')
                {
                    Position++;
                    SkipWhitespace();
                    if (AtEnd || (terminator != '\0' && _source[Position] == terminator))
                    {
                        Error("A XAML type name is required after ','.", Position);
                        break;
                    }
                    continue;
                }
                if (terminator != '\0' && !AtEnd && _source[Position] == terminator) break;
                if (!AtEnd)
                {
                    Error("Expected ',' or the end of the type argument list.", Position);
                    Recover(terminator);
                    if (!AtEnd && _source[Position] == ',') Position++;
                }
            }
            return result.ToImmutable();
        }

        private XamlTypeNameSyntax? ParseType(int depth)
        {
            if (depth >= _options.MaximumDepth)
            {
                Error($"Type-name nesting exceeds the configured limit of {_options.MaximumDepth}.", Position);
                Recover(')');
                return null;
            }
            if (++_typeCount > _options.MaximumTypeCount)
            {
                Error($"Type-name count exceeds the configured limit of {_options.MaximumTypeCount}.", Position);
                Position = _span.End;
                return null;
            }
            SkipWhitespace();
            var start = Position;
            var first = ReadName();
            if (first.Length == 0)
            {
                Error("A XAML type name is required.", Position);
                if (!AtEnd) Position++;
                return null;
            }
            var prefix = string.Empty;
            var name = first;
            if (!AtEnd && _source[Position] == ':')
            {
                prefix = first;
                Position++;
                name = ReadName();
                if (name.Length == 0) Error("A local type name is required after ':'.", Position);
            }
            SkipWhitespace();
            var arguments = ImmutableArray<XamlTypeNameSyntax>.Empty;
            if (!AtEnd && _source[Position] == '(')
            {
                Position++;
                arguments = ParseList(depth + 1, ')');
                SkipWhitespace();
                if (AtEnd || _source[Position] != ')') Error("A generic type argument list requires ')'.", Position);
                else Position++;
            }
            return new XamlTypeNameSyntax(prefix, name, arguments, TextSpan.FromBounds(start, Position));
        }

        public void SkipWhitespace() { while (!AtEnd && char.IsWhiteSpace(_source[Position])) Position++; }

        private string ReadName()
        {
            var start = Position;
            while (!AtEnd)
            {
                var value = _source[Position];
                if (!(char.IsLetterOrDigit(value) || value == '_' || value == '.' || value == '-' || value == '+')) break;
                Position++;
            }
            return _source.ToString(TextSpan.FromBounds(start, Position));
        }

        private void Recover(char terminator)
        {
            var nested = 0;
            while (!AtEnd)
            {
                var value = _source[Position];
                if (value == '(') nested++;
                else if (value == ')' && nested > 0) nested--;
                else if (nested == 0 && (value == ',' || (terminator != '\0' && value == terminator))) return;
                Position++;
            }
        }

        public void Error(string message, int position)
        {
            if (Diagnostics.Count >= _options.MaximumDiagnostics) return;
            var bounded = Math.Min(Math.Max(position, _span.Start), _span.End);
            Diagnostics.Add(XamlDiagnostics.Create(
                "PGXAML1160",
                DiagnosticSeverity.Error,
                message,
                _path,
                _source,
                new TextSpan(bounded, bounded < _span.End ? 1 : 0),
                "7.4.16"));
        }
    }
}
