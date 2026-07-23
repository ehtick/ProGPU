using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProGPU.Xaml.Infoset;
using ProGPU.Xaml.Roslyn;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Workspaces;

public enum RoslynXamlProjectDeltaMode
{
    None,
    XamlOnly,
    MetadataOnly,
    MetadataAndXaml
}

public enum RoslynXamlDocumentDeltaKind
{
    Unchanged,
    SyntaxOnly,
    Semantic,
    Added,
    Removed
}

public enum RoslynXamlReloadAction
{
    None,
    ReplaceTarget,
    CoordinateMetadataAndReplaceTarget,
    RetainLastGood
}

public enum RoslynXamlMetadataDeltaReason
{
    CompilationOptions,
    MetadataReferences,
    HostSyntaxTrees
}

public sealed class RoslynXamlStableIdentityDelta
{
    internal RoslynXamlStableIdentityDelta(
        ImmutableArray<ulong> added,
        ImmutableArray<ulong> removed,
        ImmutableArray<ulong> modified,
        ImmutableArray<ulong> retained)
    {
        Added = added;
        Removed = removed;
        Modified = modified;
        Retained = retained;
    }

    public ImmutableArray<ulong> Added { get; }
    public ImmutableArray<ulong> Removed { get; }
    public ImmutableArray<ulong> Modified { get; }
    public ImmutableArray<ulong> Retained { get; }
}

public sealed class RoslynXamlProjectDocumentDelta
{
    internal RoslynXamlProjectDocumentDelta(
        string resourceUri,
        RoslynXamlDocumentDeltaKind kind,
        string? previousSyntaxFingerprint,
        string? currentSyntaxFingerprint,
        string? previousSemanticFingerprint,
        string? currentSemanticFingerprint,
        RoslynXamlStableIdentityDelta stableIdentities,
        RoslynXamlProjectDocumentCompilation? previous,
        RoslynXamlProjectDocumentCompilation? current)
    {
        ResourceUri = resourceUri;
        Kind = kind;
        PreviousSyntaxFingerprint =
            previousSyntaxFingerprint;
        CurrentSyntaxFingerprint =
            currentSyntaxFingerprint;
        PreviousSemanticFingerprint =
            previousSemanticFingerprint;
        CurrentSemanticFingerprint =
            currentSemanticFingerprint;
        StableIdentities = stableIdentities;
        Previous = previous;
        Current = current;
    }

    public string ResourceUri { get; }
    public RoslynXamlDocumentDeltaKind Kind { get; }
    public string? PreviousSyntaxFingerprint { get; }
    public string? CurrentSyntaxFingerprint { get; }
    public string? PreviousSemanticFingerprint { get; }
    public string? CurrentSemanticFingerprint { get; }
    public RoslynXamlStableIdentityDelta StableIdentities { get; }
    public RoslynXamlProjectDocumentCompilation? Previous { get; }
    public RoslynXamlProjectDocumentCompilation? Current { get; }
    public bool HasSemanticChange =>
        Kind == RoslynXamlDocumentDeltaKind.Semantic ||
        Kind == RoslynXamlDocumentDeltaKind.Added ||
        Kind == RoslynXamlDocumentDeltaKind.Removed;
}

public sealed class RoslynXamlProjectDeltaPlan
{
    internal RoslynXamlProjectDeltaPlan(
        RoslynXamlProjectPreview previous,
        RoslynXamlProjectPreview current,
        RoslynXamlProjectDeltaMode mode,
        RoslynXamlReloadAction action,
        ImmutableArray<RoslynXamlProjectDocumentDelta> documents,
        bool targetDocumentChanged,
        bool targetDependencyChanged,
        bool metadataChanged,
        ImmutableArray<
            RoslynXamlMetadataDeltaReason>
            metadataReasons,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Previous = previous;
        Current = current;
        Mode = mode;
        Action = action;
        Documents = documents;
        TargetDocumentChanged = targetDocumentChanged;
        TargetDependencyChanged = targetDependencyChanged;
        MetadataChanged = metadataChanged;
        MetadataReasons = metadataReasons;
        Diagnostics = diagnostics;
    }

