using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Parsing;

public enum XamlBindingPathTokenKind
{
    Identifier,
    Dot,
    Colon,
    OpenParenthesis,
    CloseParenthesis,
    OpenBracket,
    CloseBracket,
    Comma,
    IntegerLiteral,
    NumericLiteral,
    StringLiteral,
    Whitespace,
    EndOfInput,
    Bad
}

/// <summary>
/// Lossless token in the shared binding-expression grammar. Offsets are relative to the
/// binding path value so callers can map them back to the containing XAML syntax.
/// </summary>
public readonly struct XamlBindingPathToken
{
    public XamlBindingPathToken(XamlBindingPathTokenKind kind, string text, TextSpan span)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = span;
    }

    public XamlBindingPathTokenKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
}

public enum XamlBindingPathStepKind
{
    Member,
    IntegerIndexer,
    StringIndexer,
    Cast,
    QualifiedMember,
    FunctionCall
}

public enum XamlBindingFunctionArgumentKind
{
    BindingPath,
    StringLiteral,
    NumericLiteral,
    BooleanLiteral
}

/// <summary>
/// One lossless, typed function-binding argument. Binding-path text is parsed again by the
/// shared parser during semantic binding so every framework uses the same grammar.
/// </summary>
public sealed class XamlBindingFunctionArgumentSyntax
{
    internal XamlBindingFunctionArgumentSyntax(
        XamlBindingFunctionArgumentKind kind,
        string text,
        TextSpan span,
        string? stringValue = null,
        bool booleanValue = false,
        string? namespacePrefix = null)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Span = span;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        NamespacePrefix = namespacePrefix;
    }

    public XamlBindingFunctionArgumentKind Kind { get; }
    public string Text { get; }
    public TextSpan Span { get; }
    public string? StringValue { get; }
    public bool BooleanValue { get; }
    public string? NamespacePrefix { get; }
}

/// <summary>
/// One immutable executable step in a compiled-binding path. Tokens remain the source of
/// lossless text; the step supplies the normalized value required by semantic binding.
/// </summary>
public sealed class XamlBindingPathStepSyntax
{
    internal XamlBindingPathStepSyntax(
        XamlBindingPathStepKind kind,
        XamlBindingPathToken valueToken,
        TextSpan span,
        int integerValue = 0,
        string? stringValue = null,
        string? typeName = null,
        string? memberName = null,
        ImmutableArray<XamlBindingFunctionArgumentSyntax> arguments = default,
        bool isStaticFunction = false)
    {
        Kind = kind;
        ValueToken = valueToken;
        Span = span;
        IntegerValue = integerValue;
        StringValue = stringValue;
        TypeName = typeName;
        MemberName = memberName;
        Arguments = arguments.IsDefault
            ? ImmutableArray<XamlBindingFunctionArgumentSyntax>.Empty
            : arguments;
        IsStaticFunction = isStaticFunction;
    }

    public XamlBindingPathStepKind Kind { get; }
    public XamlBindingPathToken ValueToken { get; }
    public TextSpan Span { get; }
    public int IntegerValue { get; }
    public string? StringValue { get; }
    public string? TypeName { get; }
    public string? MemberName { get; }
    public ImmutableArray<XamlBindingFunctionArgumentSyntax> Arguments { get; }
    public bool IsStaticFunction { get; }
}

public sealed class XamlBindingPathSyntax
{
    internal XamlBindingPathSyntax(
        string text,
        ImmutableArray<XamlBindingPathToken> tokens,
        ImmutableArray<XamlBindingPathToken> segments,
        ImmutableArray<XamlBindingPathStepSyntax> steps,
        ImmutableArray<TextSpan> errorSpans)
    {
        Text = text;
        Tokens = tokens;
        Segments = segments;
        Steps = steps;
        ErrorSpans = errorSpans;
    }

    public string Text { get; }
    public ImmutableArray<XamlBindingPathToken> Tokens { get; }

    /// <summary>
    /// Compatibility projection containing only identifier/member tokens. New semantic
    /// consumers should use <see cref="Steps"/>.
    /// </summary>
    public ImmutableArray<XamlBindingPathToken> Segments { get; }

