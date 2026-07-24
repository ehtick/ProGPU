using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ProGPU.Xaml.Schema;

/// <summary>
/// Framework-neutral compilation decision projected from host configuration or a
/// framework annotation. Unknown numeric flag combinations are retained as invalid
/// evidence rather than silently interpreted.
/// </summary>
public enum XamlCompilationMode
{
    Compile,
    Skip
}

public enum XamlCompilationScope
{
    Assembly,
    Module,
    Type
}

public sealed class XamlCompilationModeInfo
{
    public XamlCompilationModeInfo(
        XamlSchemaAnnotationInfo annotation,
        XamlCompilationScope scope,
        XamlCompilationMode? mode,
        long? rawValue,
        TypedConstant? valueConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Scope = scope;
        Mode = mode;
        RawValue = rawValue;
        ValueConstant = valueConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public XamlCompilationScope Scope { get; }
    public XamlCompilationMode? Mode { get; }
    public long? RawValue { get; }
    public TypedConstant? ValueConstant { get; }
    public ISymbol DeclaredOn => Annotation.DeclaredOn;
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Mode.HasValue && Error == null;
    public string? Error { get; }
}

public sealed class XamlRootNamespaceInfo
{
    public XamlRootNamespaceInfo(
        XamlSchemaAnnotationInfo annotation,
        IAssemblySymbol? assembly,
        string? namespaceName,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Assembly = assembly;
        Namespace = namespaceName;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public IAssemblySymbol? Assembly { get; }
    public string? Namespace { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Assembly != null &&
        !string.IsNullOrWhiteSpace(Namespace) &&
        Error == null;
    public string? Error { get; }
}

public sealed class XamlFilePathInfo
{
    public XamlFilePathInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? associatedType,
        string? filePath,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        AssociatedType = associatedType;
        FilePath = filePath;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? AssociatedType { get; }
    public string? FilePath { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => AssociatedType != null &&
        !string.IsNullOrWhiteSpace(FilePath) &&
        Error == null;
    public string? Error { get; }
}

public sealed class XamlResourceIdInfo
{
    public XamlResourceIdInfo(
        XamlSchemaAnnotationInfo annotation,
        IAssemblySymbol? assembly,
        string? resourceId,
        string? path,
        INamedTypeSymbol? associatedType,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Assembly = assembly;
        ResourceId = resourceId;
        Path = path;
        AssociatedType = associatedType;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public IAssemblySymbol? Assembly { get; }
    public string? ResourceId { get; }
    public string? Path { get; }
    public INamedTypeSymbol? AssociatedType { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Assembly != null &&
        !string.IsNullOrWhiteSpace(ResourceId) &&
        !string.IsNullOrWhiteSpace(Path) &&
        AssociatedType != null &&
        Error == null;
    public string? Error { get; }
}

/// <summary>Canonical evidence for a marker annotation on a XAML-visible type.</summary>
public sealed class XamlTypeMarkerInfo
{
    public XamlTypeMarkerInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? type,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Type = type;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? Type { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Type != null && Error == null;
    public string? Error { get; }
}

public enum XamlBuildMetadataIssueKind
{
    CompilationMode,
    FilePath,
    ResourceIdentity,
    RootNamespace
}

public sealed class XamlBuildMetadataIssue
{
    public XamlBuildMetadataIssue(
        XamlBuildMetadataIssueKind kind,
        string message)
    {
        Kind = kind;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public XamlBuildMetadataIssueKind Kind { get; }
    public string Message { get; }
}

/// <summary>
/// Immutable per-document build identity. The effective class name is the exact Roslyn
/// metadata name when a symbol is available and otherwise the deterministic root-namespace
/// projection used by markup compilers.
/// </summary>
public sealed class XamlDocumentBuildMetadata
{
    public XamlDocumentBuildMetadata(
        string documentPath,
        string? requestedClassName,
        string? effectiveClassName,
        INamedTypeSymbol? classType,
        XamlCompilationModeInfo? compilationMode,
        XamlFilePathInfo? filePath,
        XamlResourceIdInfo? resourceIdentity,
        XamlRootNamespaceInfo? rootNamespace,
        IReadOnlyList<XamlBuildMetadataIssue>? issues = null)
    {
        DocumentPath = documentPath ?? throw new ArgumentNullException(nameof(documentPath));
        RequestedClassName = requestedClassName;
        EffectiveClassName = effectiveClassName;
        ClassType = classType;
        CompilationMode = compilationMode;
        FilePath = filePath;
        ResourceIdentity = resourceIdentity;
        RootNamespace = rootNamespace;
        Issues = issues ?? Array.Empty<XamlBuildMetadataIssue>();
        var errors = new string[Issues.Count];
        for (var index = 0; index < errors.Length; index++)
            errors[index] = Issues[index].Message;
        Errors = errors;
    }

    public string DocumentPath { get; }
    public string? RequestedClassName { get; }
    public string? EffectiveClassName { get; }
    public INamedTypeSymbol? ClassType { get; }
    public XamlCompilationModeInfo? CompilationMode { get; }
    public XamlFilePathInfo? FilePath { get; }
    public XamlResourceIdInfo? ResourceIdentity { get; }
    public XamlRootNamespaceInfo? RootNamespace { get; }
    public IReadOnlyList<XamlBuildMetadataIssue> Issues { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool ShouldCompile =>
        CompilationMode?.Mode != XamlCompilationMode.Skip;
}

/// <summary>
/// Optional compiler/build metadata service exposed by Roslyn-backed type systems.
/// Hosts can use it without depending on any specific UI framework assembly.
/// </summary>
public interface IXamlBuildMetadataResolver
{
    IReadOnlyList<XamlCompilationModeInfo> AssemblyCompilationModes { get; }
    IReadOnlyList<XamlCompilationModeInfo> ModuleCompilationModes { get; }
    IReadOnlyList<XamlRootNamespaceInfo> RootNamespaces { get; }
    IReadOnlyList<XamlResourceIdInfo> ResourceIdentities { get; }

    XamlDocumentBuildMetadata ResolveDocumentBuildMetadata(
        string documentPath,
        string? className);
}
