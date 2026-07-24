using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

internal sealed class XamlLosslessLexer
{
    private readonly SourceText _source;
    private readonly string _path;
    private readonly XamlParseOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly List<XamlSyntaxToken> _tokens = new List<XamlSyntaxToken>();
    private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();
    private int _position;
    private bool _insideTag;

    public XamlLosslessLexer(SourceText source, string path, XamlParseOptions options, CancellationToken cancellationToken)
    {
        _source = source;
        _path = path;
        _options = options;
        _cancellationToken = cancellationToken;
    }

    public XamlLexResult Lex()
    {
        while (_position < _source.Length)
        {
            if ((_position & 0x3fff) == 0) _cancellationToken.ThrowIfCancellationRequested();
            if (_tokens.Count >= _options.MaximumTokens)
            {
                AddDiagnostic("PGXAML1207", $"XML token count exceeds the configured limit of {_options.MaximumTokens}.",
                    new TextSpan(_position, _source.Length - _position), "8.6.1");
                break;
            }
            if (_insideTag) LexTag(); else LexContent();
        }
        _tokens.Add(new XamlSyntaxToken(XamlTokenKind.EndOfFile, _source, new TextSpan(_position, 0)));
        return new XamlLexResult(_tokens.ToImmutableArray(), _diagnostics.ToImmutableArray());
    }

    private void LexContent()
    {
        if (StartsWith("<!--")) { LexDelimited(XamlTokenKind.Comment, "<!--", "-->", "PGXAML1201", "Unterminated XML comment."); return; }
        if (StartsWith("<![CDATA[")) { LexDelimited(XamlTokenKind.CData, "<![CDATA[", "]]>", "PGXAML1202", "Unterminated CDATA section."); return; }
        if (StartsWith("<?")) { LexDelimited(XamlTokenKind.ProcessingInstruction, "<?", "?>", "PGXAML1203", "Unterminated processing instruction."); return; }
        if (StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            var start = _position;
            ConsumeDeclaration();
            Add(XamlTokenKind.DocumentType, start, _position - start);
            AddDiagnostic("PGXAML1204", "DTD declarations are prohibited in XAML compiler inputs.", new TextSpan(start, _position - start), "8.6.1");
            return;
        }
        if (StartsWith("</")) { AddAndAdvance(XamlTokenKind.LessThanSlash, 2); _insideTag = true; return; }
        if (_source[_position] == '<') { AddAndAdvance(XamlTokenKind.LessThan, 1); _insideTag = true; return; }

        var startText = _position;
        var isWhitespace = true;
        while (_position < _source.Length && _source[_position] != '<')
        {
            PollCancellation();
            if (!char.IsWhiteSpace(_source[_position])) isWhitespace = false;
            _position++;
        }
        Add(isWhitespace ? XamlTokenKind.Whitespace : XamlTokenKind.Text, startText, _position - startText);
    }

    private void LexTag()
    {
        if (StartsWith("/>")) { AddAndAdvance(XamlTokenKind.SlashGreaterThan, 2); _insideTag = false; return; }
        var current = _source[_position];
        if (current == '>') { AddAndAdvance(XamlTokenKind.GreaterThan, 1); _insideTag = false; }
        else if (current == '=') AddAndAdvance(XamlTokenKind.Equals, 1);
        else if (char.IsWhiteSpace(current))
        {
            var start = _position++;
            while (_position < _source.Length && char.IsWhiteSpace(_source[_position]))
            {
                PollCancellation();
                _position++;
            }
            Add(XamlTokenKind.Whitespace, start, _position - start);
        }
        else if (current == '\'' || current == '"') LexQuotedString(current);
        else if (IsNameStart(current))
        {
            var start = _position++;
            while (_position < _source.Length && IsNameCharacter(_source[_position]))
            {
                PollCancellation();
                _position++;
            }
            Add(XamlTokenKind.Name, start, _position - start);
        }
        else
        {
            var start = _position++;
            Add(XamlTokenKind.BadToken, start, 1);
            AddDiagnostic("PGXAML1205", $"Unexpected character '{current}' in an XML tag.", new TextSpan(start, 1), "8.6.1");
        }
    }

