using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProGPU.Xaml.Infoset;
using ProGPU.Xaml.Schema;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Roslyn;

public sealed class RoslynXamlPreviewHost
{
    internal RoslynXamlPreviewHost(
        CSharpCompilation compilation,
        string? qualifiedTypeName,
        string? materializationError)
    {
        Compilation = compilation;
        QualifiedTypeName = qualifiedTypeName;
        MaterializationError = materializationError;
    }

    public CSharpCompilation Compilation { get; }
    public string? QualifiedTypeName { get; }
    public string? MaterializationError { get; }
    public bool CanMaterialize =>
        QualifiedTypeName != null &&
        MaterializationError == null;
}

/// <summary>
/// Creates the minimal structured partial class needed to bind and instantiate an edited
/// class-backed XAML document. Root type resolution remains delegated to the supplied
/// framework-neutral type system.
/// </summary>
public sealed class RoslynXamlPreviewHostFactory
{
    public RoslynXamlPreviewHost Create(
        CSharpCompilation baseCompilation,
        XamlInfosetDocument document,
        IXamlTypeSystem typeSystem)
    {
        if (baseCompilation == null)
            throw new ArgumentNullException(nameof(baseCompilation));
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        if (typeSystem == null)
            throw new ArgumentNullException(nameof(typeSystem));
        if (document.Root == null)
        {
            return Unavailable(
                baseCompilation,
                "Live preview requires a document root.");
        }

        var className = GetDirectiveText(
            document.Root,
            XamlNamespaces.Language2006,
            "Class");
        if (string.IsNullOrWhiteSpace(className))
        {
            return Unavailable(
                baseCompilation,
                "Live preview currently requires a valid x:Class directive.");
        }

        className = className!.Trim();
        var lastDot = className.LastIndexOf('.');
        var namespaceName = lastDot < 0
            ? string.Empty
            : className.Substring(0, lastDot);
        var typeName = lastDot < 0
            ? className
            : className.Substring(lastDot + 1);
        if (!IsValidQualifiedTypeName(namespaceName, typeName))
        {
            return Unavailable(
                baseCompilation,
                $"x:Class '{className}' is not a valid C# type name.");
        }

        var existingType = baseCompilation.GetTypeByMetadataName(
            className);
        if (existingType != null)
        {
            if (existingType.Locations.Any(
                    static location => location.IsInSource))
            {
                return new RoslynXamlPreviewHost(
                    baseCompilation,
                    className,
                    null);
            }

            return Unavailable(
                baseCompilation,
                $"x:Class '{className}' resolves only from metadata and cannot be extended by a preview partial declaration.");
        }

        var rootType = typeSystem.ResolveType(
            document.Root.TypeName.NamespaceUri,
            document.Root.TypeName.LocalName);
        if (rootType == null)
        {
            return Unavailable(
                baseCompilation,
                $"Root type '{document.Root.TypeName.DisplayName}' could not be resolved for preview host synthesis.");
        }

        var declaration = SyntaxFactory.ClassDeclaration(
                SyntaxFactory.Identifier(typeName))
            .AddModifiers(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword))
            .WithBaseList(
                SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                        SyntaxFactory.SimpleBaseType(
                            RoslynTypeSyntaxFactory.Create(
                                rootType.Symbol)))))
            .AddMembers(
                SyntaxFactory.ConstructorDeclaration(
                        SyntaxFactory.Identifier(typeName))
                    .AddModifiers(
                        SyntaxFactory.Token(
                            SyntaxKind.PublicKeyword))
                    .WithBody(
                        SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.IdentifierName(
                                        "InitializeComponent"))))));

        MemberDeclarationSyntax topLevel = declaration;
        if (namespaceName.Length != 0)
        {
            topLevel = SyntaxFactory.NamespaceDeclaration(
                    CreateName(namespaceName))
                .AddMembers(declaration);
        }

        var unit = SyntaxFactory.CompilationUnit()
            .AddMembers(topLevel);
        var tree = CSharpSyntaxTree.Create(
            unit,
            path: document.Path + ".PreviewHost.g.cs");
        return new RoslynXamlPreviewHost(
            baseCompilation.AddSyntaxTrees(tree),
            className,
            null);
    }

    private static RoslynXamlPreviewHost Unavailable(
        CSharpCompilation compilation,
        string message) =>
        new RoslynXamlPreviewHost(
            compilation,
            null,
            message);

    private static string? GetDirectiveText(
        XamlInfosetObject value,
        string namespaceUri,
        string localName)
    {
        var member = value.FindMember(namespaceUri, localName);
        if (member == null || member.Values.Length != 1)
            return null;
        return member.Values[0] is XamlInfosetText text
            ? text.Text
            : null;
    }

    private static bool IsValidQualifiedTypeName(
        string namespaceName,
        string typeName) =>
        SyntaxFacts.IsValidIdentifier(typeName) &&
        (namespaceName.Length == 0 ||
         namespaceName.Split('.').All(
             SyntaxFacts.IsValidIdentifier));

    private static NameSyntax CreateName(string value)
    {
        var segments = value.Split('.');
        NameSyntax result = SyntaxFactory.IdentifierName(
            segments[0]);
        for (var index = 1; index < segments.Length; index++)
        {
            result = SyntaxFactory.QualifiedName(
                result,
                SyntaxFactory.IdentifierName(segments[index]));
        }

        return result;
    }
}
