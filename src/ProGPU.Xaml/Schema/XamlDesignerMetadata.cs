using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ProGPU.Xaml.Schema;

public sealed class XamlDefaultValueInfo
{
    public XamlDefaultValueInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? member,
        TypedConstant? valueConstant,
        ITypeSymbol? conversionType,
        string? text,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Member = member;
        ValueConstant = valueConstant;
        ConversionType = conversionType;
        Text = text;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Member { get; }
    public TypedConstant? ValueConstant { get; }
    public ITypeSymbol? ConversionType { get; }
    public string? Text { get; }
    public bool UsesTextConversion => ConversionType != null;
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Member != null &&
        (ValueConstant.HasValue || ConversionType != null) &&
        Error == null;
    public string? Error { get; }
}

public enum XamlDesignerSerializationVisibility
{
    Hidden = 0,
    Visible = 1,
    Content = 2
}

public sealed class XamlDesignerSerializationVisibilityInfo
{
    public XamlDesignerSerializationVisibilityInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? member,
        XamlDesignerSerializationVisibility? visibility,
        TypedConstant? valueConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Member = member;
        Visibility = visibility;
        ValueConstant = valueConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Member { get; }
    public XamlDesignerSerializationVisibility? Visibility { get; }
    public TypedConstant? ValueConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Member != null && Visibility.HasValue && Error == null;
    public string? Error { get; }
}

public sealed class XamlDesignerSerializationOptionsInfo
{
    public XamlDesignerSerializationOptionsInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? member,
        bool serializeAsAttribute,
        long? rawValue,
        TypedConstant? valueConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Member = member;
        SerializeAsAttribute = serializeAsAttribute;
        RawValue = rawValue;
        ValueConstant = valueConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Member { get; }
    public bool SerializeAsAttribute { get; }
    public long? RawValue { get; }
    public TypedConstant? ValueConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Member != null && RawValue.HasValue && Error == null;
    public string? Error { get; }
}

public sealed class XamlBrowsableInfo
{
    public XamlBrowsableInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? target,
        bool? value,
        TypedConstant? valueConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Target = target;
        Value = value;
        ValueConstant = valueConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Target { get; }
    public bool? Value { get; }
    public TypedConstant? ValueConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Target != null && Value.HasValue && Error == null;
    public string? Error { get; }
}

public enum XamlEditorBrowsableState
{
    Always = 0,
    Never = 1,
    Advanced = 2
}

public sealed class XamlEditorBrowsableInfo
{
    public XamlEditorBrowsableInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? target,
        XamlEditorBrowsableState? state,
        TypedConstant? valueConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Target = target;
        State = state;
        ValueConstant = valueConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Target { get; }
    public XamlEditorBrowsableState? State { get; }
    public TypedConstant? ValueConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Target != null && State.HasValue && Error == null;
    public string? Error { get; }
}

public enum XamlLocalizationCategory
{
    None = 0,
    Text = 1,
    Title = 2,
    Label = 3,
    Button = 4,
    CheckBox = 5,
    ComboBox = 6,
    ListBox = 7,
    Menu = 8,
    RadioButton = 9,
    ToolTip = 10,
    Hyperlink = 11,
    TextFlow = 12,
    XmlData = 13,
    Font = 14,
    Inherit = 15,
    Ignore = 16,
    NeverLocalize = 17
}

public enum XamlLocalizationReadability
{
    Unreadable = 0,
    Readable = 1,
    Inherit = 2
}

public enum XamlLocalizationModifiability
{
    Unmodifiable = 0,
    Modifiable = 1,
    Inherit = 2
}

public sealed class XamlLocalizabilityInfo
{
    public XamlLocalizabilityInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? target,
        XamlLocalizationCategory? category,
        XamlLocalizationReadability? readability,
        XamlLocalizationModifiability? modifiability,
        TypedConstant? categoryConstant,
        TypedConstant? readabilityConstant,
        TypedConstant? modifiabilityConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Target = target;
        Category = category;
        Readability = readability;
        Modifiability = modifiability;
        CategoryConstant = categoryConstant;
        ReadabilityConstant = readabilityConstant;
        ModifiabilityConstant = modifiabilityConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Target { get; }
    public XamlLocalizationCategory? Category { get; }
    public XamlLocalizationReadability? Readability { get; }
    public XamlLocalizationModifiability? Modifiability { get; }
    public TypedConstant? CategoryConstant { get; }
    public TypedConstant? ReadabilityConstant { get; }
    public TypedConstant? ModifiabilityConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Target != null &&
        Category.HasValue &&
        Readability.HasValue &&
        Modifiability.HasValue &&
        Error == null;
    public string? Error { get; }
}