    public ImmutableArray<XamlBindingPathStepSyntax> Steps { get; }
    public ImmutableArray<TextSpan> ErrorSpans { get; }
    public bool HasErrors => !ErrorSpans.IsEmpty;
}

/// <summary>
/// Allocation-bounded parser for the common compiled-binding expression language. The lexer is
/// lossless and framework-neutral; semantic consumers receive immutable member and constant
/// indexer steps. Casts, attached members, and function calls share this syntax family rather
/// than introducing framework parsers.
/// </summary>
public sealed class XamlBindingPathParser
{
    public XamlBindingPathSyntax Parse(string? text)
    {
        text ??= string.Empty;
        var tokens = ImmutableArray.CreateBuilder<XamlBindingPathToken>();
        var lexicalErrors = ImmutableArray.CreateBuilder<TextSpan>();
        Lex(text, tokens, lexicalErrors);

        var segments = ImmutableArray.CreateBuilder<XamlBindingPathToken>();
        var steps = ImmutableArray.CreateBuilder<XamlBindingPathStepSyntax>();
        var errors = ImmutableArray.CreateBuilder<TextSpan>();
        errors.AddRange(lexicalErrors);
        new StepParser(tokens, text, segments, steps, errors).Parse();
        tokens.Add(new XamlBindingPathToken(
            XamlBindingPathTokenKind.EndOfInput,
            string.Empty,
            new TextSpan(text.Length, 0)));
        return new XamlBindingPathSyntax(
            text,
            tokens.ToImmutable(),
            segments.ToImmutable(),
            steps.ToImmutable(),
            errors.ToImmutable());
    }

