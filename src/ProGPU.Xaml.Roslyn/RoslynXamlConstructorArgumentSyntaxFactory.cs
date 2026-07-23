using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Builds a structured save-path construction expression from validated
/// ConstructorArgumentAttribute evidence. No source text is parsed.
/// </summary>
public static class RoslynXamlConstructorArgumentSyntaxFactory
{
    public static ObjectCreationExpressionSyntax CreateObjectCreationExpression(
        XamlConstructorArgumentShapeInfo shape,
        ExpressionSyntax propertyValue)
    {
        if (shape == null) throw new ArgumentNullException(nameof(shape));
        if (propertyValue == null) throw new ArgumentNullException(nameof(propertyValue));
        if (!shape.IsValid || shape.Constructor == null || shape.Parameter == null)
            throw new ArgumentException(
                "A valid constructor-argument shape is required.",
                nameof(shape));

        var argument = SyntaxFactory.Argument(
            SyntaxFactory.CastExpression(
                RoslynTypeSyntaxFactory.Create(shape.Parameter.Type),
                propertyValue));
        return SyntaxFactory.ObjectCreationExpression(
                RoslynTypeSyntaxFactory.Create(shape.Constructor.ContainingType))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(argument)));
    }
}
