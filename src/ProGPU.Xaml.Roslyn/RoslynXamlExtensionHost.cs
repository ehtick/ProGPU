using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Binding;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Lowering;
using ProGPU.Xaml.Resources;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

[Flags]
public enum RoslynXamlExtensionCapabilities
{
    None = 0,
    MarkupExtensionExpression = 1 << 0,
    BoundDocumentValidation = 1 << 1
}

public enum RoslynXamlExtensionConflictPolicy
{
    Diagnose,
    CoalesceEquivalent
}

public interface IRoslynXamlExtension
{
    string Id { get; }
    int ContractVersion { get; }
    int Version { get; }
    int Priority { get; }
    RoslynXamlExtensionCapabilities Capabilities { get; }
    RoslynXamlExtensionConflictPolicy ConflictPolicy { get; }
}

/// <summary>
/// Optional user/framework-package lowering seam for a canonical markup-extension IR value.
/// Implementations must construct C# exclusively as Roslyn syntax nodes.
/// </summary>
public interface IRoslynXamlMarkupExtensionExpressionExtension : IRoslynXamlExtension
{
    bool TryCreateExpression(
        RoslynXamlMarkupExtensionExpressionContext context,
        out ExpressionSyntax expression);
}

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

public sealed class RoslynXamlMarkupExtensionExpressionContext
{
    public RoslynXamlMarkupExtensionExpressionContext(
        XamlIrObject extension,
        XamlTypeInfo targetType,
        ExpressionSyntax lookupRoot,
        ExpressionSyntax? targetObject,
        XamlMemberInfo? targetMember,
        string? resourceUri,
        CancellationToken cancellationToken = default)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        LookupRoot = lookupRoot ?? throw new ArgumentNullException(nameof(lookupRoot));
        TargetObject = targetObject;
        TargetMember = targetMember;
        ResourceUri = resourceUri;
        CancellationToken = cancellationToken;
    }

    public XamlIrObject Extension { get; }
    public XamlTypeInfo TargetType { get; }
    public ExpressionSyntax LookupRoot { get; }
    public ExpressionSyntax? TargetObject { get; }
    public XamlMemberInfo? TargetMember { get; }
    public string? ResourceUri { get; }
    public CancellationToken CancellationToken { get; }
}

public enum RoslynXamlExtensionResolutionKind
{
    NotHandled,
    Handled,
    Conflict,
    Error
}

public sealed class RoslynXamlExtensionResolution
{
    internal RoslynXamlExtensionResolution(
        RoslynXamlExtensionResolutionKind kind,
        ExpressionSyntax? expression,
        ImmutableArray<string> pluginIds,
        string? message)
    {
        Kind = kind;
        Expression = expression;
        PluginIds = pluginIds;
        Message = message;
    }

    public RoslynXamlExtensionResolutionKind Kind { get; }
    public ExpressionSyntax? Expression { get; }
    public ImmutableArray<string> PluginIds { get; }
    public string? Message { get; }
}

/// <summary>
/// Immutable deterministic host for Roslyn-side compiler extensions. Metadata is snapshotted
/// at registration; handlers are called only for their declared capabilities.
/// </summary>
public sealed class RoslynXamlExtensionHost
{
    public const int CurrentContractVersion = 1;

    private readonly ImmutableArray<IRoslynXamlExtension> _extensions;

    private RoslynXamlExtensionHost(ImmutableArray<IRoslynXamlExtension> extensions)
    {
        _extensions = extensions;
    }

    public static RoslynXamlExtensionHost Empty { get; } =
        Create(Array.Empty<IRoslynXamlExtension>());

    public int ContractVersion => CurrentContractVersion;
    public ImmutableArray<IRoslynXamlExtension> Extensions => _extensions;

    public static RoslynXamlExtensionHost Create(
        IEnumerable<IRoslynXamlExtension> extensions)
    {
        if (extensions == null) throw new ArgumentNullException(nameof(extensions));
        var ordered = new List<IRoslynXamlExtension>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions)
        {
            if (extension == null)
                throw new ArgumentException("A Roslyn XAML extension cannot be null.", nameof(extensions));
            Validate(extension);
            if (!ids.Add(extension.Id))
                throw new ArgumentException(
                    $"Roslyn XAML extension ID '{extension.Id}' is registered more than once.",
                    nameof(extensions));
            ordered.Add(new RegisteredExtension(extension));
        }

