using System;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Generation;

/// <summary>Grammar-validating generator for markup-extension source.</summary>
public static class XamlMarkupSyntaxGenerator
{
    public static SourceText Generate(
        XamlMarkupExtension extension,
        XamlMarkupLanguage? language = null,
        string? preferredPluginId = null)
    {
        if (extension == null) throw new ArgumentNullException(nameof(extension));
        if (language != null &&
            TryGenerateCustom(extension, language, preferredPluginId, out var custom))
            return custom;
        if (preferredPluginId != null)
            throw new InvalidOperationException(
                $"Markup syntax plugin '{preferredPluginId}' cannot format this syntax model.");

        var builder = new StringBuilder();
        AppendExtension(builder, extension);
        var text = SourceText.From(builder.ToString());
        var parsed = new XamlMarkupExtensionParser().Parse(text, new TextSpan(0, text.Length));
        if (parsed.HasErrors || parsed.Root == null)
            throw new InvalidOperationException("The markup syntax model cannot be represented by the standard grammar.");
        return text;
    }

    private static bool TryGenerateCustom(
        XamlMarkupExtension extension,
        XamlMarkupLanguage language,
        string? preferredPluginId,
        out SourceText source)
    {
        source = null!;
        if (preferredPluginId != null)
        {
            if (!language.TryGetPlugin(preferredPluginId, out var preferred))
                throw new InvalidOperationException(
                    $"Markup syntax plugin '{preferredPluginId}' is not registered.");
            return TryGenerateWithPlugin(extension, language, preferred, out source);
        }

        IXamlMarkupSyntaxPlugin? winner = null;
        string? winningText = null;
        foreach (var plugin in language.Plugins)
        {
            if (winner != null && plugin.Priority < winner.Priority) break;
            if ((plugin.Capabilities & XamlMarkupSyntaxCapabilities.Format) == 0) continue;
            if (!plugin.TryFormat(extension, out var candidateText)) continue;
            if (candidateText == null)
                throw new InvalidOperationException(
                    $"Markup syntax plugin '{plugin.Id}' returned null generated text.");

            if (winner == null)
            {
                winner = plugin;
                winningText = candidateText;
                continue;
            }

            var canCoalesce =
                winner.ConflictPolicy == XamlMarkupSyntaxConflictPolicy.CoalesceEquivalent &&
                plugin.ConflictPolicy == XamlMarkupSyntaxConflictPolicy.CoalesceEquivalent &&
                string.Equals(winningText, candidateText, StringComparison.Ordinal);
            if (!canCoalesce)
                throw new InvalidOperationException(
                    $"Markup syntax generation is ambiguous between '{winner.Id}' and '{plugin.Id}' " +
                    $"at priority {winner.Priority}.");
        }

        if (winner == null) return false;
        source = ValidateCustomText(extension, language, winner, winningText!);
        return true;
    }

    private static bool TryGenerateWithPlugin(
        XamlMarkupExtension extension,
        XamlMarkupLanguage language,
        IXamlMarkupSyntaxPlugin plugin,
        out SourceText source)
    {
        source = null!;
        if ((plugin.Capabilities & XamlMarkupSyntaxCapabilities.Format) == 0 ||
            !plugin.TryFormat(extension, out var text))
            return false;
        if (text == null)
            throw new InvalidOperationException(
                $"Markup syntax plugin '{plugin.Id}' returned null generated text.");
        source = ValidateCustomText(extension, language, plugin, text);
        return true;
    }

    private static SourceText ValidateCustomText(
        XamlMarkupExtension extension,
        XamlMarkupLanguage language,
        IXamlMarkupSyntaxPlugin plugin,
        string text)
    {
        var source = SourceText.From(text);
        var parsed = new XamlMarkupExtensionParser().Parse(
            source,
            new TextSpan(0, source.Length),
            options: new XamlMarkupParseOptions
            {
                SyntaxLanguage = language,
                Context = XamlMarkupSyntaxContexts.Standalone
            });
        if (parsed.HasErrors ||
            parsed.Root == null ||
            !string.Equals(parsed.SyntaxPluginId, plugin.Id, StringComparison.Ordinal) ||
            !XamlMarkupLanguage.AreEquivalent(extension, parsed.Root))
            throw new InvalidOperationException(
                $"Markup syntax plugin '{plugin.Id}' generated text that does not round-trip " +
                "to the supplied canonical syntax model.");
        return source;
    }

    private static void AppendExtension(StringBuilder builder, XamlMarkupExtension extension)
    {
        ValidateName(extension.Name);
        builder.Append('{').Append(extension.Name);
        var needsSeparator = false;
        foreach (var positional in extension.PositionalArguments)
        {
            builder.Append(needsSeparator ? ", " : " ");
            AppendValue(builder, positional);
            needsSeparator = true;
        }
        foreach (var named in extension.NamedArguments)
        {
            ValidateName(named.Name);
            builder.Append(needsSeparator ? ", " : " ");
            builder.Append(named.Name).Append('=');
            AppendValue(builder, named.Value);
            needsSeparator = true;
        }
        builder.Append('}');
    }

    private static void AppendValue(StringBuilder builder, XamlMarkupValue value)
    {
        if (value is XamlMarkupExtensionValue nested)
        {
            AppendExtension(builder, nested.Extension);
            return;
        }
        var text = ((XamlMarkupTextValue)value).Text;
        if (!NeedsQuoting(text))
        {
            builder.Append(text);
            return;
        }
        builder.Append('\'');
        foreach (var character in text)
        {
            if (character == '\\' || character == '\'') builder.Append('\\');
            builder.Append(character);
        }
        builder.Append('\'');
    }

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0) return true;
        foreach (var character in value)
            if (char.IsWhiteSpace(character) || character == ',' || character == '=' ||
                character == '{' || character == '}' || character == '\'' || character == '"') return true;
        return false;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Markup names cannot be empty.", nameof(name));
        foreach (var character in name)
            if (!(char.IsLetterOrDigit(character) || character == '_' || character == ':' ||
                  character == '.' || character == '-'))
                throw new ArgumentException($"'{name}' is not a valid markup name.", nameof(name));
    }
}