public sealed class XamlDesignerSerializationMethodsInfo
{
    public XamlDesignerSerializationMethodsInfo(
        IPropertySymbol property,
        IMethodSymbol? shouldSerializeMethod,
        IMethodSymbol? resetMethod,
        IReadOnlyList<IMethodSymbol> shouldSerializeCandidates,
        IReadOnlyList<IMethodSymbol> resetCandidates,
        string providerId,
        string? error)
    {
        Property = property ?? throw new ArgumentNullException(nameof(property));
        ShouldSerializeMethod = shouldSerializeMethod;
        ResetMethod = resetMethod;
        ShouldSerializeCandidates = shouldSerializeCandidates ??
            throw new ArgumentNullException(nameof(shouldSerializeCandidates));
        ResetCandidates = resetCandidates ??
            throw new ArgumentNullException(nameof(resetCandidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public IPropertySymbol Property { get; }
    public IMethodSymbol? ShouldSerializeMethod { get; }
    public IMethodSymbol? ResetMethod { get; }
    public IReadOnlyList<IMethodSymbol> ShouldSerializeCandidates { get; }
    public IReadOnlyList<IMethodSymbol> ResetCandidates { get; }
    public string ProviderId { get; }
    public bool IsConditional => ShouldSerializeMethod != null;
    public bool CanReset => ResetMethod != null;
    public bool IsValid =>
        (ShouldSerializeMethod != null || ResetMethod != null) &&
        Error == null;
    public string? Error { get; }
}

public enum XamlMemberSerializationForm
{
    Excluded,
    Element,
    Content,
    Attribute
}

/// <summary>
/// Framework-neutral save-path decision derived only from symbol metadata.
/// Serializers and bidirectional editors consume this policy instead of
/// loading framework attribute instances or duplicating precedence rules.
/// </summary>
public sealed class XamlMemberSerializationPolicy
{
    private XamlMemberSerializationPolicy(
        XamlMemberInfo member,
        XamlMemberSerializationForm form,
        bool isValid)
    {
        Member = member;
        Form = form;
        IsValid = isValid;
    }

    public XamlMemberInfo Member { get; }
    public XamlMemberSerializationForm Form { get; }
    public bool IsValid { get; }
    public bool Include => Form != XamlMemberSerializationForm.Excluded;
    public bool IsContent => Form == XamlMemberSerializationForm.Content;
    public bool PreferAttribute => Form == XamlMemberSerializationForm.Attribute;
    public XamlDefaultValueInfo? DefaultValue => Member.DefaultValue;
    public IMethodSymbol? ShouldSerializeMethod =>
        Member.SerializationMethods?.ShouldSerializeMethod;
    public IMethodSymbol? ResetMethod =>
        Member.SerializationMethods?.ResetMethod;
    public bool IsConditionallyIncluded => ShouldSerializeMethod != null;
    public bool CanReset => ResetMethod != null || DefaultValue?.IsValid == true;

    public static XamlMemberSerializationPolicy Create(XamlMemberInfo member)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));

        var visibility = member.DesignerSerializationVisibility;
        var options = member.DesignerSerializationOptions;
        var isValid = (visibility == null || visibility.IsValid) &&
            (options == null || options.IsValid) &&
            (member.DefaultValue == null || member.DefaultValue.IsValid) &&
            (member.SerializationMethods == null ||
             member.SerializationMethods.IsValid);
        if (!isValid)
            return new XamlMemberSerializationPolicy(
                member,
                XamlMemberSerializationForm.Element,
                isValid: false);
        if (visibility?.Visibility == XamlDesignerSerializationVisibility.Hidden)
            return new XamlMemberSerializationPolicy(
                member,
                XamlMemberSerializationForm.Excluded,
                isValid: true);
        if (visibility?.Visibility == XamlDesignerSerializationVisibility.Content)
            return new XamlMemberSerializationPolicy(
                member,
                XamlMemberSerializationForm.Content,
                isValid: true);
        return new XamlMemberSerializationPolicy(
            member,
            options?.SerializeAsAttribute == true
                ? XamlMemberSerializationForm.Attribute
                : XamlMemberSerializationForm.Element,
            isValid: true);
    }
}
