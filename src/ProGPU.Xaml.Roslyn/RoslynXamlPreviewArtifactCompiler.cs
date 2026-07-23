using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// An in-memory assembly produced from a host compilation and the exact Roslyn syntax
/// trees accepted by the XAML compiler. Loading or activating the artifact is deliberately
/// left to a framework-specific, explicitly authorized tooling host.
/// </summary>
public sealed class RoslynXamlPreviewArtifact
{
    internal RoslynXamlPreviewArtifact(
        bool success,
        ImmutableArray<byte> peImage,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Success = success;
        PeImage = peImage;
        Diagnostics = diagnostics;
    }

    public bool Success { get; }
    public ImmutableArray<byte> PeImage { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

/// <summary>
/// Compiles generated XAML syntax trees into an in-memory artifact without reparsing
/// generated C# text. This component is framework-neutral and performs no assembly load,
/// reflection, UI mutation, or dynamic-code capability decision.
/// </summary>
public sealed class RoslynXamlPreviewArtifactCompiler
{
#pragma warning disable RS2008 // Compiler/tooling diagnostics are tracked in the XAML specification.
    private static readonly DiagnosticDescriptor NoGeneratedSyntaxDescriptor =
        new DiagnosticDescriptor(
            "PGXAML8001",
            "Live preview has no generated Roslyn syntax",
            "Live preview requires at least one generated Roslyn syntax tree",
            "ProGPU.Xaml.Tooling",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingGeneratedSyntaxDescriptor =
        new DiagnosticDescriptor(
            "PGXAML8002",
            "Generated XAML source has no Roslyn syntax tree",
            "Generated source '{0}' has no Roslyn syntax tree; preview never reparses generated C# text",
            "ProGPU.Xaml.Tooling",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
#pragma warning restore RS2008

    public RoslynXamlPreviewArtifact Compile(
        CSharpCompilation hostCompilation,
        XamlCompilationResult xamlCompilation,
        CancellationToken cancellationToken = default)
    {
        if (hostCompilation == null)
            throw new ArgumentNullException(nameof(hostCompilation));
        if (xamlCompilation == null)
            throw new ArgumentNullException(nameof(xamlCompilation));
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(xamlCompilation.Diagnostics);
        if (diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return Failed(diagnostics);
        }

        if (xamlCompilation.Sources.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                NoGeneratedSyntaxDescriptor,
                Location.None));
            return Failed(diagnostics);
        }

        var generatedTrees = new List<SyntaxTree>(
            xamlCompilation.Sources.Count);
        foreach (var source in xamlCompilation.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.GeneratedSyntaxTree == null)
            {
                diagnostics.Add(Diagnostic.Create(
                    MissingGeneratedSyntaxDescriptor,
                    Location.None,
                    source.HintName));
                continue;
            }

            generatedTrees.Add(source.GeneratedSyntaxTree);
        }

        if (diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return Failed(diagnostics);
        }

        var existingTrees = new HashSet<SyntaxTree>(
            hostCompilation.SyntaxTrees);
        var treesToAdd = generatedTrees
            .Where(existingTrees.Add)
            .ToArray();
        var compilation = hostCompilation.AddSyntaxTrees(
            treesToAdd);
        using (var stream = new MemoryStream())
        {
            var emit = compilation.Emit(
                stream,
                cancellationToken: cancellationToken);
            diagnostics.AddRange(emit.Diagnostics);
            if (!emit.Success)
                return Failed(diagnostics);

            return new RoslynXamlPreviewArtifact(
                success: true,
                ImmutableArray.CreateRange(stream.ToArray()),
                diagnostics.ToImmutable());
        }
    }

    private static RoslynXamlPreviewArtifact Failed(
        ImmutableArray<Diagnostic>.Builder diagnostics) =>
        new RoslynXamlPreviewArtifact(
            success: false,
            ImmutableArray<byte>.Empty,
            diagnostics.ToImmutable());
}
