using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Diagnostics;

namespace ProGPU.Xaml.Syntax;

public enum XamlMarkupTokenKind
{
    None = 0,
    OpenBrace,
    CloseBrace,
    Comma,
    Equals,
    Identifier,
    Text,
    QuotedString,
    Escape,
    Whitespace,
    BadToken,
    EndOfFile,
    FirstCustom = 1024
}

/// <summary>A lossless span-backed token. Text is materialized only when requested.</summary>
public readonly struct XamlMarkupSyntaxToken : IEquatable<XamlMarkupSyntaxToken>
{
    private readonly SourceText _source;

    internal XamlMarkupSyntaxToken(int rawKind, SourceText source, TextSpan span, bool isMissing = false)
    {
        RawKind = rawKind;
        _source = source;
        Span = span;
        IsMissing = isMissing;
    }

    public int RawKind { get; }
    public XamlMarkupTokenKind Kind => (XamlMarkupTokenKind)RawKind;
    public TextSpan Span { get; }
    public TextSpan FullSpan => Span;
    public bool IsMissing { get; }
    public bool IsTrivia => Kind == XamlMarkupTokenKind.Whitespace;
    public bool IsCustom => RawKind >= (int)XamlMarkupTokenKind.FirstCustom;
    public string Text => Span.Length == 0 ? string.Empty : _source.ToString(Span);

    public bool Equals(XamlMarkupSyntaxToken other) =>
        RawKind == other.RawKind && Span == other.Span && IsMissing == other.IsMissing &&
        ReferenceEquals(_source, other._source);
    public override bool Equals(object? obj) => obj is XamlMarkupSyntaxToken other && Equals(other);
    public override int GetHashCode() => unchecked((RawKind * 397) ^ Span.GetHashCode());
    public override string ToString() => Text;
}

public readonly struct XamlMarkupTokenRecognition
{
    public XamlMarkupTokenRecognition(int rawKind, int length)
    {
        if (rawKind < (int)XamlMarkupTokenKind.FirstCustom)
            throw new ArgumentOutOfRangeException(nameof(rawKind), "Custom token kinds must be at least FirstCustom.");
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        RawKind = rawKind;
        Length = length;
    }

    public int RawKind { get; }
    public int Length { get; }
}

/// <summary>
/// Trigger-indexed lexical extension. Recognizers are invoked only when the current character
/// appears in <see cref="TriggerCharacters"/>, never once per source character.
/// </summary>
public interface IXamlMarkupTokenRecognizer
{
    string Id { get; }
    int Version { get; }
    int Priority { get; }
    IReadOnlyList<char> TriggerCharacters { get; }
    bool TryRecognize(SourceText source, TextSpan remaining, out XamlMarkupTokenRecognition recognition);
}

public interface IXamlMarkupBracketPairResolver
{
    IReadOnlyDictionary<char, char> GetBracketPairs(
        string extensionName,
        string memberName);
}

public sealed class XamlMarkupParseOptions
{
    public int MaximumDepth { get; set; } = 128;
    public int MaximumTokens { get; set; } = 16 * 1024;
    public int MaximumTokenLength { get; set; } = 1024 * 1024;
    public int MaximumArguments { get; set; } = 4096;
    public int MaximumDiagnostics { get; set; } = 128;
    /// <summary>
    /// Additional opening-to-closing delimiter pairs whose contents may contain top-level
    /// markup commas or equals signs. The common path remains empty and allocation-free.
    /// </summary>
    public IReadOnlyDictionary<char, char> BracketPairs { get; set; } =
        new Dictionary<char, char>();
    public IXamlMarkupBracketPairResolver? BracketPairResolver { get; set; }
    public IReadOnlyList<IXamlMarkupTokenRecognizer> TokenRecognizers { get; set; } =
        Array.Empty<IXamlMarkupTokenRecognizer>();
}