    private static void Lex(
        string text,
        ImmutableArray<XamlBindingPathToken>.Builder tokens,
        ImmutableArray<TextSpan>.Builder errors)
    {
        var position = 0;
        while (position < text.Length)
        {
            var start = position;
            var current = text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                AddToken(tokens, XamlBindingPathTokenKind.Whitespace, text, start, position);
                continue;
            }

            switch (current)
            {
                case '.':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.Dot, text, start, position);
                    continue;
                case ':':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.Colon, text, start, position);
                    continue;
                case '(':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.OpenParenthesis, text, start, position);
                    continue;
                case ')':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.CloseParenthesis, text, start, position);
                    continue;
                case '[':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.OpenBracket, text, start, position);
                    continue;
                case ']':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.CloseBracket, text, start, position);
                    continue;
                case ',':
                    position++;
                    AddToken(tokens, XamlBindingPathTokenKind.Comma, text, start, position);
                    continue;
                case '\'':
                case '"':
                {
                    var quote = current;
                    position++;
                    var terminated = false;
                    while (position < text.Length)
                    {
                        if (text[position] == '^' && position + 1 < text.Length)
                        {
                            position += 2;
                            continue;
                        }
                        if (text[position] == quote)
                        {
                            position++;
                            terminated = true;
                            break;
                        }
                        position++;
                    }
                    AddToken(tokens, XamlBindingPathTokenKind.StringLiteral, text, start, position);
                    if (!terminated)
                        errors.Add(TextSpan.FromBounds(start, position));
                    continue;
                }
            }

            if (char.IsDigit(current) ||
                (current == '-' &&
                 position + 1 < text.Length &&
                 char.IsDigit(text[position + 1])))
            {
                var numericKind = XamlBindingPathTokenKind.IntegerLiteral;
                position++;
                while (position < text.Length && char.IsDigit(text[position])) position++;
                if (position < text.Length &&
                    text[position] == '.' &&
                    position + 1 < text.Length &&
                    char.IsDigit(text[position + 1]))
                {
                    numericKind = XamlBindingPathTokenKind.NumericLiteral;
                    position++;
                    while (position < text.Length && char.IsDigit(text[position])) position++;
                }
                if (position < text.Length &&
                    (text[position] == 'e' || text[position] == 'E'))
                {
                    numericKind = XamlBindingPathTokenKind.NumericLiteral;
                    position++;
                    if (position < text.Length &&
                        (text[position] == '+' || text[position] == '-'))
                        position++;
                    var exponentStart = position;
                    while (position < text.Length && char.IsDigit(text[position])) position++;
                    if (position == exponentStart)
                        errors.Add(TextSpan.FromBounds(start, position));
                }
                if (text[start] == '-')
                    numericKind = XamlBindingPathTokenKind.NumericLiteral;
                AddToken(tokens, numericKind, text, start, position);
                continue;
            }
            if (IsIdentifierStart(current))
            {
                position++;
                while (position < text.Length && IsIdentifierPart(text[position])) position++;
                AddToken(tokens, XamlBindingPathTokenKind.Identifier, text, start, position);
                continue;
            }

            position++;
            while (position < text.Length &&
                   !char.IsWhiteSpace(text[position]) &&
                   text[position] != '.' &&
                   text[position] != ':' &&
                   text[position] != '(' &&
                   text[position] != ')' &&
                   text[position] != '[' &&
                   text[position] != ']' &&
                   text[position] != ',' &&
                   text[position] != '\'' &&
                   text[position] != '"' &&
                   !char.IsDigit(text[position]) &&
                   !IsIdentifierStart(text[position]))
                position++;
            AddToken(tokens, XamlBindingPathTokenKind.Bad, text, start, position);
            errors.Add(TextSpan.FromBounds(start, position));
        }
    }

    private static void AddToken(
        ImmutableArray<XamlBindingPathToken>.Builder tokens,
        XamlBindingPathTokenKind kind,
        string text,
        int start,
        int end) =>
        tokens.Add(new XamlBindingPathToken(
            kind,
            text.Substring(start, end - start),
            TextSpan.FromBounds(start, end)));

    private sealed class StepParser
    {
        private readonly ImmutableArray<XamlBindingPathToken>.Builder _tokens;
        private readonly int _textLength;
        private readonly ImmutableArray<XamlBindingPathToken>.Builder _segments;
        private readonly ImmutableArray<XamlBindingPathStepSyntax>.Builder _steps;
        private readonly ImmutableArray<TextSpan>.Builder _errors;
        private readonly string _text;
        private int _index;

        public StepParser(
            ImmutableArray<XamlBindingPathToken>.Builder tokens,
            string text,
            ImmutableArray<XamlBindingPathToken>.Builder segments,
            ImmutableArray<XamlBindingPathStepSyntax>.Builder steps,
            ImmutableArray<TextSpan>.Builder errors)
        {
            _tokens = tokens;
            _textLength = text.Length;
            _segments = segments;
            _steps = steps;
            _errors = errors;
            _text = text;
        }

        public void Parse()
        {
            if (!ParseExpression())
            {
                if (_steps.Count == 0)
                    _errors.Add(new TextSpan(0, _textLength));
                return;
            }
            while (TryRead(out var trailing))
                _errors.Add(trailing.Span);
        }

        private bool ParseExpression()
        {
            if (!ParsePrimary()) return false;
            while (TryPeek(out var token))
            {
                if (token.Kind == XamlBindingPathTokenKind.OpenBracket)
                {
                    Read();
                    ParseIndexer(token);
                    continue;
                }
                if (token.Kind == XamlBindingPathTokenKind.OpenParenthesis)
                {
                    Read();
                    ParseFunctionCall(token);
                    continue;
                }
                if (token.Kind != XamlBindingPathTokenKind.Dot) break;
                Read();
                if (!TryRead(out var afterDot))
                {
                    _errors.Add(new TextSpan(_textLength, 0));
                    return true;
                }
                if (afterDot.Kind == XamlBindingPathTokenKind.Identifier)
                {
                    AddMember(afterDot);
                    continue;
                }
                if (afterDot.Kind ==
                    XamlBindingPathTokenKind.OpenParenthesis)
                {
                    ParseQualifiedMember(afterDot);
                    continue;
                }
                _errors.Add(afterDot.Span);
                return true;
            }
            return true;
        }

        private bool ParsePrimary()
        {
            if (!TryRead(out var token)) return false;
            if (token.Kind == XamlBindingPathTokenKind.Identifier)
            {
                if (TryParseStaticFunction(token))
                    return true;
                AddMember(token);
                return true;
            }
            if (token.Kind == XamlBindingPathTokenKind.OpenBracket)
            {
                ParseIndexer(token);
                return true;
            }
            if (token.Kind != XamlBindingPathTokenKind.OpenParenthesis)
            {
                _errors.Add(token.Span);
                return false;
            }

            var saved = _index;
            if (TryParseCastHeader(token, out var cast))
            {
                if (TryPeek(out var operand) && CanStartPrimary(operand.Kind))
                {
                    if (!ParseExpression())
                        _errors.Add(operand.Span);
                }
                _steps.Add(cast!);
                return true;
            }

            _index = saved;
            if (!ParseExpression())
            {
                _errors.Add(token.Span);
                return true;
            }
            if (!TryRead(out var close) ||
                close.Kind != XamlBindingPathTokenKind.CloseParenthesis)
            {
                _errors.Add(close.Span);
                return true;
            }
            return true;
        }

        private bool TryParseStaticFunction(XamlBindingPathToken prefix)
        {
            var saved = _index;
            if (!TryRead(out var colon) ||
                colon.Kind != XamlBindingPathTokenKind.Colon ||
                !TryRead(out var type) ||
                type.Kind != XamlBindingPathTokenKind.Identifier ||
                !TryRead(out var dot) ||
                dot.Kind != XamlBindingPathTokenKind.Dot ||
                !TryRead(out var method) ||
                method.Kind != XamlBindingPathTokenKind.Identifier ||
                !TryRead(out var open) ||
                open.Kind != XamlBindingPathTokenKind.OpenParenthesis)
            {
                _index = saved;
                return false;
            }
            ParseFunctionCall(
                open,
                method,
                prefix.Text + ":" + type.Text,
                isStatic: true);
            return true;
        }

        private void ParseFunctionCall(
            XamlBindingPathToken open,
            XamlBindingPathToken method = default,
            string? typeName = null,
            bool isStatic = false)
        {
            if (!isStatic)
            {
                if (_steps.Count == 0 ||
                    _steps[_steps.Count - 1].Kind != XamlBindingPathStepKind.Member)
                {
                    _errors.Add(open.Span);
                    SkipFunction(open);
                    return;
                }
                var methodStep = _steps[_steps.Count - 1];
                method = methodStep.ValueToken;
                _steps.RemoveAt(_steps.Count - 1);
                if (_segments.Count != 0)
                    _segments.RemoveAt(_segments.Count - 1);
            }

            var arguments = ImmutableArray.CreateBuilder<XamlBindingFunctionArgumentSyntax>();
            var argumentStart = _index;
            var parenthesisDepth = 0;
            var bracketDepth = 0;
            while (_index < _tokens.Count)
            {
                var token = _tokens[_index];
                if (token.Kind == XamlBindingPathTokenKind.OpenParenthesis)
                {
                    parenthesisDepth++;
                    _index++;
                    continue;
                }
                if (token.Kind == XamlBindingPathTokenKind.CloseParenthesis)
                {
                    if (parenthesisDepth != 0)
                    {
                        parenthesisDepth--;
                        _index++;
                        continue;
                    }
                    if (!AddFunctionArgument(
                            arguments,
                            argumentStart,
                            _index,
                            allowEmpty: arguments.Count == 0))
                        _errors.Add(token.Span);
                    _index++;
                    _steps.Add(new XamlBindingPathStepSyntax(
                        XamlBindingPathStepKind.FunctionCall,
                        method,
                        TextSpan.FromBounds(method.Span.Start, token.Span.End),
                        typeName: typeName,
                        memberName: method.Text,
                        arguments: arguments.ToImmutable(),
                        isStaticFunction: isStatic));
                    return;
                }
                if (token.Kind == XamlBindingPathTokenKind.OpenBracket)
                    bracketDepth++;
                else if (token.Kind == XamlBindingPathTokenKind.CloseBracket)
                    bracketDepth--;
                else if (token.Kind == XamlBindingPathTokenKind.Comma &&
                         parenthesisDepth == 0 &&
                         bracketDepth == 0)
                {
                    if (!AddFunctionArgument(
                            arguments,
                            argumentStart,
                            _index,
                            allowEmpty: false))
                        _errors.Add(token.Span);
                    _index++;
                    argumentStart = _index;
                    continue;
                }
                _index++;
            }
            _errors.Add(new TextSpan(open.Span.End, 0));
        }

        private bool AddFunctionArgument(
            ImmutableArray<XamlBindingFunctionArgumentSyntax>.Builder arguments,
            int startIndex,
            int endIndex,
            bool allowEmpty)
        {
            while (startIndex < endIndex &&
                   _tokens[startIndex].Kind == XamlBindingPathTokenKind.Whitespace)
                startIndex++;
            while (endIndex > startIndex &&
                   _tokens[endIndex - 1].Kind == XamlBindingPathTokenKind.Whitespace)
                endIndex--;
            if (startIndex == endIndex)
                return allowEmpty;

            var first = _tokens[startIndex];
            var last = _tokens[endIndex - 1];
            var span = TextSpan.FromBounds(first.Span.Start, last.Span.End);
            var text = _text.Substring(span.Start, span.Length);
            if (endIndex - startIndex == 1 &&
                first.Kind == XamlBindingPathTokenKind.StringLiteral &&
                first.Text.Length >= 2 &&
                first.Text[first.Text.Length - 1] == first.Text[0])
            {
                arguments.Add(new XamlBindingFunctionArgumentSyntax(
                    XamlBindingFunctionArgumentKind.StringLiteral,
                    text,
                    span,
                    stringValue: DecodeString(first.Text)));
                return true;
            }
            if (endIndex - startIndex == 1 &&
                first.Kind is XamlBindingPathTokenKind.IntegerLiteral or
                    XamlBindingPathTokenKind.NumericLiteral)
            {
                arguments.Add(new XamlBindingFunctionArgumentSyntax(
                    XamlBindingFunctionArgumentKind.NumericLiteral,
                    text,
                    span));
                return true;
            }
            if (endIndex - startIndex == 3 &&
                first.Kind == XamlBindingPathTokenKind.Identifier &&
                _tokens[startIndex + 1].Kind == XamlBindingPathTokenKind.Colon &&
                _tokens[startIndex + 2].Kind == XamlBindingPathTokenKind.Identifier &&
                (string.Equals(_tokens[startIndex + 2].Text, "True", StringComparison.Ordinal) ||
                 string.Equals(_tokens[startIndex + 2].Text, "False", StringComparison.Ordinal)))
            {
                arguments.Add(new XamlBindingFunctionArgumentSyntax(
                    XamlBindingFunctionArgumentKind.BooleanLiteral,
                    text,
                    span,
                    booleanValue: string.Equals(
                        _tokens[startIndex + 2].Text,
                        "True",
                        StringComparison.Ordinal),
                    namespacePrefix: first.Text));
                return true;
            }

            var path = new XamlBindingPathParser().Parse(text);
            if (path.HasErrors)
            {
                _errors.Add(span);
                return false;
            }
            arguments.Add(new XamlBindingFunctionArgumentSyntax(
                XamlBindingFunctionArgumentKind.BindingPath,
                text,
                span));
            return true;
        }

        private void SkipFunction(XamlBindingPathToken open)
        {
            var depth = 0;
            while (TryRead(out var token))
            {
                if (token.Kind == XamlBindingPathTokenKind.OpenParenthesis)
                    depth++;
                else if (token.Kind == XamlBindingPathTokenKind.CloseParenthesis)
                {
                    if (depth == 0) return;
                    depth--;
                }
            }
            _errors.Add(new TextSpan(open.Span.End, 0));
        }

        private bool TryParseCastHeader(
            XamlBindingPathToken open,
            out XamlBindingPathStepSyntax? cast)
        {
            cast = null;
            if (!TryRead(out var first) ||
                first.Kind != XamlBindingPathTokenKind.Identifier)
                return false;

            var typeName = first.Text;
            if (TryPeek(out var colon) &&
                colon.Kind == XamlBindingPathTokenKind.Colon)
            {
                Read();
                if (!TryRead(out var name) ||
                    name.Kind != XamlBindingPathTokenKind.Identifier)
                    return false;
                typeName += ":" + name.Text;
            }
            if (!TryRead(out var close) ||
                close.Kind != XamlBindingPathTokenKind.CloseParenthesis)
                return false;
            cast = new XamlBindingPathStepSyntax(
                XamlBindingPathStepKind.Cast,
                first,
                TextSpan.FromBounds(open.Span.Start, close.Span.End),
                typeName: typeName);
            return true;
        }

        private void ParseQualifiedMember(XamlBindingPathToken open)
        {
            if (!TryRead(out var first) ||
                first.Kind != XamlBindingPathTokenKind.Identifier)
            {
                _errors.Add(first.Span);
                return;
            }
            var typeName = first.Text;
            if (TryPeek(out var colon) &&
                colon.Kind == XamlBindingPathTokenKind.Colon)
            {
                Read();
                if (!TryRead(out var name) ||
                    name.Kind != XamlBindingPathTokenKind.Identifier)
                {
                    _errors.Add(name.Span);
                    return;
                }
                typeName += ":" + name.Text;
            }
            if (!TryRead(out var dot) ||
                dot.Kind != XamlBindingPathTokenKind.Dot ||
                !TryRead(out var member) ||
                member.Kind != XamlBindingPathTokenKind.Identifier ||
                !TryRead(out var close) ||
                close.Kind != XamlBindingPathTokenKind.CloseParenthesis)
            {
                _errors.Add(dot.Span);
                return;
            }
            _segments.Add(member);
            _steps.Add(new XamlBindingPathStepSyntax(
                XamlBindingPathStepKind.QualifiedMember,
                member,
                TextSpan.FromBounds(open.Span.Start, close.Span.End),
                typeName: typeName,
                memberName: member.Text));
        }

        private void ParseIndexer(XamlBindingPathToken open)
        {
            if (!TryRead(out var value))
            {
                _errors.Add(new TextSpan(open.Span.End, 0));
                return;
            }
            if (!TryRead(out var close) ||
                close.Kind != XamlBindingPathTokenKind.CloseBracket)
            {
                _errors.Add(close.Span);
                return;
            }

            var span = TextSpan.FromBounds(open.Span.Start, close.Span.End);
            if (value.Kind == XamlBindingPathTokenKind.IntegerLiteral &&
                int.TryParse(
                    value.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var integerValue))
            {
                _steps.Add(new XamlBindingPathStepSyntax(
                    XamlBindingPathStepKind.IntegerIndexer,
                    value,
                    span,
                    integerValue));
                return;
            }
            if (value.Kind == XamlBindingPathTokenKind.StringLiteral &&
                value.Text.Length >= 2 &&
                value.Text[value.Text.Length - 1] == value.Text[0])
            {
                _steps.Add(new XamlBindingPathStepSyntax(
                    XamlBindingPathStepKind.StringIndexer,
                    value,
                    span,
                    stringValue: DecodeString(value.Text)));
                return;
            }
            _errors.Add(value.Span);
        }

        private void AddMember(XamlBindingPathToken token)
        {
            _segments.Add(token);
            _steps.Add(new XamlBindingPathStepSyntax(
                XamlBindingPathStepKind.Member,
                token,
                token.Span,
                memberName: token.Text));
        }

        private bool TryPeek(out XamlBindingPathToken token)
        {
            var index = _index;
            return TryReadAt(ref index, out token);
        }

        private bool TryRead(out XamlBindingPathToken token) =>
            TryReadAt(ref _index, out token);

        private XamlBindingPathToken Read()
        {
            TryRead(out var token);
            return token;
        }

        private bool TryReadAt(
            ref int index,
            out XamlBindingPathToken token)
        {
            while (index < _tokens.Count)
            {
                token = _tokens[index++];
                if (token.Kind != XamlBindingPathTokenKind.Whitespace)
                    return true;
            }
            token = default;
            return false;
        }

        private static bool CanStartPrimary(XamlBindingPathTokenKind kind) =>
            kind == XamlBindingPathTokenKind.Identifier ||
            kind == XamlBindingPathTokenKind.OpenParenthesis ||
            kind == XamlBindingPathTokenKind.OpenBracket;
    }

    private static string DecodeString(string text)
    {
        var builder = ImmutableArray.CreateBuilder<char>(Math.Max(0, text.Length - 2));
        for (var index = 1; index < text.Length - 1; index++)
        {
            if (text[index] == '^' && index + 1 < text.Length - 1)
                index++;
            builder.Add(text[index]);
        }
        return new string(builder.ToArray());
    }

    private static bool IsIdentifierStart(char value) =>
        value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value) =>
        value == '_' || char.IsLetterOrDigit(value);
}
