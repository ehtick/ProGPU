using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Schema;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Infoset;

public sealed class XamlInfosetValidationOptions
{
    public int MaximumDiagnostics { get; set; } = 1024;
}

/// <summary>Executes MS-XAML section 6 structural rules without schema or framework knowledge.</summary>
public sealed class XamlInfosetStructuralValidator
{
    public ImmutableArray<Diagnostic> Validate(
        XamlInfosetDocument document,
        XamlInfosetValidationOptions? options = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        options ??= new XamlInfosetValidationOptions();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (document.Root == null) return diagnostics.ToImmutable();

        var visited = new HashSet<ulong>();
        ValidateObject(document.Root, isRoot: true, document, options, diagnostics, visited);
        return diagnostics.ToImmutable();
    }

    private static void ValidateObject(
        XamlInfosetObject value,
        bool isRoot,
        XamlInfosetDocument document,
        XamlInfosetValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        HashSet<ulong> visited)
    {
        if (!visited.Add(value.StableId))
        {
            Add(diagnostics, options, document, "PGXAML1206",
                "The XAML information set must be a tree; an object identity was encountered more than once.",
                value.SourceSpan, "6.1.1.2");
            return;
        }

        var members = new HashSet<XamlInfosetMemberName>();
        var hasClass = false;
        var hasSubclass = false;
        var hasClassModifier = false;
        var passedInitialMemberRegion = false;
        foreach (var member in value.Members)
        {
            // The lossless infoset retains indentation so editor and formatter clients can
            // reproduce the document. Whitespace-only implicit content is lexical evidence,
            // not a semantic member and therefore cannot close the initial-member region or
            // participate in duplicate-member validation.
            if (IsWhitespaceOnlyImplicitMember(member)) continue;

            if (!members.Add(member.Name))
            {
                Add(diagnostics, options, document, "PGXAML1202",
                    $"Member '{member.Name.DisplayName}' is specified more than once.",
                    member.SourceSpan, "6.2.1.3");
            }

            if (member.Name.IsDirective &&
                XamlIntrinsicSchema.TryGetDirective(member.Name.NamespaceUri, member.Name.LocalName, out var definition))
            {
                var validRepresentation = definition!.AllowedLocation switch
                {
                    XamlAllowedLocation.AttributeOnly => member.Origin == XamlMemberOrigin.Directive,
                    XamlAllowedLocation.InitialMemberElementsOnly =>
                        member.Origin == XamlMemberOrigin.MemberElement && !passedInitialMemberRegion,
                    XamlAllowedLocation.AttributeOrInitialMemberElementsOnly =>
                        member.Origin == XamlMemberOrigin.Directive ||
                        (member.Origin == XamlMemberOrigin.MemberElement && !passedInitialMemberRegion),
                    XamlAllowedLocation.None => member.Origin == XamlMemberOrigin.ImplicitContent ||
                                                member.Origin == XamlMemberOrigin.MarkupExtensionArgument,
                    _ => true
                };
                if (!validRepresentation)
                {
                    Add(diagnostics, options, document, "PGXAML1209",
                        $"Directive '{member.Name.DisplayName}' is not valid at this XML location.",
                        member.SourceSpan,
                        GetDirectiveSection(member.Name.LocalName));
                }
            }

            if (member.Origin == XamlMemberOrigin.ImplicitContent ||
                (member.Origin == XamlMemberOrigin.MemberElement &&
                 !IsInitialConstructionDirective(member)))
                passedInitialMemberRegion = true;

            if (IsXamlDirective(member, "Class"))
            {
                hasClass = true;
                if (!isRoot)
                {
                    Add(diagnostics, options, document, "PGXAML1204",
                        "x:Class is valid only on the document root object.",
                        member.SourceSpan, "6.3.1.6");
                }
            }
            else if (IsXamlDirective(member, "Subclass")) hasSubclass = true;
            else if (IsXamlDirective(member, "ClassModifier")) hasClassModifier = true;

            foreach (var child in member.Values)
            {
                if (child is XamlInfosetObject childObject)
                    ValidateObject(childObject, isRoot: false, document, options, diagnostics, visited);
            }
        }

        if (isRoot && hasSubclass && !hasClass)
        {
            Add(diagnostics, options, document, "PGXAML1207",
                "x:Subclass requires x:Class on the root object.", value.SourceSpan, "6.3.1.7");
        }
        if (isRoot && hasClassModifier && !hasClass)
        {
            Add(diagnostics, options, document, "PGXAML1208",
                "x:ClassModifier requires x:Class on the root object.", value.SourceSpan, "6.3.1.8");
        }
    }

    private static bool IsXamlDirective(XamlInfosetMember member, string name) =>
        member.Name.IsDirective &&
        string.Equals(member.Name.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
        string.Equals(member.Name.LocalName, name, StringComparison.Ordinal);

    private static bool IsInitialConstructionDirective(XamlInfosetMember member) =>
        IsXamlDirective(member, "Arguments") || IsXamlDirective(member, "FactoryMethod");

    private static bool IsWhitespaceOnlyImplicitMember(XamlInfosetMember member)
    {
        if (member.Origin != XamlMemberOrigin.ImplicitContent || member.Values.IsEmpty) return false;
        foreach (var value in member.Values)
        {
            if (value is not XamlInfosetText text || !string.IsNullOrWhiteSpace(text.Text))
                return false;
        }
        return true;
    }

    private static string GetDirectiveSection(string name) => name switch
    {
        "Arguments" => "7.3.16",
        "FactoryMethod" => "7.3.17",
        "TypeArguments" => "7.3.11",
        "Initialization" => "7.3.3",
        _ => "7.3"
    };

    private static void Add(
        ImmutableArray<Diagnostic>.Builder diagnostics,
        XamlInfosetValidationOptions options,
        XamlInfosetDocument document,
        string id,
        string message,
        Microsoft.CodeAnalysis.Text.TextSpan span,
        string section)
    {
        if (diagnostics.Count >= options.MaximumDiagnostics) return;
        diagnostics.Add(XamlDiagnostics.Create(
            id,
            DiagnosticSeverity.Error,
            message,
            document.Path,
            document.SourceText,
            span,
            section));
    }
}
