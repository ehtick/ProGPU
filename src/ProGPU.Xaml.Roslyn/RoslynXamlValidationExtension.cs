using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Binding;
using ProGPU.Xaml.Resources;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Optional semantic validation seam over the canonical enriched bound document and
/// framework-neutral resource graph.
/// </summary>
public interface IRoslynXamlBoundDocumentValidatorExtension : IRoslynXamlExtension
{
    IEnumerable<RoslynXamlValidationIssue> Validate(
        RoslynXamlBoundDocumentValidationContext context);
}

public sealed class RoslynXamlBoundDocumentValidationContext
{
    public RoslynXamlBoundDocumentValidationContext(
        XamlBoundDocument document,
        XamlResourceGraph resourceGraph,
        IXamlTypeSystem typeSystem,
        string frameworkId,
        string? resourceUri,
        bool strict,
        CancellationToken cancellationToken = default)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        ResourceGraph = resourceGraph ?? throw new ArgumentNullException(nameof(resourceGraph));
        TypeSystem = typeSystem ?? throw new ArgumentNullException(nameof(typeSystem));
        FrameworkId = string.IsNullOrWhiteSpace(frameworkId)
            ? throw new ArgumentException(
                "A framework ID is required.",
                nameof(frameworkId))
            : frameworkId;
        ResourceUri = resourceUri;
        Strict = strict;
        CancellationToken = cancellationToken;
    }

    public XamlBoundDocument Document { get; }
    public XamlResourceGraph ResourceGraph { get; }
    public IXamlTypeSystem TypeSystem { get; }
    public string FrameworkId { get; }
    public string? ResourceUri { get; }
    public bool Strict { get; }
    public CancellationToken CancellationToken { get; }
}

public sealed class RoslynXamlValidationIssue
{
    public RoslynXamlValidationIssue(
        string id,
        DiagnosticSeverity severity,
        string message,
        TextSpan sourceSpan,
        string? specificationSection = null)
    {
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException(
                "A validation diagnostic ID is required.",
                nameof(id))
            : id;
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        SourceSpan = sourceSpan;
        SpecificationSection = specificationSection;
    }

    public string Id { get; }
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public TextSpan SourceSpan { get; }
    public string? SpecificationSection { get; }
}
