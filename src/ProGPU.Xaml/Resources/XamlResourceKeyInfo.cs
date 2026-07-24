using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ProGPU.Xaml.Binding;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Resources;

public enum XamlResourceKeyKind
{
    Text,
    Type,
    StaticMember
}

/// <summary>
/// Canonical, framework-neutral resource-key evidence. Symbol-valued keys retain their
/// Roslyn symbols; text remains unconverted until the containing dictionary key type is known.
/// </summary>
public sealed class XamlResourceKeyInfo : IEquatable<XamlResourceKeyInfo>
{
    private XamlResourceKeyInfo(
        XamlResourceKeyKind kind,
        string text,
        string identity,
        string expressionIdentity,
        ITypeSymbol? typeSymbol,
        ISymbol? staticMember,
        ITypeSymbol? valueType,
        TextSpan sourceSpan,
        ulong stableId)
    {
        Kind = kind;
        Text = text;
        Identity = identity;
        ExpressionIdentity = expressionIdentity;
        TypeSymbol = typeSymbol;
        StaticMember = staticMember;
        ValueType = valueType;
        SourceSpan = sourceSpan;
        StableId = stableId;
    }

    public XamlResourceKeyKind Kind { get; }
    public string Text { get; }
    /// <summary>Canonical runtime-equality identity used for graph matching and duplicates.</summary>
    public string Identity { get; }
    /// <summary>Identity of the syntax expression that will produce the key.</summary>
    public string ExpressionIdentity { get; }
    public ITypeSymbol? TypeSymbol { get; }
    public ISymbol? StaticMember { get; }
    /// <summary>The runtime value type of a symbol-valued key when Roslyn can prove it.</summary>
    public ITypeSymbol? ValueType { get; }
    public bool IsKnownNull => StaticMember is IFieldSymbol { HasConstantValue: true, ConstantValue: null };
    public TextSpan SourceSpan { get; }
    public ulong StableId { get; }

    public static XamlResourceKeyInfo FromText(
        string text,
        TextSpan sourceSpan,
        ulong stableId,
        ITypeSymbol? runtimeType = null)
    {
        text ??= string.Empty;
        var expressionIdentity = "text:" + text;
        return new XamlResourceKeyInfo(
            XamlResourceKeyKind.Text,
            text,
            CreateTextRuntimeIdentity(text, runtimeType) ?? expressionIdentity,
            expressionIdentity,
            null,
            null,
            runtimeType,
            sourceSpan,
            stableId);
    }