    private void LexQuotedString(char quote)
    {
        var start = _position++;
        while (_position < _source.Length && _source[_position] != quote)
        {
            PollCancellation();
            _position++;
        }
        if (_position < _source.Length) _position++;
        else AddDiagnostic("PGXAML1206", "Unterminated XML attribute value.", new TextSpan(start, _position - start), "8.6.1");
        Add(XamlTokenKind.StringLiteral, start, _position - start);
    }

    private void LexDelimited(XamlTokenKind kind, string startMarker, string endMarker, string id, string message)
    {
        var start = _position;
        _position += startMarker.Length;
        var end = IndexOf(endMarker, _position);
        if (end < 0)
        {
            _position = _source.Length;
            AddDiagnostic(id, message, new TextSpan(start, _position - start), "8.6.1");
        }
        else _position = end + endMarker.Length;
        Add(kind, start, _position - start);
    }

    private void ConsumeDeclaration()
    {
        var brackets = 0;
        var quote = '\0';
        while (_position < _source.Length)
        {
            PollCancellation();
            var current = _source[_position++];
            if (quote != '\0') { if (current == quote) quote = '\0'; }
            else if (current == '\'' || current == '"') quote = current;
            else if (current == '[') brackets++;
            else if (current == ']') brackets--;
            else if (current == '>' && brackets <= 0) return;
        }
    }

    private int IndexOf(string value, int start)
    {
        for (var index = start; index <= _source.Length - value.Length; index++)
        {
            if ((index & 0x3fff) == 0)
                _cancellationToken.ThrowIfCancellationRequested();
            var match = true;
            for (var offset = 0; offset < value.Length; offset++)
            {
                if (_source[index + offset] != value[offset]) { match = false; break; }
            }
            if (match) return index;
        }
        return -1;
    }

    private bool StartsWith(string value, StringComparison comparison = StringComparison.Ordinal)
    {
        if (_position + value.Length > _source.Length) return false;
        for (var index = 0; index < value.Length; index++)
        {
            var sourceCharacter = _source[_position + index];
            var valueCharacter = value[index];
            if (comparison == StringComparison.OrdinalIgnoreCase)
            {
                sourceCharacter = char.ToUpperInvariant(sourceCharacter);
                valueCharacter = char.ToUpperInvariant(valueCharacter);
            }
            if (sourceCharacter != valueCharacter) return false;
        }
        return true;
    }

    private void AddAndAdvance(XamlTokenKind kind, int length) { Add(kind, _position, length); _position += length; }
    private void PollCancellation()
    {
        if ((_position & 0x3fff) == 0)
            _cancellationToken.ThrowIfCancellationRequested();
    }
    private void Add(XamlTokenKind kind, int start, int length)
    {
        if (length > _options.MaximumTokenLength)
        {
            AddDiagnostic("PGXAML1208", $"XML token length exceeds the configured limit of {_options.MaximumTokenLength}.",
                new TextSpan(start, length), "8.6.1");
        }
        _tokens.Add(new XamlSyntaxToken(kind, _source, new TextSpan(start, length)));
    }
    private void AddDiagnostic(string id, string message, TextSpan span, string section)
    {
        if (_diagnostics.Count >= _options.MaximumDiagnostics) return;
        _diagnostics.Add(XamlDiagnostics.Create(id, DiagnosticSeverity.Error, message, _path, _source, span, section));
    }
    private static bool IsNameStart(char value)
    {
        if (value == '_' || value == ':') return true;
        switch (char.GetUnicodeCategory(value))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.LetterNumber:
                return true;
            default:
                return false;
        }
    }
    private static bool IsNameCharacter(char value)
    {
        if (IsNameStart(value) || value == '-' || value == '.' || value == '\u00b7') return true;
        switch (char.GetUnicodeCategory(value))
        {
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.ConnectorPunctuation:
                return true;
            default:
                return false;
        }
    }
}

internal sealed class XamlLexResult
{
    public XamlLexResult(ImmutableArray<XamlSyntaxToken> tokens, ImmutableArray<Diagnostic> diagnostics)
    {
        Tokens = tokens;
        Diagnostics = diagnostics;
    }
    public ImmutableArray<XamlSyntaxToken> Tokens { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
