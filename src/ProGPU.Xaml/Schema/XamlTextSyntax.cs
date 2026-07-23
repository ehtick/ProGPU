using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ProGPU.Xaml.Schema;

public enum XamlTextSyntaxKind
{
    None,
    Intrinsic,
    Enumeration,
    TypeConverter,
    CreateFromString,
    Profile,
    Error
}

public sealed class XamlTextValueSyntaxInfo
{
    public XamlTextValueSyntaxInfo(string text, bool trimWhitespace = true, bool isCaseSensitive = false)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        TrimWhitespace = trimWhitespace;
        IsCaseSensitive = isCaseSensitive;
    }
    public string Text { get; }
    public bool TrimWhitespace { get; }
    public bool IsCaseSensitive { get; }
}

public sealed class XamlTextPatternSyntaxInfo
{
    public XamlTextPatternSyntaxInfo(string pattern, bool trimWhitespace = true, bool isCaseSensitive = true)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        TrimWhitespace = trimWhitespace;
        IsCaseSensitive = isCaseSensitive;
    }
    public string Pattern { get; }
    public bool TrimWhitespace { get; }
    public bool IsCaseSensitive { get; }
}

/// <summary>Exact Roslyn symbols used to construct a value from XAML text.</summary>
public enum XamlCreateFromStringInvocationKind
{
    ConverterInstance,
    StaticMethod
}

public sealed class XamlCreateFromStringShapeInfo
{
    public XamlCreateFromStringShapeInfo(
        INamedTypeSymbol factoryType,
        IMethodSymbol constructor,
        IMethodSymbol method,
        XamlSchemaAnnotationInfo annotation,
        string providerId)
        : this(
            XamlCreateFromStringInvocationKind.ConverterInstance,
            targetType: null,
            factoryType,
            constructor,
            method,
            method.Name,
            new[] { method },
            annotation,
            providerId,
            error: null)
    {
    }

