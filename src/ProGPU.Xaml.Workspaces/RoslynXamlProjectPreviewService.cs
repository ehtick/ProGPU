using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Resources;
using ProGPU.Xaml.Roslyn;
using ProGPU.Xaml.Schema;
using ProGPU.Xaml.Tooling;

namespace ProGPU.Xaml.Workspaces;

public sealed class RoslynXamlProjectPreviewOptions
{
    /// <summary>
    /// Optional unsaved editor snapshot. The service applies it only to the immutable
    /// project snapshot used for this operation; it never mutates the owning workspace.
    /// </summary>
    public SourceText? EditedText { get; set; }

    public RoslynXamlCompilationInspectionOptions InspectionOptions { get; set; } =
        new RoslynXamlCompilationInspectionOptions();

    public bool EmitArtifact { get; set; } = true;

    /// <summary>
    /// Maps an AdditionalDocument to its stable project resource identity. The default
    /// uses Roslyn document folders and name, which is independent of machine paths.
    /// </summary>
    public Func<TextDocument, string>? LogicalPathProvider { get; set; }
}

public sealed class RoslynXamlProjectPreview
{
    internal RoslynXamlProjectPreview(
        Project project,
        TextDocument document,
        CSharpCompilation hostCompilation,
        XamlDocumentInspection sourceInspection,
        RoslynXamlCompilationInspection compilationInspection,
        RoslynXamlPreviewArtifact? artifact,
        string? qualifiedTypeName,
        string? materializationError,
        string resourceUri,
        XamlResourceDependencySlice resourceDependencies)
    {
        Project = project;
        Document = document;
        HostCompilation = hostCompilation;
        SourceInspection = sourceInspection;
        CompilationInspection = compilationInspection;
        Artifact = artifact;
        QualifiedTypeName = qualifiedTypeName;
        MaterializationError = materializationError;
        ResourceUri = resourceUri;
        ResourceDependencies = resourceDependencies;
    }

    /// <summary>The immutable project snapshot, including any supplied editor text.</summary>
    public Project Project { get; }
    public TextDocument Document { get; }
    public CSharpCompilation HostCompilation { get; }
    public XamlDocumentInspection SourceInspection { get; }
    public RoslynXamlCompilationInspection CompilationInspection { get; }
    public RoslynXamlPreviewArtifact? Artifact { get; }
    public string? QualifiedTypeName { get; }
    public string? MaterializationError { get; }
    public string ResourceUri { get; }
    public XamlResourceDependencySlice ResourceDependencies { get; }
    public bool CanMaterialize =>
        QualifiedTypeName != null &&
        MaterializationError == null &&
        Artifact?.Success == true;
}

/// <summary>
/// Compiles one XAML AdditionalDocument against an immutable Roslyn project snapshot.
/// Project C# sources, parse/compilation options, metadata/project references, and sibling
/// XAML resource manifests all participate. No Workspace is mutated and generated C# is
/// passed between phases only as Roslyn syntax trees.
/// </summary>
public sealed class RoslynXamlProjectPreviewService
{
    public async Task<RoslynXamlProjectPreview> CompileAsync(
        Project project,
        DocumentId xamlDocumentId,
        IRoslynXamlFrameworkProfile framework,
        RoslynXamlProjectPreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (xamlDocumentId == null)
            throw new ArgumentNullException(nameof(xamlDocumentId));
        if (framework == null) throw new ArgumentNullException(nameof(framework));
        options = options ?? new RoslynXamlProjectPreviewOptions();
        if (options.InspectionOptions == null)
            throw new ArgumentNullException(nameof(options.InspectionOptions));
        if (options.InspectionOptions.CompilerOptions == null)
        {
            throw new ArgumentNullException(
                nameof(options.InspectionOptions.CompilerOptions));
        }

        var document = project.GetAdditionalDocument(xamlDocumentId) ??
            throw new ArgumentException(
                "The supplied ID is not an AdditionalDocument in this project.",
                nameof(xamlDocumentId));
        if (options.EditedText != null)
        {
            var changedSolution = project.Solution.WithAdditionalDocumentText(
                xamlDocumentId,
                options.EditedText,
                PreservationMode.PreserveIdentity);
            project = changedSolution.GetProject(project.Id) ??
                throw new InvalidOperationException(
                    "The edited project snapshot could not be recovered.");
            document = project.GetAdditionalDocument(xamlDocumentId)!;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var compilation = await project.GetCompilationAsync(
            cancellationToken).ConfigureAwait(false);
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            throw new NotSupportedException(
                "XAML preview currently requires a C# Roslyn project.");
        }

        var hostCompilation = (CSharpCompilation)
            RoslynXamlHostCompilation.WithoutGeneratedXamlTrees(
                csharpCompilation);
        var text = await document.GetTextAsync(
            cancellationToken).ConfigureAwait(false);
        var requestedOptions = options.InspectionOptions;
        var requestedCompilerOptions = requestedOptions.CompilerOptions;
        var parseMode = requestedCompilerOptions.Strict
            ? XamlParseMode.Strict
            : XamlParseMode.Recovering;
        var targetInspection = Inspect(
            document,
            text,
            parseMode,
            cancellationToken);

        var initialTypeSystem = new RoslynXamlTypeSystem(
            hostCompilation,
            framework);
        var previewHost = new RoslynXamlPreviewHostFactory().Create(
            hostCompilation,
            targetInspection.Infoset,
            initialTypeSystem);
        var previewTypeSystem = new RoslynXamlTypeSystem(
            previewHost.Compilation,
            framework);

        var manifests = new List<XamlResourceDocumentManifest>();
        string? targetResourceUri = null;
        foreach (var candidate in project.AdditionalDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsXamlDocument(candidate, framework))
                continue;

            var candidateInspection =
                candidate.Id == xamlDocumentId
                    ? targetInspection
                    : Inspect(
                        candidate,
                        await candidate.GetTextAsync(
                            cancellationToken).ConfigureAwait(false),
                        parseMode,
                        cancellationToken);
            var logicalPath = GetLogicalPath(
                candidate,
                options.LogicalPathProvider);
            try
            {
                var semantic =
                    new RoslynXamlSemanticManifestCompiler().Compile(
                        candidateInspection.Infoset,
                        previewTypeSystem,
                        framework,
                        logicalPath,
                        requestedCompilerOptions.Strict,
                        cancellationToken);
                manifests.Add(semantic.Manifest);
                if (candidate.Id == xamlDocumentId)
                    targetResourceUri = semantic.ResourceUri;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Match the incremental-generator boundary: resource discovery remains
                // available from the canonical infoset when semantic enrichment fails.
                manifests.Add(
                    new XamlResourceDocumentManifestBuilder().Build(
                        candidateInspection.Infoset,
                        logicalPath));
                if (candidate.Id == xamlDocumentId)
                    targetResourceUri = logicalPath;
            }
        }