        ordered.Sort(static (left, right) =>
        {
            var priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0) return priority;
            var id = StringComparer.Ordinal.Compare(left.Id, right.Id);
            return id != 0 ? id : right.Version.CompareTo(left.Version);
        });
        return new RoslynXamlExtensionHost(ordered.ToImmutableArray());
    }

    public RoslynXamlExtensionResolution ResolveMarkupExtensionExpression(
        RoslynXamlMarkupExtensionExpressionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        IRoslynXamlExtension? winner = null;
        ExpressionSyntax? winningExpression = null;
        var matchingIds = ImmutableArray.CreateBuilder<string>();

        foreach (var extension in _extensions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (winner != null && extension.Priority < winner.Priority)
                break;
            if ((extension.Capabilities &
                 RoslynXamlExtensionCapabilities.MarkupExtensionExpression) == 0)
                continue;
            if (extension is not IRoslynXamlMarkupExtensionExpressionExtension expressionExtension)
                return Error(
                    extension.Id,
                    $"Roslyn XAML extension '{extension.Id}' declares markup-expression " +
                    "capability without implementing its contract.");

            ExpressionSyntax expression;
            bool handled;
            try
            {
                handled = expressionExtension.TryCreateExpression(context, out expression!);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Error(
                    extension.Id,
                    $"Roslyn XAML extension '{extension.Id}' failed: {exception.Message}");
            }

            if (!handled) continue;
            if (expression == null)
                return Error(
                    extension.Id,
                    $"Roslyn XAML extension '{extension.Id}' returned a null expression.");

            matchingIds.Add(extension.Id);
            if (winner == null)
            {
                winner = extension;
                winningExpression = expression;
                continue;
            }

            var canCoalesce =
                winner.ConflictPolicy == RoslynXamlExtensionConflictPolicy.CoalesceEquivalent &&
                extension.ConflictPolicy == RoslynXamlExtensionConflictPolicy.CoalesceEquivalent &&
                winningExpression!.IsEquivalentTo(expression);
            if (!canCoalesce)
            {
                return new RoslynXamlExtensionResolution(
                    RoslynXamlExtensionResolutionKind.Conflict,
                    null,
                    matchingIds.ToImmutable(),
                    $"Roslyn XAML extensions '{winner.Id}' and '{extension.Id}' both handled " +
                    $"the markup expression at priority {winner.Priority}.");
            }
        }

        return winner == null
            ? new RoslynXamlExtensionResolution(
                RoslynXamlExtensionResolutionKind.NotHandled,
                null,
                ImmutableArray<string>.Empty,
                null)
            : new RoslynXamlExtensionResolution(
                RoslynXamlExtensionResolutionKind.Handled,
                winningExpression,
                matchingIds.ToImmutable(),
                null);
    }

    public ImmutableArray<Diagnostic> ValidateBoundDocument(
        RoslynXamlBoundDocumentValidationContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var extension in _extensions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if ((extension.Capabilities &
                 RoslynXamlExtensionCapabilities.BoundDocumentValidation) == 0)
                continue;
            if (extension is not IRoslynXamlBoundDocumentValidatorExtension validator)
            {
                diagnostics.Add(CreateValidationHostDiagnostic(
                    context,
                    "PGXAML2133",
                    $"Roslyn XAML extension '{extension.Id}' declares bound-document " +
                    "validation capability without implementing its contract."));
                continue;
            }

            try
            {
                var issues = validator.Validate(context);
                if (issues == null)
                {
                    diagnostics.Add(CreateValidationHostDiagnostic(
                        context,
                        "PGXAML2134",
                        $"Roslyn XAML extension '{extension.Id}' returned a null validation sequence."));
                    continue;
                }

                foreach (var issue in issues)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (issue == null ||
                        issue.SourceSpan.Start < 0 ||
                        issue.SourceSpan.End > context.Document.Infoset.SourceText.Length)
                    {
                        diagnostics.Add(CreateValidationHostDiagnostic(
                            context,
                            "PGXAML2134",
                            $"Roslyn XAML extension '{extension.Id}' returned an invalid validation issue."));
                        continue;
                    }

                    diagnostics.Add(XamlDiagnostics.Create(
                        issue.Id,
                        issue.Severity,
                        issue.Message,
                        context.Document.Infoset.Path,
                        context.Document.Infoset.SourceText,
                        issue.SourceSpan,
                        issue.SpecificationSection));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(CreateValidationHostDiagnostic(
                    context,
                    "PGXAML2133",
                    $"Roslyn XAML extension '{extension.Id}' failed during bound-document " +
                    $"validation: {exception.Message}"));
            }
        }
        return diagnostics.ToImmutable();
    }

    private static RoslynXamlExtensionResolution Error(string id, string message) =>
        new RoslynXamlExtensionResolution(
            RoslynXamlExtensionResolutionKind.Error,
            null,
            ImmutableArray.Create(id),
            message);

    private static Diagnostic CreateValidationHostDiagnostic(
        RoslynXamlBoundDocumentValidationContext context,
        string id,
        string message)
    {
        var span = context.Document.Root?.SourceSpan ?? default;
        return XamlDiagnostics.Create(
            id,
            DiagnosticSeverity.Error,
            message,
            context.Document.Infoset.Path,
            context.Document.Infoset.SourceText,
            span,
            "EXT-004");
    }

    private static void Validate(IRoslynXamlExtension extension)
    {
        if (string.IsNullOrWhiteSpace(extension.Id))
            throw new ArgumentException("A Roslyn XAML extension requires a non-empty ID.", nameof(extension));
        if (extension.ContractVersion != CurrentContractVersion)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' requires contract version " +
                $"{extension.ContractVersion}; this host supports {CurrentContractVersion}.",
                nameof(extension));
        if (extension.Version <= 0)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' requires a positive implementation version.",
                nameof(extension));
        if (extension.Capabilities == RoslynXamlExtensionCapabilities.None)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' does not declare a capability.",
                nameof(extension));
        if ((extension.Capabilities &
             ~(RoslynXamlExtensionCapabilities.MarkupExtensionExpression |
               RoslynXamlExtensionCapabilities.BoundDocumentValidation)) != 0)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' declares unsupported capabilities.",
                nameof(extension));
        if ((extension.Capabilities &
             RoslynXamlExtensionCapabilities.MarkupExtensionExpression) != 0 &&
            extension is not IRoslynXamlMarkupExtensionExpressionExtension)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' declares markup-expression " +
                "capability without implementing its contract.",
                nameof(extension));
        if ((extension.Capabilities &
             RoslynXamlExtensionCapabilities.BoundDocumentValidation) != 0 &&
            extension is not IRoslynXamlBoundDocumentValidatorExtension)
            throw new ArgumentException(
                $"Roslyn XAML extension '{extension.Id}' declares bound-document validation " +
                "capability without implementing its contract.",
                nameof(extension));
    }

    private sealed class RegisteredExtension :
        IRoslynXamlMarkupExtensionExpressionExtension,
        IRoslynXamlBoundDocumentValidatorExtension
    {
        private readonly IRoslynXamlExtension _extension;

        public RegisteredExtension(IRoslynXamlExtension extension)
        {
            _extension = extension;
            Id = extension.Id;
            ContractVersion = extension.ContractVersion;
            Version = extension.Version;
            Priority = extension.Priority;
            Capabilities = extension.Capabilities;
            ConflictPolicy = extension.ConflictPolicy;
        }

        public string Id { get; }
        public int ContractVersion { get; }
        public int Version { get; }
        public int Priority { get; }
        public RoslynXamlExtensionCapabilities Capabilities { get; }
        public RoslynXamlExtensionConflictPolicy ConflictPolicy { get; }

        public bool TryCreateExpression(
            RoslynXamlMarkupExtensionExpressionContext context,
            out ExpressionSyntax expression)
            => ((IRoslynXamlMarkupExtensionExpressionExtension)_extension)
                .TryCreateExpression(context, out expression!);

        public IEnumerable<RoslynXamlValidationIssue> Validate(
            RoslynXamlBoundDocumentValidationContext context) =>
            ((IRoslynXamlBoundDocumentValidatorExtension)_extension)
                .Validate(context);
    }
}
