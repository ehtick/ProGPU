using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Builds structured save-path calls from a validated value-serializer descriptor.
/// The caller owns the serializer context expression and runtime invocation policy.
/// </summary>
public static class RoslynXamlValueSerializerSyntaxFactory
{
    public static ExpressionSyntax CreateCanConvertToStringExpression(
        XamlValueSerializerShapeInfo shape,
        ExpressionSyntax value,
        ExpressionSyntax context)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!shape.IsValid || shape.IsSuppressed || shape.CanConvertToStringMethod == null)
            throw new ArgumentException("A valid, non-suppressed value-serializer shape is required.", nameof(shape));
        return CreateInvocation(shape, shape.CanConvertToStringMethod, value, context);
    }

    public static ExpressionSyntax CreateConvertToStringExpression(
        XamlValueSerializerShapeInfo shape,
        ExpressionSyntax value,
        ExpressionSyntax context)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!shape.IsValid || shape.IsSuppressed || shape.ConvertToStringMethod == null)
            throw new ArgumentException("A valid, non-suppressed value-serializer shape is required.", nameof(shape));
        return CreateInvocation(shape, shape.ConvertToStringMethod, value, context);
    }

    private static ExpressionSyntax CreateInvocation(
        XamlValueSerializerShapeInfo shape,
        Microsoft.CodeAnalysis.IMethodSymbol method,
        ExpressionSyntax value,
        ExpressionSyntax context)
    {
        var serializer = SyntaxFactory.ObjectCreationExpression(
                RoslynTypeSyntaxFactory.Create(shape.Constructor!.ContainingType))
            .WithArgumentList(SyntaxFactory.ArgumentList());
        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    serializer,
                    SyntaxFactory.IdentifierName(method.Name)))
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(SyntaxFactory.CastExpression(
                        RoslynTypeSyntaxFactory.Create(method.Parameters[0].Type),
                        value)),
                    SyntaxFactory.Argument(SyntaxFactory.CastExpression(
                        RoslynTypeSyntaxFactory.Create(method.Parameters[1].Type),
                        context))
                })));
    }
}