    public XamlCreateFromStringShapeInfo(
        XamlCreateFromStringInvocationKind invocationKind,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol? factoryType,
        IMethodSymbol? constructor,
        IMethodSymbol? method,
        string requestedMethodName,
        IReadOnlyList<IMethodSymbol>? candidates,
        XamlSchemaAnnotationInfo annotation,
        string providerId,
        string? error)
    {
        InvocationKind = invocationKind;
        TargetType = targetType;
        FactoryType = factoryType;
        Constructor = constructor;
        Method = method;
        RequestedMethodName = requestedMethodName ?? string.Empty;
        Candidates = candidates ?? Array.Empty<IMethodSymbol>();
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlCreateFromStringInvocationKind InvocationKind { get; }
    public INamedTypeSymbol? TargetType { get; }
    public INamedTypeSymbol? FactoryType { get; }
    public IMethodSymbol? Constructor { get; }
    public IMethodSymbol? Method { get; }
    public string RequestedMethodName { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public XamlSchemaAnnotationInfo Annotation { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid =>
        Error == null &&
        FactoryType != null &&
        Method != null &&
        (InvocationKind !=
             XamlCreateFromStringInvocationKind.ConverterInstance ||
         Constructor != null);
}

/// <summary>Immutable section 5.4 text-syntax descriptor plus CLR conversion evidence.</summary>
public sealed class XamlTextSyntaxInfo
{
    public static XamlTextSyntaxInfo None { get; } = new XamlTextSyntaxInfo(XamlTextSyntaxKind.None);

    public XamlTextSyntaxInfo(
        XamlTextSyntaxKind kind,
        IReadOnlyList<XamlTextValueSyntaxInfo>? values = null,
        IReadOnlyList<XamlTextPatternSyntaxInfo>? patterns = null,
        INamedTypeSymbol? converterType = null,
        XamlSchemaAnnotationInfo? annotation = null,
        string? error = null,
        XamlCreateFromStringShapeInfo? createFromStringShape = null)
    {
        Kind = kind;
        Values = values ?? Array.Empty<XamlTextValueSyntaxInfo>();
        Patterns = patterns ?? Array.Empty<XamlTextPatternSyntaxInfo>();
        CreateFromStringShape = createFromStringShape;
        ConverterType = converterType ?? createFromStringShape?.FactoryType;
        Annotation = annotation;
        Error = error;
    }

    public XamlTextSyntaxKind Kind { get; }
    public IReadOnlyList<XamlTextValueSyntaxInfo> Values { get; }
    public IReadOnlyList<XamlTextPatternSyntaxInfo> Patterns { get; }
    public INamedTypeSymbol? ConverterType { get; }
    public XamlCreateFromStringShapeInfo? CreateFromStringShape { get; }
    public XamlSchemaAnnotationInfo? Annotation { get; }
    public string? Error { get; }
}

public static class XamlIntrinsicTextSyntax
{
    public static XamlTextSyntaxInfo Create(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return Intrinsic(values: new[] { Value("True"), Value("False") });
            case SpecialType.System_Char:
                return Intrinsic(patterns: new[] { Pattern(".") });
            case SpecialType.System_Byte:
                return Intrinsic(patterns: new[] { Pattern(@"\d+") });
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Decimal:
                return Intrinsic(patterns: new[] { Pattern(@"[+-]?\d+") });
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return Intrinsic(
                    new[] { Value("Infinity", caseSensitive: true), Value("-Infinity", caseSensitive: true), Value("NaN", caseSensitive: true) },
                    new[] { Pattern("floating-point with optional exponent") });
            case SpecialType.System_String:
            case SpecialType.System_Object:
                return Intrinsic();
        }
        var name = type.ToDisplayString();
        if (string.Equals(name, "System.Uri", StringComparison.Ordinal))
            return Intrinsic(patterns: new[] { Pattern("*", caseSensitive: true) });
        if (string.Equals(name, "System.TimeSpan", StringComparison.Ordinal))
            return Intrinsic(patterns: new[] { Pattern("clock-or-integer-timespan") });
        return XamlTextSyntaxInfo.None;
    }

    public static bool IsValid(XamlTypeInfo type, string text)
    {
        text = (text ?? string.Empty).Trim();
        switch (type.Symbol.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Object:
                return true;
            case SpecialType.System_Boolean:
                return string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(text, "False", StringComparison.OrdinalIgnoreCase);
            case SpecialType.System_Char:
                return text.Length == 1;
            case SpecialType.System_Byte:
                return IsUnsignedDigits(text) && byte.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Int16:
                return IsSignedDigits(text) && short.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Int32:
                return IsSignedDigits(text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Int64:
                return IsSignedDigits(text) && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Decimal:
                return IsSignedDigits(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Single:
                return IsSpecialFloat(text) || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            case SpecialType.System_Double:
                return IsSpecialFloat(text) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
        var name = type.Symbol.ToDisplayString();
        if (string.Equals(name, "System.Uri", StringComparison.Ordinal)) return true;
        if (string.Equals(name, "System.TimeSpan", StringComparison.Ordinal))
            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out _);
        return true;
    }

    public static bool IsValidEnumeration(XamlTypeInfo type, string text)
    {
        var parts = (text ?? string.Empty).Split(',');
        return parts.Length != 0 && parts.All(part => type.EnumValues.Any(value =>
            string.Equals(value.Name, part.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static XamlTextSyntaxInfo Intrinsic(
        IReadOnlyList<XamlTextValueSyntaxInfo>? values = null,
        IReadOnlyList<XamlTextPatternSyntaxInfo>? patterns = null) =>
        new XamlTextSyntaxInfo(XamlTextSyntaxKind.Intrinsic, values, patterns);

    private static XamlTextValueSyntaxInfo Value(string text, bool caseSensitive = false) =>
        new XamlTextValueSyntaxInfo(text, trimWhitespace: true, isCaseSensitive: caseSensitive);

    private static XamlTextPatternSyntaxInfo Pattern(string text, bool caseSensitive = true) =>
        new XamlTextPatternSyntaxInfo(text, trimWhitespace: true, isCaseSensitive: caseSensitive);

    private static bool IsUnsignedDigits(string value) => value.Length != 0 && value.All(char.IsDigit);
    private static bool IsSignedDigits(string value)
    {
        if (value.Length == 0) return false;
        var start = value[0] == '+' || value[0] == '-' ? 1 : 0;
        return start != value.Length && value.Skip(start).All(char.IsDigit);
    }
    private static bool IsSpecialFloat(string value) =>
        string.Equals(value, "Infinity", StringComparison.Ordinal) ||
        string.Equals(value, "-Infinity", StringComparison.Ordinal) ||
        string.Equals(value, "NaN", StringComparison.Ordinal);
}
