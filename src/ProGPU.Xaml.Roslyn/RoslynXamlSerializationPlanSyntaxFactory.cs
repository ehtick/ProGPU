using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Serialization;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Structured Roslyn projections for a validated whole-object serialization plan.
/// Runtime value acquisition and inaccessible-method bridging remain host/profile policy.
/// </summary>
public static class RoslynXamlSerializationPlanSyntaxFactory
{
    public static ObjectCreationExpressionSyntax CreateConstructorExpression(
        XamlObjectSerializationPlan plan,
        Func<XamlMemberSerializationPlan, ExpressionSyntax> getMemberValue)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (getMemberValue == null)
            throw new ArgumentNullException(nameof(getMemberValue));
        if (!plan.IsValid || !plan.UsesConstructorArgument)
            throw new ArgumentException(
                "A valid constructor-argument object plan is required.",
                nameof(plan));
        var member = plan.Members.Single(candidate =>
            candidate.Disposition ==
            XamlSerializationDisposition.ConstructorArgument);
        return RoslynXamlConstructorArgumentSyntaxFactory
            .CreateObjectCreationExpression(
                member.ConstructorArgument!,
                getMemberValue(member));
    }

    public static InvocationExpressionSyntax CreateShouldSerializeInvocation(
        XamlMemberSerializationPlan member,
        ExpressionSyntax instance)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        var method = member.ShouldSerializeMethod;
        ValidateDirectCallable(method, "should-serialize", member);
        return CreateInstanceInvocation(instance, method!);
    }

    public static InvocationExpressionSyntax CreateResetInvocation(
        XamlMemberSerializationPlan member,
        ExpressionSyntax instance)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        var method = member.ResetMethod;
        ValidateDirectCallable(method, "reset", member);
        return CreateInstanceInvocation(instance, method!);
    }

    private static void ValidateDirectCallable(
        IMethodSymbol? method,
        string role,
        XamlMemberSerializationPlan member)
    {
        if (method == null)
            throw new ArgumentException(
                $"The member plan has no {role} callable.",
                nameof(member));
        if (method.DeclaredAccessibility != Accessibility.Public)
            throw new ArgumentException(
                $"The selected {role} callable is not directly accessible; a typed framework bridge is required.",
                nameof(member));
    }

    private static InvocationExpressionSyntax CreateInstanceInvocation(
        ExpressionSyntax instance,
        IMethodSymbol method) =>
        SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.ParenthesizedExpression(
                    SyntaxFactory.CastExpression(
                        RoslynTypeSyntaxFactory.Create(method.ContainingType),
                        instance)),
                SyntaxFactory.IdentifierName(method.Name)),
            SyntaxFactory.ArgumentList());
}
