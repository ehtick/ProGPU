using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Creates structured load/save calls from a validated deferred-loader descriptor. The
/// framework supplies the reader and service-provider expressions; no loader is discovered
/// through reflection and no generated C# is parsed back into syntax.
/// </summary>
public static class RoslynXamlDeferringLoaderSyntaxFactory
{
    public static ExpressionSyntax CreateLoadExpression(
        XamlDeferringLoaderShapeInfo shape,
        ExpressionSyntax reader,
        ExpressionSyntax serviceProvider)
    {
        Validate(shape, reader, serviceProvider);
        return SyntaxFactory.CastExpression(
            RoslynTypeSyntaxFactory.Create(shape.ContentType!),
            CreateInvocation(
                shape,
                shape.LoadMethod!,
                reader,
                serviceProvider));
    }

    public static ExpressionSyntax CreateSaveExpression(
        XamlDeferringLoaderShapeInfo shape,
        ExpressionSyntax content,
        ExpressionSyntax serviceProvider)
    {
        Validate(shape, content, serviceProvider);
        return CreateInvocation(
            shape,
            shape.SaveMethod!,
            content,
            serviceProvider);
    }

    private static void Validate(
        XamlDeferringLoaderShapeInfo shape,
        ExpressionSyntax value,
        ExpressionSyntax serviceProvider)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));
        if (!shape.IsValid)
            throw new ArgumentException(
                "A valid deferred-loader shape is required.",
                nameof(shape));
    }

    private static ExpressionSyntax CreateInvocation(
        XamlDeferringLoaderShapeInfo shape,
        IMethodSymbol method,
        ExpressionSyntax value,
        ExpressionSyntax serviceProvider)
    {
        var loader = SyntaxFactory.ObjectCreationExpression(
                RoslynTypeSyntaxFactory.Create(shape.Constructor!.ContainingType))
            .WithArgumentList(SyntaxFactory.ArgumentList());
        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    loader,
                    SyntaxFactory.IdentifierName(method.Name)))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument(
                            SyntaxFactory.CastExpression(
                                RoslynTypeSyntaxFactory.Create(
                                    method.Parameters[0].Type),
                                value)),
                        SyntaxFactory.Argument(
                            SyntaxFactory.CastExpression(
                                RoslynTypeSyntaxFactory.Create(
                                    method.Parameters[1].Type),
                                serviceProvider))
                    })));
    }
}
