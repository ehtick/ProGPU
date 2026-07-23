using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Formatting;

public sealed class XamlMarkupFormattingOptions
{
    public bool SpaceAfterComma { get; set; } = true;
    public bool SpaceAroundEquals { get; set; }
    public bool SpaceAfterExtensionName { get; set; } = true;
}

/// <summary>Rule-based formatter for the shared markup-value token stream.</summary>
public static class XamlMarkupFormatter
{
    public static ImmutableArray<TextChange> GetTextChanges(
        XamlMarkupParseResult syntax,
        SourceText source,
        XamlMarkupFormattingOptions? options = null)
    {
        if (syntax == null) throw new ArgumentNullException(nameof(syntax));
        if (source == null) throw new ArgumentNullException(nameof(source));
        options = options ?? new XamlMarkupFormattingOptions();
        var changes = ImmutableArray.CreateBuilder<TextChange>();
        var tokens = syntax.Tokens;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Kind != XamlMarkupTokenKind.Whitespace) continue;
            var previous = PreviousSignificant(tokens, index);
            var next = NextSignificant(tokens, index);
            if (previous == XamlMarkupTokenKind.None || next == XamlMarkupTokenKind.None) continue;

            string? desired = null;
            if (previous == XamlMarkupTokenKind.Comma)
                desired = options.SpaceAfterComma ? " " : string.Empty;
            else if (previous == XamlMarkupTokenKind.Equals || next == XamlMarkupTokenKind.Equals)
                desired = options.SpaceAroundEquals ? " " : string.Empty;
            else if (previous == XamlMarkupTokenKind.OpenBrace || next == XamlMarkupTokenKind.CloseBrace)
                desired = string.Empty;
            else if (previous == XamlMarkupTokenKind.Identifier &&
                     (next == XamlMarkupTokenKind.Identifier || next == XamlMarkupTokenKind.Text ||
                      next == XamlMarkupTokenKind.OpenBrace || next == XamlMarkupTokenKind.QuotedString))
                desired = options.SpaceAfterExtensionName ? " " : string.Empty;

            if (desired != null && !string.Equals(source.ToString(token.Span), desired, StringComparison.Ordinal))
                changes.Add(new TextChange(token.Span, desired));
        }
        return changes.ToImmutable();
    }

    public static SourceText Format(
        XamlMarkupParseResult syntax,
        SourceText source,
        XamlMarkupFormattingOptions? options = null) =>
        source.WithChanges(GetTextChanges(syntax, source, options));

    private static XamlMarkupTokenKind PreviousSignificant(
        ImmutableArray<XamlMarkupSyntaxToken> tokens,
        int index)
    {
        for (var current = index - 1; current >= 0; current--)
            if (!tokens[current].IsTrivia) return tokens[current].Kind;
        return XamlMarkupTokenKind.None;
    }

    private static XamlMarkupTokenKind NextSignificant(
        ImmutableArray<XamlMarkupSyntaxToken> tokens,
        int index)
    {
        for (var current = index + 1; current < tokens.Length; current++)
            if (!tokens[current].IsTrivia && tokens[current].Kind != XamlMarkupTokenKind.EndOfFile)
                return tokens[current].Kind;
        return XamlMarkupTokenKind.None;
    }
}
