using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProGPU.Xaml.Roslyn;

public static class RoslynTypeSyntaxFactory
{
    public static TypeSyntax Create(ITypeSymbol symbol)
    {
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        TypeSyntax syntax;
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                syntax = SyntaxFactory.ArrayType(Create(array.ElementType),
                    SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                        SyntaxFactory.SeparatedList<ExpressionSyntax>(CreateOmittedSizes(array.Rank)))));
                break;
            case IPointerTypeSymbol pointer:
                syntax = SyntaxFactory.PointerType(Create(pointer.PointedAtType));
                break;
            case INamedTypeSymbol named:
                syntax = CreateNamed(named);
                break;
            case IDynamicTypeSymbol:
                syntax = SyntaxFactory.IdentifierName("dynamic");
                break;
            default:
                syntax = SyntaxFactory.IdentifierName(Escape(symbol.Name));
                break;
        }

        if (symbol.NullableAnnotation == NullableAnnotation.Annotated &&
            syntax.Kind() != SyntaxKind.NullableType)
        {
            syntax = SyntaxFactory.NullableType(syntax);
        }
        return syntax;
    }

    public static NameSyntax CreateGlobalName(params string[] segments)
    {
        if (segments == null || segments.Length == 0)
            throw new ArgumentException("At least one name segment is required.", nameof(segments));
        NameSyntax result = SyntaxFactory.AliasQualifiedName(
            SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
            SyntaxFactory.IdentifierName(Escape(segments[0])));
        for (var index = 1; index < segments.Length; index++)
            result = SyntaxFactory.QualifiedName(result, SyntaxFactory.IdentifierName(Escape(segments[index])));
        return result;
    }

    private static TypeSyntax CreateNamed(INamedTypeSymbol symbol)
    {
        var special = SpecialTypeSyntax(symbol.SpecialType);
        if (special != null) return special;

        SimpleNameSyntax ownName;
        if (symbol.TypeArguments.Length == 0)
        {
            ownName = SyntaxFactory.IdentifierName(Escape(symbol.Name));
        }
        else
        {
            var arguments = new TypeSyntax[symbol.TypeArguments.Length];
            for (var index = 0; index < arguments.Length; index++) arguments[index] = Create(symbol.TypeArguments[index]);
            ownName = SyntaxFactory.GenericName(
                SyntaxFactory.Identifier(Escape(symbol.Name)),
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(arguments)));
        }

        if (symbol.ContainingType != null)
            return SyntaxFactory.QualifiedName((NameSyntax)CreateNamed(symbol.ContainingType), ownName);

        var namespaceSegments = new Stack<string>();
        for (var current = symbol.ContainingNamespace; current != null && !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
            namespaceSegments.Push(current.Name);

        if (namespaceSegments.Count == 0) return ownName;
        var first = namespaceSegments.Pop();
        NameSyntax result = SyntaxFactory.AliasQualifiedName(
            SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
            SyntaxFactory.IdentifierName(Escape(first)));
        while (namespaceSegments.Count != 0)
            result = SyntaxFactory.QualifiedName(result, SyntaxFactory.IdentifierName(Escape(namespaceSegments.Pop())));
        return SyntaxFactory.QualifiedName(result, ownName);
    }

    private static TypeSyntax? SpecialTypeSyntax(SpecialType type)
    {
        SyntaxKind kind;
        switch (type)
        {
            case SpecialType.System_Boolean: kind = SyntaxKind.BoolKeyword; break;
            case SpecialType.System_Byte: kind = SyntaxKind.ByteKeyword; break;
            case SpecialType.System_SByte: kind = SyntaxKind.SByteKeyword; break;
            case SpecialType.System_Int16: kind = SyntaxKind.ShortKeyword; break;
            case SpecialType.System_UInt16: kind = SyntaxKind.UShortKeyword; break;
            case SpecialType.System_Int32: kind = SyntaxKind.IntKeyword; break;
            case SpecialType.System_UInt32: kind = SyntaxKind.UIntKeyword; break;
            case SpecialType.System_Int64: kind = SyntaxKind.LongKeyword; break;
            case SpecialType.System_UInt64: kind = SyntaxKind.ULongKeyword; break;
            case SpecialType.System_Single: kind = SyntaxKind.FloatKeyword; break;
            case SpecialType.System_Double: kind = SyntaxKind.DoubleKeyword; break;
            case SpecialType.System_Decimal: kind = SyntaxKind.DecimalKeyword; break;
            case SpecialType.System_String: kind = SyntaxKind.StringKeyword; break;
            case SpecialType.System_Char: kind = SyntaxKind.CharKeyword; break;
            case SpecialType.System_Object: kind = SyntaxKind.ObjectKeyword; break;
            case SpecialType.System_Void: kind = SyntaxKind.VoidKeyword; break;
            default: return null;
        }
        return SyntaxFactory.PredefinedType(SyntaxFactory.Token(kind));
    }

    private static IEnumerable<ExpressionSyntax> CreateOmittedSizes(int rank)
    {
        for (var index = 0; index < rank; index++) yield return SyntaxFactory.OmittedArraySizeExpression();
    }

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;
}