public sealed class XamlMarkupLexResult
{
    internal XamlMarkupLexResult(
        ImmutableArray<XamlMarkupSyntaxToken> tokens,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<XamlMarkupSyntaxToken> Tokens { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

/// <summary>Framework-neutral, linear-time lexer for XAML attribute markup languages.</summary>
public static class XamlMarkupTokenizer
{
    public static XamlMarkupLexResult Tokenize(
        SourceText source,
        TextSpan span,
        string path = "",
        XamlMarkupParseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (span.Start < 0 || span.End > source.Length) throw new ArgumentOutOfRangeException(nameof(span));
        options = options ?? new XamlMarkupParseOptions();

        var tokens = ImmutableArray.CreateBuilder<XamlMarkupSyntaxToken>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var recognizers = CreateRecognizerMap(options.TokenRecognizers);
        var position = span.Start;
        while (position < span.End)
        {
            if (((position - span.Start) & 0x0fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (tokens.Count >= options.MaximumTokens)
            {
                AddDiagnostic(diagnostics, options, source, path, "PGXAML1150",
                    $"Markup token count exceeds the configured limit of {options.MaximumTokens}.",
                    new TextSpan(position, span.End - position));
                break;
            }

            var current = source[position];
            if (recognizers.TryGetValue(current, out var candidates))
            {
                var matched = false;
                foreach (var recognizer in candidates)
                {
                    if (!recognizer.TryRecognize(source, TextSpan.FromBounds(position, span.End), out var recognition))
                        continue;
                    if (recognition.Length > span.End - position || recognition.Length > options.MaximumTokenLength)
                    {
                        AddDiagnostic(diagnostics, options, source, path, "PGXAML1151",
                            $"Custom markup token from '{recognizer.Id}' exceeds its source span or configured token limit.",
                            new TextSpan(position, Math.Min(span.End - position, Math.Max(1, recognition.Length))));
                        break;
                    }
                    tokens.Add(new XamlMarkupSyntaxToken(recognition.RawKind, source,
                        new TextSpan(position, recognition.Length)));
                    position += recognition.Length;
                    matched = true;
                    break;
                }
                if (matched) continue;
            }

            switch (current)
            {
                case '{': Add(XamlMarkupTokenKind.OpenBrace, 1); break;
                case '}': Add(XamlMarkupTokenKind.CloseBrace, 1); break;
                case ',': Add(XamlMarkupTokenKind.Comma, 1); break;
                case '=': Add(XamlMarkupTokenKind.Equals, 1); break;
                case '\\':
                    Add(XamlMarkupTokenKind.Escape, position + 1 < span.End ? 2 : 1);
                    break;
                case '\'':
                case '"':
                    LexQuoted(current);
                    break;
                default:
                    if (char.IsWhiteSpace(current)) LexWhitespace();
                    else LexText();
                    break;
            }
        }

        tokens.Add(new XamlMarkupSyntaxToken((int)XamlMarkupTokenKind.EndOfFile, source,
            new TextSpan(Math.Min(position, span.End), 0)));
        return new XamlMarkupLexResult(tokens.ToImmutable(), diagnostics.ToImmutable());

        void Add(XamlMarkupTokenKind kind, int length)
        {
            tokens.Add(new XamlMarkupSyntaxToken((int)kind, source, new TextSpan(position, length)));
            position += length;
        }

        void LexWhitespace()
        {
            var start = position++;
            while (position < span.End && char.IsWhiteSpace(source[position])) position++;
            AddMeasured(XamlMarkupTokenKind.Whitespace, start);
        }

        void LexQuoted(char quote)
        {
            var start = position++;
            var escaped = false;
            while (position < span.End)
            {
                var value = source[position++];
                if (escaped) { escaped = false; continue; }
                if (value == '\\') { escaped = true; continue; }
                if (value == quote) { AddMeasured(XamlMarkupTokenKind.QuotedString, start); return; }
            }
            AddMeasured(XamlMarkupTokenKind.QuotedString, start);
            AddDiagnostic(diagnostics, options, source, path, "PGXAML1152",
                "Unterminated quoted markup value.", new TextSpan(start, position - start));
        }

        void LexText()
        {
            var start = position++;
            while (position < span.End)
            {
                var value = source[position];
                if (char.IsWhiteSpace(value) || value == '{' || value == '}' || value == ',' ||
                    value == '=' || value == '\'' || value == '"' || value == '\\') break;
                position++;
            }
            var kind = IsIdentifier(source, TextSpan.FromBounds(start, position))
                ? XamlMarkupTokenKind.Identifier
                : XamlMarkupTokenKind.Text;
            AddMeasured(kind, start);
        }

        void AddMeasured(XamlMarkupTokenKind kind, int start)
        {
            var length = position - start;
            if (length > options.MaximumTokenLength)
            {
                AddDiagnostic(diagnostics, options, source, path, "PGXAML1153",
                    $"Markup token length exceeds the configured limit of {options.MaximumTokenLength}.",
                    new TextSpan(start, length));
            }
            tokens.Add(new XamlMarkupSyntaxToken((int)kind, source, new TextSpan(start, length)));
        }
    }

    private static Dictionary<char, List<IXamlMarkupTokenRecognizer>> CreateRecognizerMap(
        IReadOnlyList<IXamlMarkupTokenRecognizer> registrations)
    {
        var result = new Dictionary<char, List<IXamlMarkupTokenRecognizer>>();
        foreach (var registration in registrations)
        {
            foreach (var trigger in registration.TriggerCharacters)
            {
                if (!result.TryGetValue(trigger, out var list))
                {
                    list = new List<IXamlMarkupTokenRecognizer>();
                    result.Add(trigger, list);
                }
                list.Add(registration);
            }
        }
        foreach (var pair in result)
        {
            pair.Value.Sort((left, right) =>
            {
                var priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : StringComparer.Ordinal.Compare(left.Id, right.Id);
            });
        }
        return result;
    }

    private static bool IsIdentifier(SourceText source, TextSpan span)
    {
        if (span.Length == 0) return false;
        for (var index = span.Start; index < span.End; index++)
        {
            var value = source[index];
            if (!(char.IsLetterOrDigit(value) || value == '_' || value == ':' || value == '.' || value == '-'))
                return false;
        }
        return true;
    }

    private static void AddDiagnostic(
        ImmutableArray<Diagnostic>.Builder diagnostics,
        XamlMarkupParseOptions options,
        SourceText source,
        string path,
        string id,
        string message,
        TextSpan span)
    {
        if (diagnostics.Count >= options.MaximumDiagnostics) return;
        diagnostics.Add(XamlDiagnostics.Create(id, DiagnosticSeverity.Error, message, path, source, span, "8.6.7.1"));
    }
}