    public static XamlResourceKeyInfo FromType(ITypeSymbol type, TextSpan sourceSpan, ulong stableId)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new XamlResourceKeyInfo(
            XamlResourceKeyKind.Type, display, "type:" + display, "type:" + display, type, null,
            FindReferencedType(type, "System.Type"), sourceSpan, stableId);
    }

    public static XamlResourceKeyInfo FromStaticMember(ISymbol member, TextSpan sourceSpan, ulong stableId)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));
        var display = member.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var valueType = member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        var expressionIdentity = "static:" + display;
        var identity = member is IFieldSymbol constantField && XamlResourceConstantIdentity.TryCreate(constantField, out var constantIdentity)
            ? constantIdentity
            : expressionIdentity;
        return new XamlResourceKeyInfo(
            XamlResourceKeyKind.StaticMember, display, identity, expressionIdentity,
            null, member, valueType, sourceSpan, stableId);
    }

    public bool Equals(XamlResourceKeyInfo? other) => other != null &&
        string.Equals(Identity, other.Identity, StringComparison.Ordinal);
    public override bool Equals(object? obj) => Equals(obj as XamlResourceKeyInfo);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
    public override string ToString() => Text;

    private static INamedTypeSymbol? FindReferencedType(ITypeSymbol anchor, string metadataName)
    {
        var own = anchor.ContainingAssembly?.GetTypeByMetadataName(metadataName);
        if (own != null) return own;
        foreach (var assembly in anchor.ContainingModule?.ReferencedAssemblySymbols ?? ImmutableArray<IAssemblySymbol>.Empty)
        {
            var candidate = assembly.GetTypeByMetadataName(metadataName);
            if (candidate != null) return candidate;
        }
        return null;
    }

    private static string? CreateTextRuntimeIdentity(string text, ITypeSymbol? runtimeType)
    {
        if (runtimeType == null || runtimeType.SpecialType == SpecialType.System_Object ||
            runtimeType.SpecialType == SpecialType.System_String)
        {
            var value = text.StartsWith("{}", StringComparison.Ordinal) ? text.Substring(2) : text;
            return XamlResourceConstantIdentity.Create(runtimeType, value, SpecialType.System_String);
        }

        object? constant = runtimeType.SpecialType switch
        {
            SpecialType.System_Boolean when bool.TryParse(text, out var value) => value,
            SpecialType.System_Char when text.Length == 1 => text[0],
            SpecialType.System_Byte when byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int16 when short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int32 when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Int64 when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Single when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Double when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            SpecialType.System_Decimal when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
        if (constant != null)
            return XamlResourceConstantIdentity.Create(runtimeType, constant, runtimeType.SpecialType);

        if (runtimeType.TypeKind == TypeKind.Enum)
        {
            var field = runtimeType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(candidate =>
                candidate.HasConstantValue && string.Equals(candidate.Name, text, StringComparison.OrdinalIgnoreCase));
            if (field != null && XamlResourceConstantIdentity.TryCreate(field, out var enumIdentity))
                return enumIdentity;
        }

        var typeIdentity = runtimeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "text-converted:" + typeIdentity + ":" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
    }
}

internal static class XamlResourceConstantIdentity
{
    public static bool TryCreate(IFieldSymbol field, out string identity)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        if (!field.HasConstantValue)
        {
            identity = string.Empty;
            return false;
        }

        identity = Create(field.Type, field.ConstantValue, field.Type.SpecialType);
        return true;
    }

    public static string Create(ITypeSymbol? type, object? value, SpecialType fallbackSpecialType)
    {
        var typeIdentity = type == null || type.SpecialType == SpecialType.System_Object
            ? SpecialTypeName(fallbackSpecialType)
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var valueIdentity = Format(value);
        return "constant:" + typeIdentity + ":" + valueIdentity.Length.ToString(CultureInfo.InvariantCulture) + ":" + valueIdentity;
    }

    private static string SpecialTypeName(SpecialType type) => type switch
    {
        SpecialType.System_String => "string",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Char => "char",
        SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_Int32 => "int",
        SpecialType.System_Int64 => "long",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_Decimal => "decimal",
        _ => type.ToString()
    };

    private static string Format(object? value)
    {
        if (value == null) return "null";
        return value switch
        {
            string text => "string:" + text,
            char character => "char:" + ((int)character).ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "bool:true" : "bool:false",
            float number when float.IsNaN(number) => "single:nan",
            float number when number == 0f => "single:0",
            float number => "single:" + number.ToString("R", CultureInfo.InvariantCulture),
            double number when double.IsNaN(number) => "double:nan",
            double number when number == 0d => "double:0",
            double number => "double:" + number.ToString("R", CultureInfo.InvariantCulture),
            decimal number when number == 0m => "decimal:0",
            decimal number => "decimal:" + number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => value.GetType().FullName + ":" +
                formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.GetType().FullName + ":" + value
        };
    }
}