    public RoslynXamlProjectPreview Previous { get; }
    public RoslynXamlProjectPreview Current { get; }
    public RoslynXamlProjectDeltaMode Mode { get; }
    public RoslynXamlReloadAction Action { get; }
    public ImmutableArray<RoslynXamlProjectDocumentDelta> Documents { get; }
    public bool TargetDocumentChanged { get; }
    public bool TargetDependencyChanged { get; }
    public bool MetadataChanged { get; }
    public ImmutableArray<
        RoslynXamlMetadataDeltaReason>
        MetadataReasons { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool HasSemanticXamlChanges =>
        Documents.Any(
            static document =>
                document.HasSemanticChange);
    public bool RequiresTargetReplacement =>
        Action == RoslynXamlReloadAction.ReplaceTarget ||
        Action ==
        RoslynXamlReloadAction
            .CoordinateMetadataAndReplaceTarget;
    public bool CanApply =>
        RequiresTargetReplacement &&
        Diagnostics.All(
            static diagnostic =>
                diagnostic.Severity !=
                DiagnosticSeverity.Error) &&
        Current.CanMaterialize;

    /// <summary>
    /// Projects the accepted executable payload across a framework-adapter boundary
    /// without requiring that adapter to reference Roslyn compiler assemblies.
    /// </summary>
    public bool TryGetExecutableUpdate(
        out byte[] peImage,
        out string qualifiedTypeName)
    {
        if (CanApply &&
            Current.Artifact != null &&
            Current.QualifiedTypeName != null)
        {
            peImage =
                Current.Artifact.PeImage.ToArray();
            qualifiedTypeName =
                Current.QualifiedTypeName;
            return true;
        }

        peImage = Array.Empty<byte>();
        qualifiedTypeName = string.Empty;
        return false;
    }

    public string FailureMessage =>
        Diagnostics.FirstOrDefault()?
            .GetMessage(
                CultureInfo.InvariantCulture) ??
        Current.MaterializationError ??
        "The project delta has no accepted executable artifact.";
}

/// <summary>
/// Compares two accepted immutable project-preview snapshots. XAML identity comes from
/// canonical stable node IDs and Roslyn projection annotations; semantic equality comes
/// from structured syntax nodes. Source positions never participate in identity.
/// </summary>
public sealed class RoslynXamlProjectDeltaService
{
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor UnavailableArtifactDescriptor =
        new DiagnosticDescriptor(
            "PGXAML8003",
            "XAML delta artifact is unavailable",
            "XAML delta cannot replace '{0}': {1}",
            "ProGPU.Xaml.Tooling",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TargetMismatchDescriptor =
        new DiagnosticDescriptor(
            "PGXAML8004",
            "XAML delta target changed",
            "XAML delta target changed from '{0}' to '{1}'; the last good tree was retained",
            "ProGPU.Xaml.Tooling",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
#pragma warning restore RS2008

    public RoslynXamlProjectDeltaPlan CreatePlan(
        RoslynXamlProjectPreview previous,
        RoslynXamlProjectPreview current,
        CancellationToken cancellationToken = default)
    {
        if (previous == null)
            throw new ArgumentNullException(nameof(previous));
        if (current == null)
            throw new ArgumentNullException(nameof(current));
        cancellationToken.ThrowIfCancellationRequested();

        var previousByUri = IndexDocuments(
            previous.ProjectDocuments);
        var currentByUri = IndexDocuments(
            current.ProjectDocuments);
        var resourceUris = previousByUri.Keys
            .Concat(currentByUri.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                static value => value,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deltas =
            ImmutableArray.CreateBuilder<
                RoslynXamlProjectDocumentDelta>(
                resourceUris.Length);
        foreach (var resourceUri in resourceUris)
        {
            cancellationToken.ThrowIfCancellationRequested();
            previousByUri.TryGetValue(
                resourceUri,
                out var oldDocument);
            currentByUri.TryGetValue(
                resourceUri,
                out var newDocument);
            deltas.Add(CreateDocumentDelta(
                resourceUri,
                oldDocument,
                newDocument,
                cancellationToken));
        }

        var documents = deltas.ToImmutable();
        var metadataReasons =
            GetMetadataDeltaReasons(
            previous.HostCompilation,
            current.HostCompilation,
            cancellationToken);
        var metadataChanged =
            metadataReasons.Length != 0;
        var semanticXamlChanged = documents.Any(
            static document =>
                document.HasSemanticChange);
        var mode = GetMode(
            metadataChanged,
            semanticXamlChanged);
        var targetChanged = IsTargetChanged(
            previous,
            current,
            documents);
        var dependencyChanged = IsTargetDependencyChanged(
            previous,
            current,
            documents);
        var requiresReplacement =
            metadataChanged ||
            targetChanged ||
            dependencyChanged;
        var diagnostics =
            ImmutableArray.CreateBuilder<Diagnostic>();
        var action = requiresReplacement
            ? metadataChanged
                ? RoslynXamlReloadAction
                    .CoordinateMetadataAndReplaceTarget
                : RoslynXamlReloadAction.ReplaceTarget
            : RoslynXamlReloadAction.None;

        if (requiresReplacement &&
            !string.Equals(
                previous.QualifiedTypeName,
                current.QualifiedTypeName,
                StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic.Create(
                TargetMismatchDescriptor,
                Location.None,
                previous.QualifiedTypeName ?? "<unavailable>",
                current.QualifiedTypeName ?? "<unavailable>"));
            action =
                RoslynXamlReloadAction.RetainLastGood;
        }
        else if (requiresReplacement &&
                 !current.CanMaterialize)
        {
            diagnostics.Add(Diagnostic.Create(
                UnavailableArtifactDescriptor,
                Location.None,
                current.QualifiedTypeName ??
                previous.QualifiedTypeName ??
                "<unavailable>",
                GetMaterializationFailure(current)));
            action =
                RoslynXamlReloadAction.RetainLastGood;
        }

        return new RoslynXamlProjectDeltaPlan(
            previous,
            current,
            mode,
            action,
            documents,
            targetChanged,
            dependencyChanged,
            metadataChanged,
            metadataReasons,
            diagnostics.ToImmutable());
    }

    private static IReadOnlyDictionary<
        string,
        RoslynXamlProjectDocumentCompilation> IndexDocuments(
        IReadOnlyList<
            RoslynXamlProjectDocumentCompilation> documents)
    {
        var result =
            new Dictionary<
                string,
                RoslynXamlProjectDocumentCompilation>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (result.ContainsKey(
                    document.ResourceUri))
            {
                throw new InvalidOperationException(
                    "Project preview contains duplicate resource URI '" +
                    document.ResourceUri +
                    "'.");
            }

            result.Add(
                document.ResourceUri,
                document);
        }

        return result;
    }

    private static RoslynXamlProjectDocumentDelta
        CreateDocumentDelta(
            string resourceUri,
            RoslynXamlProjectDocumentCompilation? previous,
            RoslynXamlProjectDocumentCompilation? current,
            CancellationToken cancellationToken)
    {
        var previousSyntax = previous == null
            ? null
            : RoslynXamlSourceChecksum.ComputeHex(
                previous.SourceInspection.Infoset.SourceText);
        var currentSyntax = current == null
            ? null
            : RoslynXamlSourceChecksum.ComputeHex(
                current.SourceInspection.Infoset.SourceText);
        var previousSemantic = previous == null
            ? null
            : ComputeSemanticFingerprint(previous);
        var currentSemantic = current == null
            ? null
            : ComputeSemanticFingerprint(current);
        RoslynXamlDocumentDeltaKind kind;
        if (previous == null)
            kind = RoslynXamlDocumentDeltaKind.Added;
        else if (current == null)
            kind = RoslynXamlDocumentDeltaKind.Removed;
        else if (!SemanticallyEquivalent(
                     previous,
                     current,
                     cancellationToken))
        {
            kind = RoslynXamlDocumentDeltaKind.Semantic;
        }
        else if (!string.Equals(
                     previousSyntax,
                     currentSyntax,
                     StringComparison.Ordinal))
        {
            kind = RoslynXamlDocumentDeltaKind.SyntaxOnly;
        }
        else
        {
            kind =
                RoslynXamlDocumentDeltaKind.Unchanged;
        }

        return new RoslynXamlProjectDocumentDelta(
            resourceUri,
            kind,
            previousSyntax,
            currentSyntax,
            previousSemantic,
            currentSemantic,
            CompareStableIdentities(
                previous,
                current,
                cancellationToken),
            previous,
            current);
    }

    private static RoslynXamlStableIdentityDelta
        CompareStableIdentities(
            RoslynXamlProjectDocumentCompilation? previous,
            RoslynXamlProjectDocumentCompilation? current,
            CancellationToken cancellationToken)
    {
        var oldIds = CollectInfosetStableIds(
            previous?.SourceInspection.Infoset.Root,
            cancellationToken);
        var newIds = CollectInfosetStableIds(
            current?.SourceInspection.Infoset.Root,
            cancellationToken);
        var modified = previous == null || current == null
            ? ImmutableArray<ulong>.Empty
            : CollectModifiedProjectionIds(
                previous.CompilationResult,
                current.CompilationResult,
                cancellationToken);
        var modifiedSet =
            new HashSet<ulong>(modified);
        return new RoslynXamlStableIdentityDelta(
            newIds.Except(oldIds)
                .OrderBy(static value => value)
                .ToImmutableArray(),
            oldIds.Except(newIds)
                .OrderBy(static value => value)
                .ToImmutableArray(),
            modified,
            oldIds.Intersect(newIds)
                .Where(value => !modifiedSet.Contains(value))
                .OrderBy(static value => value)
                .ToImmutableArray());
    }

    private static HashSet<ulong> CollectInfosetStableIds(
        XamlInfosetObject? root,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<ulong>();
        if (root == null)
            return result;
        var stack = new Stack<XamlInfosetValue>();
        stack.Push(root);
        while (stack.Count != 0)
        {
            if ((result.Count & 0xff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var value = stack.Pop();
            result.Add(value.StableId);
            if (value is XamlInfosetObject objectValue)
            {
                for (var index =
                         objectValue.Members.Length - 1;
                     index >= 0;
                     index--)
                {
                    stack.Push(objectValue.Members[index]);
                }
            }
            else if (value is XamlInfosetMember member)
            {
                for (var index =
                         member.Values.Length - 1;
                     index >= 0;
                     index--)
                {
                    stack.Push(member.Values[index]);
                }
            }
        }

        return result;
    }

    private static ImmutableArray<ulong>
        CollectModifiedProjectionIds(
            XamlCompilationResult previous,
            XamlCompilationResult current,
            CancellationToken cancellationToken)
    {
        var oldEntries = IndexProjectionEntries(previous);
        var newEntries = IndexProjectionEntries(current);
        var modified = new HashSet<ulong>();
        foreach (var key in oldEntries.Keys.Intersect(
                     newEntries.Keys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldNodes = oldEntries[key];
            var newNodes = newEntries[key];
            if (oldNodes.Length != newNodes.Length)
            {
                modified.Add(key.StableNodeId);
                continue;
            }

            for (var index = 0;
                 index < oldNodes.Length;
                 index++)
            {
                if (!oldNodes[index].IsEquivalentTo(
                        newNodes[index]))
                {
                    modified.Add(key.StableNodeId);
                    break;
                }
            }
        }

        return modified
            .OrderBy(static value => value)
            .ToImmutableArray();
    }

    private static IReadOnlyDictionary<
        ProjectionIdentity,
        SyntaxNodeOrToken[]> IndexProjectionEntries(
        XamlCompilationResult result) =>
        result.Sources
            .Where(
                static source =>
                    source.GeneratedSyntaxTree != null)
            .SelectMany(
                static source =>
                    XamlProjectionMap.Read(
                        source.GeneratedSyntaxTree!))
            .GroupBy(
                static entry =>
                    new ProjectionIdentity(
                        entry.StableNodeId,
                        entry.Kind,
                        entry.MemberId))
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(
                        static entry =>
                            entry.GeneratedNode)
                    .ToArray());

    private static bool SemanticallyEquivalent(
        RoslynXamlProjectDocumentCompilation previous,
        RoslynXamlProjectDocumentCompilation current,
        CancellationToken cancellationToken)
    {
        if (!previous.ResourceDependencies.Equals(
                current.ResourceDependencies))
            return false;
        var oldSources = previous.CompilationResult.Sources
            .ToDictionary(
                static source => source.HintName,
                StringComparer.Ordinal);
        var newSources = current.CompilationResult.Sources
            .ToDictionary(
                static source => source.HintName,
                StringComparer.Ordinal);
        if (oldSources.Count != newSources.Count ||
            oldSources.Keys.Except(
                    newSources.Keys,
                    StringComparer.Ordinal)
                .Any())
            return false;
        foreach (var pair in oldSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldTree = pair.Value.GeneratedSyntaxTree;
            var newTree =
                newSources[pair.Key].GeneratedSyntaxTree;
            if (oldTree == null || newTree == null)
            {
                if (oldTree != newTree)
                    return false;
                continue;
            }

            if (!oldTree.GetRoot(cancellationToken)
                    .IsEquivalentTo(
                        newTree.GetRoot(cancellationToken)))
            {
                return false;
            }
        }

        return DiagnosticShapesEquivalent(
            previous.CompilationResult.Diagnostics,
            current.CompilationResult.Diagnostics);
    }

    private static bool DiagnosticShapesEquivalent(
        IReadOnlyList<Diagnostic> previous,
        IReadOnlyList<Diagnostic> current)
    {
        var oldShapes = previous.Select(DiagnosticShape)
            .OrderBy(
                static value => value,
                StringComparer.Ordinal);
        var newShapes = current.Select(DiagnosticShape)
            .OrderBy(
                static value => value,
                StringComparer.Ordinal);
        return oldShapes.SequenceEqual(
            newShapes,
            StringComparer.Ordinal);
    }

    private static string DiagnosticShape(
        Diagnostic diagnostic) =>
        diagnostic.Id +
        "\0" +
        diagnostic.Severity +
        "\0" +
        diagnostic.GetMessage(
            CultureInfo.InvariantCulture);

    private static string ComputeSemanticFingerprint(
        RoslynXamlProjectDocumentCompilation document)
    {
        var hash = new Fingerprint64();
        hash.Add(document.ResourceUri);
        hash.Add(document.ResourceDependencies.Fingerprint);
        foreach (var source in document.CompilationResult
                     .Sources.OrderBy(
                         static value => value.HintName,
                         StringComparer.Ordinal))
        {
            hash.Add(source.HintName);
            var tree = source.GeneratedSyntaxTree;
            if (tree == null)
            {
                hash.Add("<missing-tree>");
                continue;
            }

            foreach (var token in tree.GetRoot()
                         .DescendantTokens(
                             descendIntoTrivia: false))
            {
                hash.Add(token.RawKind);
                hash.Add(token.ValueText);
            }
        }

        foreach (var diagnostic in document
                     .CompilationResult.Diagnostics
                     .Select(DiagnosticShape)
                     .OrderBy(
                         static value => value,
                         StringComparer.Ordinal))
        {
            hash.Add(diagnostic);
        }

        return hash.Value.ToString(
            "x16",
            CultureInfo.InvariantCulture);
    }

    private static RoslynXamlProjectDeltaMode GetMode(
        bool metadataChanged,
        bool xamlChanged)
    {
        if (metadataChanged && xamlChanged)
            return RoslynXamlProjectDeltaMode
                .MetadataAndXaml;
        if (metadataChanged)
            return RoslynXamlProjectDeltaMode
                .MetadataOnly;
        return xamlChanged
            ? RoslynXamlProjectDeltaMode.XamlOnly
            : RoslynXamlProjectDeltaMode.None;
    }

    private static bool IsTargetChanged(
        RoslynXamlProjectPreview previous,
        RoslynXamlProjectPreview current,
        ImmutableArray<
            RoslynXamlProjectDocumentDelta> documents)
    {
        if (!string.Equals(
                previous.ResourceUri,
                current.ResourceUri,
                StringComparison.OrdinalIgnoreCase))
            return true;
        return documents.Any(
            document =>
                string.Equals(
                    document.ResourceUri,
                    current.ResourceUri,
                    StringComparison.OrdinalIgnoreCase) &&
                document.HasSemanticChange);
    }

    private static bool IsTargetDependencyChanged(
        RoslynXamlProjectPreview previous,
        RoslynXamlProjectPreview current,
        ImmutableArray<
            RoslynXamlProjectDocumentDelta> documents)
    {
        if (!previous.ResourceDependencies.Equals(
                current.ResourceDependencies))
            return true;
        var providerPaths =
            new HashSet<string>(
                previous.ResourceDependencies.ProviderPaths
                    .Concat(
                        current.ResourceDependencies.ProviderPaths),
                StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (!document.HasSemanticChange)
                continue;
            var path =
                document.Current?.ResourceDependencies.DocumentPath ??
                document.Previous?.ResourceDependencies.DocumentPath;
            if (path != null &&
                providerPaths.Contains(path))
                return true;
        }

        return false;
    }

    private static ImmutableArray<
        RoslynXamlMetadataDeltaReason>
        GetMetadataDeltaReasons(
        CSharpCompilation previous,
        CSharpCompilation current,
        CancellationToken cancellationToken)
    {
        var reasons =
            ImmutableArray.CreateBuilder<
                RoslynXamlMetadataDeltaReason>();
        if (!string.Equals(
                CompilationOptionsShape(
                    previous.Options),
                CompilationOptionsShape(
                    current.Options),
                StringComparison.Ordinal))
        {
            reasons.Add(
                RoslynXamlMetadataDeltaReason
                    .CompilationOptions);
        }
        if (!ReferencesEquivalent(
                previous.References,
                current.References))
        {
            reasons.Add(
                RoslynXamlMetadataDeltaReason
                    .MetadataReferences);
        }
        if (!HostTreeShapes(
                previous,
                cancellationToken)
            .SequenceEqual(
                HostTreeShapes(
                    current,
                    cancellationToken),
                StringComparer.Ordinal))
        {
            reasons.Add(
                RoslynXamlMetadataDeltaReason
                    .HostSyntaxTrees);
        }

        return reasons.ToImmutable();
    }

    private static string CompilationOptionsShape(
        CSharpCompilationOptions options) =>
        options.OutputKind +
        "\0" +
        options.ModuleName +
        "\0" +
        options.MainTypeName +
        "\0" +
        options.ScriptClassName +
        "\0" +
        string.Join(",", options.Usings) +
        "\0" +
        options.OptimizationLevel +
        "\0" +
        options.CheckOverflow +
        "\0" +
        options.AllowUnsafe +
        "\0" +
        options.Platform +
        "\0" +
        options.GeneralDiagnosticOption +
        "\0" +
        options.WarningLevel.ToString(
            CultureInfo.InvariantCulture) +
        "\0" +
        string.Join(
            ",",
            options.SpecificDiagnosticOptions
                .OrderBy(
                    static pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(
                    static pair =>
                        pair.Key +
                        "=" +
                        pair.Value)) +
        "\0" +
        options.ConcurrentBuild +
        "\0" +
        options.Deterministic +
        "\0" +
        options.MetadataImportOptions +
        "\0" +
        options.NullableContextOptions +
        "\0" +
        options.PublicSign +
        "\0" +
        options.CryptoKeyFile +
        "\0" +
        options.CryptoKeyContainer +
        "\0" +
        Convert.ToBase64String(
            options.CryptoPublicKey.ToArray()) +
        "\0" +
        options.DelaySign +
        "\0" +
        options.ReportSuppressedDiagnostics +
        "\0" +
        options.MetadataReferenceResolver?
            .GetType().FullName +
        "\0" +
        options.XmlReferenceResolver?
            .GetType().FullName +
        "\0" +
        options.SourceReferenceResolver?
            .GetType().FullName +
        "\0" +
        options.StrongNameProvider?
            .GetType().FullName +
        "\0" +
        options.AssemblyIdentityComparer?
            .GetType().FullName +
        "\0" +
        options.SyntaxTreeOptionsProvider?
            .GetType().FullName;

    private static string[] HostTreeShapes(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RoslynXamlHostCompilation
                    .IsGeneratedXamlTree(tree) ||
                tree.FilePath?.EndsWith(
                    ".PreviewHost.g.cs",
                    StringComparison.OrdinalIgnoreCase) ==
                true)
                continue;
            result.Add(
                NormalizeTreePath(tree.FilePath) +
                "\0" +
                ParseOptionsShape(tree.Options) +
                "\0" +
                RoslynXamlSourceChecksum.ComputeHex(
                    tree.GetText(
                        cancellationToken)));
        }

        result.Sort(StringComparer.Ordinal);
        return result.ToArray();
    }

    private static string NormalizeTreePath(
        string? path) =>
        (path ?? string.Empty)
            .Replace('\\', '/');

    private static string ParseOptionsShape(
        ParseOptions options)
    {
        if (options is not CSharpParseOptions csharp)
            return options.ToString();
        return csharp.LanguageVersion +
               "\0" +
               csharp.SpecifiedLanguageVersion +
               "\0" +
               csharp.DocumentationMode +
               "\0" +
               csharp.Kind +
               "\0" +
               string.Join(
                   ",",
                   csharp.PreprocessorSymbolNames
                       .OrderBy(
                           static value => value,
                           StringComparer.Ordinal)) +
               "\0" +
               string.Join(
                   ",",
                   csharp.Features
                       .OrderBy(
                           static pair => pair.Key,
                           StringComparer.Ordinal)
                       .Select(
                           static pair =>
                               pair.Key +
                               "=" +
                               pair.Value));
    }

    private static bool ReferencesEquivalent(
        IEnumerable<MetadataReference> previous,
        IEnumerable<MetadataReference> current)
    {
        var oldReferences = previous
            .Select(ReferenceShape)
            .OrderBy(
                static value => value,
                StringComparer.Ordinal);
        var newReferences = current
            .Select(ReferenceShape)
            .OrderBy(
                static value => value,
                StringComparer.Ordinal);
        return oldReferences.SequenceEqual(
            newReferences,
            StringComparer.Ordinal);
    }

    private static string ReferenceShape(
        MetadataReference reference) =>
        reference.Display +
        "\0" +
        reference.Properties.Kind +
        "\0" +
        reference.Properties.EmbedInteropTypes +
        "\0" +
        string.Join(
            ",",
            reference.Properties.Aliases);

    private static string GetMaterializationFailure(
        RoslynXamlProjectPreview current)
    {
        if (!string.IsNullOrWhiteSpace(
                current.MaterializationError))
            return current.MaterializationError!;
        var error = current.Artifact?.Diagnostics
            .FirstOrDefault(
                static diagnostic =>
                    diagnostic.Severity ==
                    DiagnosticSeverity.Error);
        return error?.GetMessage(
                   CultureInfo.InvariantCulture) ??
               "the current project artifact is unavailable";
    }

    private readonly struct ProjectionIdentity :
        IEquatable<ProjectionIdentity>
    {
        public ProjectionIdentity(
            ulong stableNodeId,
            XamlProjectionKind kind,
            string? memberId)
        {
            StableNodeId = stableNodeId;
            Kind = kind;
            MemberId = memberId;
        }

        public ulong StableNodeId { get; }
        public XamlProjectionKind Kind { get; }
        public string? MemberId { get; }

        public bool Equals(
            ProjectionIdentity other) =>
            StableNodeId == other.StableNodeId &&
            Kind == other.Kind &&
            string.Equals(
                MemberId,
                other.MemberId,
                StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is ProjectionIdentity other &&
            Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StableNodeId.GetHashCode();
                hash = (hash * 397) ^
                       Kind.GetHashCode();
                return (hash * 397) ^
                       (MemberId == null
                           ? 0
                           : StringComparer.Ordinal
                               .GetHashCode(MemberId));
            }
        }
    }

    private struct Fingerprint64
    {
        private ulong _value;

        public ulong Value =>
            _value == 0
                ? 14695981039346656037UL
                : _value;

        public void Add(int value)
        {
            Add(value.ToString(
                CultureInfo.InvariantCulture));
        }

        public void Add(string? value)
        {
            var hash = Value;
            if (value == null)
            {
                hash ^= 0xff;
                hash *= 1099511628211UL;
            }
            else
            {
                foreach (var character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
            }

            hash ^= 0;
            hash *= 1099511628211UL;
            _value = hash;
        }
    }
}
