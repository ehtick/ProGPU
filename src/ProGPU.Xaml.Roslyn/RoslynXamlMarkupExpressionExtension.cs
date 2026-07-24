using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Lowering;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

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
