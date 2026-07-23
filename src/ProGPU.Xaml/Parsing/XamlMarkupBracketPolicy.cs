using System;
using System.Collections.Generic;
using ProGPU.Xaml.Schema;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

/// <summary>
/// Bridges canonical member annotations to the shared schema-independent markup parser.
/// Invalid or conflicting pairs are excluded; the binder reports their retained evidence.
/// </summary>
public static class XamlMarkupBracketPolicy
{
    public static IReadOnlyDictionary<char, char> CreatePairs(
        XamlMemberInfo member)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));
        var result = new Dictionary<char, char>();
        foreach (var pair in member.MarkupExtensionBracketCharacters)
        {
            if (!pair.IsValid) continue;
            if (!result.ContainsKey(pair.OpeningBracket))
                result.Add(pair.OpeningBracket, pair.ClosingBracket);
        }
        return result;
    }

    public static XamlMarkupParseOptions CreateOptions(
        XamlMemberInfo member,
        XamlMarkupParseOptions? template = null)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));
        template = template ?? new XamlMarkupParseOptions();
        return new XamlMarkupParseOptions
        {
            MaximumDepth = template.MaximumDepth,
            MaximumTokens = template.MaximumTokens,
            MaximumTokenLength = template.MaximumTokenLength,
            MaximumArguments = template.MaximumArguments,
            MaximumDiagnostics = template.MaximumDiagnostics,
            TokenRecognizers = template.TokenRecognizers,
            SyntaxLanguage = template.SyntaxLanguage,
            Context = template.Context,
            BracketPairs = CreatePairs(member),
            BracketPairResolver = template.BracketPairResolver
        };
    }
}
