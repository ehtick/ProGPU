using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Parsing;

public enum XamlParseMode
{
    Strict,
    Recovering
}

public sealed class XamlParseOptions
{
    public const int MaximumSupportedDepth = 1024;

    public XamlParseMode Mode { get; set; } = XamlParseMode.Strict;
    public int MaximumDepth { get; set; } = 512;
    public int MaximumTokens { get; set; } = 4 * 1024 * 1024;
    public int MaximumTokenLength { get; set; } = 16 * 1024 * 1024;
    public int MaximumAttributeLength { get; set; } = 1024 * 1024;
    public int MaximumNodes { get; set; } = 2 * 1024 * 1024;
    public int MaximumDiagnostics { get; set; } = 1024;
    public IReadOnlyList<IXamlSyntaxExtension> Extensions { get; set; } = Array.Empty<IXamlSyntaxExtension>();

    internal static XamlParseOptions Snapshot(XamlParseOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        ValidatePositive(options.MaximumDepth, nameof(MaximumDepth));
        if (options.MaximumDepth > MaximumSupportedDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDepth),
                $"The maximum supported XAML nesting depth is {MaximumSupportedDepth}.");
        }
        ValidatePositive(options.MaximumTokens, nameof(MaximumTokens));
        ValidatePositive(options.MaximumTokenLength, nameof(MaximumTokenLength));
        ValidatePositive(options.MaximumAttributeLength, nameof(MaximumAttributeLength));
        ValidatePositive(options.MaximumNodes, nameof(MaximumNodes));
        ValidatePositive(options.MaximumDiagnostics, nameof(MaximumDiagnostics));
        if (options.Extensions == null)
            throw new ArgumentNullException(nameof(Extensions));
        var extensions = new IXamlSyntaxExtension[options.Extensions.Count];
        for (var index = 0; index < extensions.Length; index++)
        {
            extensions[index] = options.Extensions[index] ??
                throw new ArgumentException(
                    "XAML syntax extension registrations cannot contain null.",
                    nameof(Extensions));
        }
        return new XamlParseOptions
        {
            Mode = options.Mode,
            MaximumDepth = options.MaximumDepth,
            MaximumTokens = options.MaximumTokens,
            MaximumTokenLength = options.MaximumTokenLength,
            MaximumAttributeLength = options.MaximumAttributeLength,
            MaximumNodes = options.MaximumNodes,
            MaximumDiagnostics = options.MaximumDiagnostics,
            Extensions = extensions
        };
    }

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(
                parameterName,
                "XAML parser limits must be positive.");
    }
}

public interface IXamlSyntaxExtension
{
    IEnumerable<Diagnostic> Analyze(XamlSyntaxTree syntaxTree);
}