        var resourceDependencies =
            new XamlResourceProjectIndex(manifests)
                .GetDependencySlice(targetInspection.Infoset.Path);
        var compilerOptions = new XamlCompilerOptions
        {
            Framework = framework.Id,
            ResourceUri =
                targetResourceUri ??
                requestedCompilerOptions.ResourceUri ??
                GetLogicalPath(document, options.LogicalPathProvider),
            ResourceDependencies = resourceDependencies,
            Strict = requestedCompilerOptions.Strict,
            EmitHotReloadHooks =
                requestedCompilerOptions.EmitHotReloadHooks,
            EmitSourceComments =
                requestedCompilerOptions.EmitSourceComments,
            StaticResourceForwardReferenceMode =
                requestedCompilerOptions.StaticResourceForwardReferenceMode
        };
        var compilationInspection =
            new RoslynXamlCompilationInspectionService().Inspect(
                targetInspection,
                previewTypeSystem,
                framework,
                new RoslynXamlCompilationInspectionOptions
                {
                    CompilerOptions = compilerOptions,
                    MaximumProjectionEntries =
                        requestedOptions.MaximumProjectionEntries,
                    MaximumPreviewLength =
                        requestedOptions.MaximumPreviewLength
                },
                cancellationToken);
        var artifact =
            options.EmitArtifact &&
            previewHost.CanMaterialize
                ? new RoslynXamlPreviewArtifactCompiler().Compile(
                    previewHost.Compilation,
                    compilationInspection.CompilationResult,
                    cancellationToken)
                : null;
        return new RoslynXamlProjectPreview(
            project,
            document,
            previewHost.Compilation,
            targetInspection,
            compilationInspection,
            artifact,
            previewHost.QualifiedTypeName,
            previewHost.MaterializationError,
            compilerOptions.ResourceUri!,
            resourceDependencies);
    }

    private static XamlDocumentInspection Inspect(
        TextDocument document,
        SourceText text,
        XamlParseMode parseMode,
        CancellationToken cancellationToken) =>
        new XamlDocumentInspectionService().Inspect(
            text,
            document.FilePath ?? document.Name,
            new XamlDocumentInspectionOptions
            {
                ParseOptions = new XamlParseOptions
                {
                    Mode = parseMode
                }
            },
            cancellationToken);

    private static bool IsXamlDocument(
        TextDocument document,
        IXamlFrameworkProfile framework)
    {
        var extension = Path.GetExtension(
            document.FilePath ?? document.Name);
        return framework.FileExtensions.Any(
            candidate => string.Equals(
                candidate.StartsWith(".", StringComparison.Ordinal)
                    ? candidate
                    : "." + candidate,
                extension,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string GetLogicalPath(
        TextDocument document,
        Func<TextDocument, string>? provider)
    {
        var path = provider?.Invoke(document);
        if (!string.IsNullOrWhiteSpace(path))
            return path!;
        if (document.Folders.Count == 0)
            return document.Name;
        return string.Join("/", document.Folders) +
               "/" +
               document.Name;
    }
}
