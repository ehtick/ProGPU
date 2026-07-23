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
    public XamlParseMode Mode { get; set; } = XamlParseMode.Strict;
    public int MaximumDepth { get; set; } = 512;
    public int MaximumTokens { get; set; } = 4 * 1024 * 1024;
    public int MaximumTokenLength { get; set; } = 16 * 1024 * 1024;
    public int MaximumAttributeLength { get; set; } = 1024 * 1024;
    public int MaximumNodes { get; set; } = 2 * 1024 * 1024;
    public int MaximumDiagnostics { get; set; } = 1024;
    public IReadOnlyList<IXamlSyntaxExtension> Extensions { get; set; } = Array.Empty<IXamlSyntaxExtension>();
}

public interface IXamlSyntaxExtension
{
    IEnumerable<Diagnostic> Analyze(XamlSyntaxTree syntaxTree);
}
