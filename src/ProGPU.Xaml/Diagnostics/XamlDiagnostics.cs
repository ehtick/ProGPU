using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Diagnostics;

public static class XamlDiagnostics
{
    public const string SpecificationSectionProperty = "MSXamlSection";

    public static Diagnostic Create(
        string id,
        DiagnosticSeverity severity,
        string message,
        string path,
        SourceText sourceText,
        TextSpan span,
        string? specificationSection = null)
    {
        if (span.Start < 0 || span.End > sourceText.Length)
        {
            span = new TextSpan(Math.Max(0, Math.Min(span.Start, sourceText.Length)), 0);
        }

        var descriptor = new DiagnosticDescriptor(
            id,
            "XAML compilation",
            "{0}",
            "ProGPU.Xaml",
            severity,
            isEnabledByDefault: true,
            description: specificationSection == null ? null : "MS-XAML section " + specificationSection);
        var properties = specificationSection == null
            ? ImmutableDictionary<string, string?>.Empty
            : ImmutableDictionary<string, string?>.Empty.Add(SpecificationSectionProperty, specificationSection);
        var location = Location.Create(path, span, sourceText.Lines.GetLinePositionSpan(span));
        return Diagnostic.Create(descriptor, location, properties, message);
    }

    public static Diagnostic Create(
        string id,
        DiagnosticSeverity severity,
        string message,
        string path,
        TextSpan span,
        LinePositionSpan lineSpan,
        string? specificationSection = null)
    {
        var descriptor = new DiagnosticDescriptor(
            id,
            "XAML compilation",
            "{0}",
            "ProGPU.Xaml",
            severity,
            isEnabledByDefault: true,
            description: specificationSection == null ? null : "MS-XAML section " + specificationSection);
        var properties = specificationSection == null
            ? ImmutableDictionary<string, string?>.Empty
            : ImmutableDictionary<string, string?>.Empty.Add(SpecificationSectionProperty, specificationSection);
        return Diagnostic.Create(
            descriptor,
            Location.Create(path, span, lineSpan),
            properties,
            message);
    }

    public static string? GetSpecificationSection(Diagnostic diagnostic) =>
        diagnostic.Properties.TryGetValue(SpecificationSectionProperty, out var value) ? value : null;
}
