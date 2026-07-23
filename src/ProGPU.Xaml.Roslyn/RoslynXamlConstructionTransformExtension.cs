using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using ProGPU.Xaml.Lowering;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Optional ordered transform over canonical construction IR. The host preserves the bound
/// document, resource graph, diagnostics, and compilation-unit root identity.
/// </summary>
public interface IRoslynXamlConstructionProgramTransformExtension : IRoslynXamlExtension
{
    RoslynXamlConstructionTransformResult Transform(
        RoslynXamlConstructionTransformContext context);
}

public sealed class RoslynXamlConstructionTransformContext
{
    public RoslynXamlConstructionTransformContext(
        XamlConstructionProgram program,
        string frameworkId,
        string? resourceUri,
        bool strict,
        CancellationToken cancellationToken = default)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        FrameworkId = string.IsNullOrWhiteSpace(frameworkId)
            ? throw new ArgumentException(
                "A framework ID is required.",
                nameof(frameworkId))
            : frameworkId;
        ResourceUri = resourceUri;
        Strict = strict;
        CancellationToken = cancellationToken;
    }

    public XamlConstructionProgram Program { get; }
    public string FrameworkId { get; }
    public string? ResourceUri { get; }
    public bool Strict { get; }
    public CancellationToken CancellationToken { get; }
}

public sealed class RoslynXamlConstructionTransformResult
{
    public RoslynXamlConstructionTransformResult(
        XamlIrObject? root,
        IEnumerable<RoslynXamlValidationIssue>? issues = null)
    {
        Root = root;
        Issues = issues == null
            ? ImmutableArray<RoslynXamlValidationIssue>.Empty
            : issues.ToImmutableArray();
    }

    public XamlIrObject? Root { get; }
    public ImmutableArray<RoslynXamlValidationIssue> Issues { get; }
}
