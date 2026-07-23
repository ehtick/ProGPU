using System;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Parsing;

namespace ProGPU.Xaml.Generation;

/// <summary>Grammar-validating generator for markup-extension source.</summary>
public static class XamlMarkupSyntaxGenerator
{
    public static SourceText Generate(XamlMarkupExtension extension)
    {
        if (extension == null) throw new ArgumentNullException(nameof(extension));
        var builder = new StringBuilder();
        AppendExtension(builder, extension);
        var text = SourceText.From(builder.ToString());
        var parsed = new XamlMarkupExtensionParser().Parse(text, new TextSpan(0, text.Length));
        if (parsed.HasErrors || parsed.Root == null)
            throw new InvalidOperationException("The markup syntax model cannot be represented by the standard grammar.");
        return text;
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