public static class XamlResourceKeyFactory
{
    public static XamlResourceKeyInfo? GetDictionaryKey(
        XamlBoundObject item,
        ITypeSymbol? dictionaryKeyType = null,
        IReadOnlyList<string>? directiveAliases = null)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        foreach (var member in item.Members)
        {
            if (member.Member.Kind == XamlBoundReferenceKind.Directive &&
                string.Equals(member.Member.RequestedName.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
                string.Equals(member.Member.RequestedName.LocalName, "Key", StringComparison.Ordinal))
                return member.Values.Length == 1 ? FromBoundValue(member.Values[0], dictionaryKeyType) : null;
        }
        if (directiveAliases != null)
        {
            foreach (var alias in directiveAliases)
            {
                foreach (var member in item.Members)
                {
                    if (member.Member.Kind == XamlBoundReferenceKind.Directive &&
                        string.Equals(member.Member.RequestedName.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
                        string.Equals(member.Member.RequestedName.LocalName, alias, StringComparison.Ordinal))
                        return member.Values.Length == 1
                            ? FromBoundValue(member.Values[0], dictionaryKeyType)
                            : null;
                }
            }
        }
        var implicitName = item.Type.Symbol?.DictionaryKeyMemberName;
        if (implicitName == null) return null;
        foreach (var member in item.Members)
            if (member.Member.Symbol != null &&
                string.Equals(member.Member.Symbol.Name, implicitName, StringComparison.Ordinal))
                return member.Values.Length == 1 ? FromBoundValue(member.Values[0], dictionaryKeyType) : null;
        return null;
    }

    public static XamlResourceKeyInfo? GetReferenceKey(XamlBoundObject reference)
    {
        if (reference == null) throw new ArgumentNullException(nameof(reference));
        foreach (var member in reference.Members)
        {
            // Object-element markup extensions may themselves be dictionary entries.
            // Language directives such as x:Key describe that entry; they are not
            // arguments to the resource lookup operation.
            if (member.Member.Kind == XamlBoundReferenceKind.Directive &&
                string.Equals(member.Member.RequestedName.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
                string.Equals(member.Member.RequestedName.LocalName, "Key", StringComparison.Ordinal))
                continue;
            foreach (var value in member.Values)
            {
                var key = FromBoundValue(value);
                if (key != null) return key;
            }
        }
        return null;
    }

    public static XamlResourceKeyInfo? FromBoundValue(XamlBoundValue value, ITypeSymbol? textRuntimeType = null)
    {
        switch (value)
        {
            case XamlBoundText text:
                return XamlResourceKeyInfo.FromText(text.Text, text.SourceSpan, text.StableId, textRuntimeType);
            case XamlBoundTypeValue type when type.Type.Symbol != null:
                return XamlResourceKeyInfo.FromType(type.Type.Symbol.Symbol, type.SourceSpan, type.StableId);
            case XamlBoundStaticMemberValue member when member.Member != null:
                return XamlResourceKeyInfo.FromStaticMember(member.Member, member.SourceSpan, member.StableId);
            case XamlBoundObject nested when IsIntrinsic(nested, "Type", "TypeExtension"):
            {
                var type = nested.Members.SelectMany(static member => member.Values)
                    .OfType<XamlBoundTypeValue>().SingleOrDefault(candidate => candidate.Type.Symbol != null);
                return type == null
                    ? null
                    : XamlResourceKeyInfo.FromType(type.Type.Symbol!.Symbol, nested.SourceSpan, nested.StableId);
            }
            case XamlBoundObject nested when IsIntrinsic(nested, "Static", "StaticExtension"):
            {
                var member = nested.Members.SelectMany(static item => item.Values)
                    .OfType<XamlBoundStaticMemberValue>().SingleOrDefault(candidate => candidate.Member != null);
                return member?.Member == null
                    ? null
                    : XamlResourceKeyInfo.FromStaticMember(member.Member, nested.SourceSpan, nested.StableId);
            }
            default:
                return null;
        }
    }

    private static bool IsIntrinsic(XamlBoundObject value, string shortName, string extensionName) =>
        string.Equals(value.Type.RequestedName.NamespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal) &&
        (string.Equals(value.Type.RequestedName.LocalName, shortName, StringComparison.Ordinal) ||
         string.Equals(value.Type.RequestedName.LocalName, extensionName, StringComparison.Ordinal));
}
