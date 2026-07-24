using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Optional Roslyn type-system capability used to keep generated syntax trees compatible
/// with their host compilation without serializing or reparsing generated C#.
/// </summary>
public interface IRoslynXamlParseOptionsProvider
{
    CSharpParseOptions ParseOptions { get; }
}

internal static class RoslynXamlSyntaxTreeOptions
{
    public static CSharpParseOptions From(
        CSharpCompilation compilation) =>
        compilation.SyntaxTrees
            .Select(static tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .FirstOrDefault() ??
        CSharpParseOptions.Default;
}
