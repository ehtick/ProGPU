using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Schema;

/// <summary>
/// Stable semantic identifiers understood by the framework-neutral schema and binding layers.
/// Framework packages map their public attributes to these identifiers.
/// </summary>
public static class XamlSchemaSemantics
{
    public const string ContentProperty = "xaml.content-property";
    public const string RuntimeNameProperty = "xaml.runtime-name-property";
    public const string DictionaryKeyProperty = "xaml.dictionary-key-property";
    public const string Ambient = "xaml.ambient";
    public const string DependsOn = "xaml.depends-on";
    public const string ConstructorArgument = "xaml.constructor-argument";
    public const string ContentWrapper = "xaml.content-wrapper";
    public const string NameScopeProperty = "xaml.namescope-property";
    public const string XmlLanguageProperty = "xaml.xml-language-property";
    public const string UidProperty = "xaml.uid-property";
    public const string TrimSurroundingWhitespace = "xaml.trim-surrounding-whitespace";
    public const string WhitespaceSignificantCollection = "xaml.whitespace-significant-collection";
    public const string UsableDuringInitialization = "xaml.usable-during-initialization";
    public const string TypeConverter = "xaml.type-converter";
    public const string CreateFromString = "xaml.create-from-string";
    public const string ValueSerializer = "xaml.value-serializer";
    public const string MarkupExtensionReturnType = "xaml.markup-extension-return-type";
    public const string MarkupExtensionBracketCharacters = "xaml.markup-extension-bracket-characters";
    public const string SetMarkupExtensionHandler = "xaml.set-markup-extension-handler";
    public const string SetTypeConverterHandler = "xaml.set-type-converter-handler";
    public const string XmlnsDefinition = "xaml.xmlns-definition";
    public const string XmlnsPrefix = "xaml.xmlns-prefix";
    public const string XmlnsCompatibleWith = "xaml.xmlns-compatible-with";
    public const string AssignBinding = "binding.assign-object";
    public const string DataType = "binding.data-type";
    public const string InheritDataType = "binding.inherit-data-type";
    public const string InheritDataTypeFromItems = "binding.inherit-data-type-from-items";
    public const string TemplateContent = "xaml.template-content";
    public const string ControlTemplateScope = "xaml.control-template-scope";
    public const string MarkupExtensionOption = "xaml.markup-extension-option";
    public const string MarkupExtensionDefaultOption = "xaml.markup-extension-default-option";
    public const string ListSeparator = "xaml.list-separator";
    public const string AcceptEmptyServiceProvider = "xaml.accept-empty-service-provider";
    public const string RequireService = "xaml.require-service";
    public const string XamlCompilation = "xaml.compilation-mode";
    public const string DeferredLoad = "xaml.deferred-load";
    public const string RootNamespace = "xaml.root-namespace";
    public const string XamlFilePath = "xaml.file-path";
    public const string XamlResourceId = "xaml.resource-id";
    public const string FullXamlMetadataProvider = "xaml.full-metadata-provider";
    public const string Bindable = "binding.bindable";
    public const string AttachedPropertyBrowseRule = "tooling.attached-property-browse-rule";
    public const string StyleTypedProperty = "tooling.style-typed-property";
    public const string TemplatePart = "tooling.template-part";
    public const string TemplateVisualState = "tooling.template-visual-state";
    public const string DefaultValue = "serialization.default-value";
    public const string DesignerSerializationVisibility = "serialization.designer-visibility";
    public const string DesignerSerializationOptions = "serialization.designer-options";
    public const string Browsable = "tooling.browsable";
    public const string EditorBrowsable = "tooling.editor-browsable";
    public const string DesignTimeVisible = "tooling.design-time-visible";
    public const string Localizability = "localization.localizability";
    public const string Obsolete = "compiler.obsolete";
    public const string Experimental = "compiler.experimental";
}

/// <summary>
/// Exact annotation evidence for a member whose value establishes the compiled-binding
/// data type of nested markup. The value itself remains a bound XAML node so profiles can
/// support type objects, selectors, and framework-specific data-type representations.
/// </summary>
public sealed class XamlDataTypeSourceInfo
{
    public XamlDataTypeSourceInfo(
        XamlSchemaAnnotationInfo annotation,
        IPropertySymbol? property,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Property = property;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public IPropertySymbol? Property { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Property != null && Error == null;
    public string? Error { get; }
}

public enum XamlDataTypeScopeKind
{
    Style = 1,
    ControlTemplate = 2
}

/// <summary>
/// Exact annotation evidence requesting a framework scope as the data type used to resolve
/// a member or constructor parameter. Numeric values are retained through the enum projection
/// so unknown future framework values produce a diagnostic instead of being guessed.
/// </summary>
public sealed class XamlDataTypeInheritanceInfo
{
    public XamlDataTypeInheritanceInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? target,
        XamlDataTypeScopeKind? scopeKind,
        TypedConstant? scopeKindConstant,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Target = target;
        ScopeKind = scopeKind;
        ScopeKindConstant = scopeKindConstant;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Target { get; }
    public XamlDataTypeScopeKind? ScopeKind { get; }
    public TypedConstant? ScopeKindConstant { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Target != null && ScopeKind.HasValue && Error == null;
    public string? Error { get; }
}

/// <summary>
/// Exact annotation evidence for item-type inheritance. Both the declared lookup type and
/// the resolved public instance property are retained for analyzers, editors, and emitters.
/// </summary>
public sealed class XamlItemsDataTypeInheritanceInfo
{
    public XamlItemsDataTypeInheritanceInfo(
        XamlSchemaAnnotationInfo annotation,
        IPropertySymbol? targetProperty,
        string? ancestorItemsPropertyName,
        ITypeSymbol? declaredAncestorType,
        INamedTypeSymbol? lookupType,
        IPropertySymbol? ancestorItemsProperty,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        TargetProperty = targetProperty;
        AncestorItemsPropertyName = ancestorItemsPropertyName;
        DeclaredAncestorType = declaredAncestorType;
        LookupType = lookupType;
        AncestorItemsProperty = ancestorItemsProperty;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public IPropertySymbol? TargetProperty { get; }
    public string? AncestorItemsPropertyName { get; }
    public ITypeSymbol? DeclaredAncestorType { get; }
    public INamedTypeSymbol? LookupType { get; }
    public IPropertySymbol? AncestorItemsProperty { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => TargetProperty != null &&
        !string.IsNullOrEmpty(AncestorItemsPropertyName) &&
        LookupType != null &&
        AncestorItemsProperty != null &&
        Error == null;
    public string? Error { get; }
}

/// <summary>
/// Exact annotation evidence that a binding markup object is assigned as the member value
/// instead of invoking the framework's ordinary bind operation.
/// </summary>
public sealed class XamlBindingAssignmentInfo
{
    public XamlBindingAssignmentInfo(
        XamlSchemaAnnotationInfo annotation,
        ISymbol? target,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Target = target;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ISymbol? Target { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Target != null && Error == null;
    public string? Error { get; }
}

public sealed class XamlTemplatePartInfo
{
    public XamlTemplatePartInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? declaringType,
        string? name,
        ITypeSymbol? partType,
        bool? isRequired,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        DeclaringType = declaringType;
        Name = name;
        PartType = partType;
        IsRequired = isRequired;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? DeclaringType { get; }
    public string? Name { get; }
    public ITypeSymbol? PartType { get; }
    public bool? IsRequired { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => DeclaringType != null &&
        !string.IsNullOrWhiteSpace(Name) &&
        PartType != null &&
        Error == null;
    public string? Error { get; }
}

public sealed class XamlTemplateVisualStateInfo
{
    public XamlTemplateVisualStateInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? declaringType,
        string? name,
        string? groupName,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        DeclaringType = declaringType;
        Name = name;
        GroupName = groupName;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? DeclaringType { get; }
    public string? Name { get; }
    public string? GroupName { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => DeclaringType != null &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(GroupName) &&
        Error == null;
    public string? Error { get; }
}

public readonly struct XamlTemplateVisualStateKey :
    IEquatable<XamlTemplateVisualStateKey>
{
    public XamlTemplateVisualStateKey(string groupName, string name)
    {
        GroupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string GroupName { get; }
    public string Name { get; }

    public bool Equals(XamlTemplateVisualStateKey other) =>
        string.Equals(GroupName, other.GroupName, StringComparison.Ordinal) &&
        string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is XamlTemplateVisualStateKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(GroupName) * 397) ^
                StringComparer.Ordinal.GetHashCode(Name);
        }
    }

    public override string ToString() => GroupName + "." + Name;
}

public sealed class XamlStyleTypedPropertyInfo
{
    public XamlStyleTypedPropertyInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? declaringType,
        string? propertyName,
        IPropertySymbol? property,
        ITypeSymbol? styleTargetType,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        DeclaringType = declaringType;
        PropertyName = propertyName;
        Property = property;
        StyleTargetType = styleTargetType;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? DeclaringType { get; }
    public string? PropertyName { get; }
    public IPropertySymbol? Property { get; }
    public ITypeSymbol? StyleTargetType { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => DeclaringType != null &&
        !string.IsNullOrWhiteSpace(PropertyName) &&
        Property != null &&
        StyleTargetType != null &&
        Error == null;
    public string? Error { get; }
}

public enum XamlAttachedPropertyBrowseRuleKind
{
    TargetType,
    Children,
    AttributePresent
}

public sealed class XamlAttachedPropertyBrowseRuleInfo
{
    public XamlAttachedPropertyBrowseRuleInfo(
        XamlSchemaAnnotationInfo annotation,
        XamlAttachedPropertyBrowseRuleKind kind,
        IMethodSymbol? getter,
        ITypeSymbol? constraintType,
        bool includeDescendants,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Kind = kind;
        Getter = getter;
        ConstraintType = constraintType;
        IncludeDescendants = includeDescendants;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public XamlAttachedPropertyBrowseRuleKind Kind { get; }
    public IMethodSymbol? Getter { get; }
    public ITypeSymbol? ConstraintType { get; }
    public bool IncludeDescendants { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Getter != null &&
        Error == null &&
        (Kind == XamlAttachedPropertyBrowseRuleKind.Children ||
         ConstraintType != null);
    public string? Error { get; }
}

/// <summary>
/// Exact Roslyn projection of a type-level attribute that aliases a XAML language
/// concept to an ordinary or attachable CLR member.
/// </summary>
public sealed class XamlAliasedMemberShapeInfo
{
    public XamlAliasedMemberShapeInfo(
        XamlSchemaAnnotationInfo annotation,
        string semantic,
        string? declaredName,
        ITypeSymbol? ownerType,
        IPropertySymbol? property,
        IMethodSymbol? attachableGetter,
        IMethodSymbol? attachableSetter,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        DeclaredName = declaredName;
        OwnerType = ownerType;
        Property = property;
        AttachableGetter = attachableGetter;
        AttachableSetter = attachableSetter;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public string Semantic { get; }
    public string? DeclaredName { get; }
    public ITypeSymbol? OwnerType { get; }
    public IPropertySymbol? Property { get; }
    public IMethodSymbol? AttachableGetter { get; }
    public IMethodSymbol? AttachableSetter { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsAttachable => OwnerType != null;
    public bool IsValid => Error == null &&
        !string.IsNullOrWhiteSpace(DeclaredName) &&
        (Property != null || AttachableGetter != null || AttachableSetter != null);
    public string? Error { get; }
}

public sealed class XamlMemberDependencyInfo
{
    public XamlMemberDependencyInfo(
        XamlSchemaAnnotationInfo annotation,
        string? declaredName,
        IPropertySymbol? dependency,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        DeclaredName = declaredName;
        Dependency = dependency;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public string? DeclaredName { get; }
    public IPropertySymbol? Dependency { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Error == null &&
        !string.IsNullOrWhiteSpace(DeclaredName) &&
        Dependency != null;
    public string? Error { get; }
}

public enum XamlNameScopeIdentityKind
{
    Interface,
    DuckMethods
}

public sealed class XamlNameScopeShapeInfo
{
    public XamlNameScopeShapeInfo(
        XamlNameScopeIdentityKind identityKind,
        ITypeSymbol? identityType,
        IMethodSymbol? registerNameMethod,
        IMethodSymbol? unregisterNameMethod,
        IMethodSymbol? findNameMethod,
        IReadOnlyList<IMethodSymbol> candidates,
        string providerId,
        string? error)
    {
        IdentityKind = identityKind;
        IdentityType = identityType;
        RegisterNameMethod = registerNameMethod;
        UnregisterNameMethod = unregisterNameMethod;
        FindNameMethod = findNameMethod;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlNameScopeIdentityKind IdentityKind { get; }
    public ITypeSymbol? IdentityType { get; }
    public IMethodSymbol? RegisterNameMethod { get; }
    public IMethodSymbol? UnregisterNameMethod { get; }
    public IMethodSymbol? FindNameMethod { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public string ProviderId { get; }
    public bool IsValid => Error == null &&
        RegisterNameMethod != null &&
        UnregisterNameMethod != null &&
        FindNameMethod != null;
    public string? Error { get; }
}

public sealed class XamlMarkupExtensionOptionInfo
{
    public XamlMarkupExtensionOptionInfo(
        XamlSchemaAnnotationInfo annotation,
        bool isDefault,
        TypedConstant? optionValue,
        int priority,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Property = annotation.DeclaredOn as IPropertySymbol;
        IsDefault = isDefault;
        OptionValue = optionValue;
        Priority = priority;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public IPropertySymbol? Property { get; }
    public bool IsDefault { get; }
    public TypedConstant? OptionValue { get; }
    public int Priority { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool IsValid => Error == null &&
        Property != null &&
        (IsDefault || OptionValue.HasValue);
    public string? Error { get; }
}

/// <summary>
/// Exact Roslyn projection of a framework-registered static option selector used by
/// switch-like markup extensions. The neutral schema records the callable contract;
/// framework lowering decides when and how to invoke it.
/// </summary>
public sealed class XamlMarkupExtensionOptionSelectorShapeInfo
{
    public XamlMarkupExtensionOptionSelectorShapeInfo(
        IMethodSymbol? method,
        IReadOnlyList<IMethodSymbol> candidates,
        IReadOnlyList<XamlMarkupExtensionOptionInfo> options,
        ITypeSymbol? optionType,
        ITypeSymbol? serviceProviderType,
        string providerId,
        string? error)
    {
        Method = method;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        OptionType = optionType;
        ServiceProviderType = serviceProviderType;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public IMethodSymbol? Method { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public IReadOnlyList<XamlMarkupExtensionOptionInfo> Options { get; }
    public ITypeSymbol? OptionType { get; }
    public ITypeSymbol? ServiceProviderType { get; }
    public string ProviderId { get; }
    public bool RequiresServiceProvider => ServiceProviderType != null;
    public bool IsValid => Method != null && OptionType != null && Error == null;
    public string? Error { get; }
}

public sealed class XamlListItemInfo
{
    public XamlListItemInfo(string text, string rawText, TextSpan sourceSpan)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        RawText = rawText ?? throw new ArgumentNullException(nameof(rawText));
        SourceSpan = sourceSpan;
    }

    public string Text { get; }
    public string RawText { get; }
    public TextSpan SourceSpan { get; }
}

/// <summary>
/// Canonical, reusable projection of an attributed list-string grammar. Separator buckets
/// are prepared once so every split avoids reflection and repeated metadata interpretation.
/// </summary>
public sealed class XamlListSplitInfo
{
    private const int RemoveEmptyEntries = 1;
    private const int TrimEntries = 2;
    private readonly IReadOnlyDictionary<char, IReadOnlyList<string>> _separatorBuckets;

    public XamlListSplitInfo(
        XamlSchemaAnnotationInfo annotation,
        ITypeSymbol declaringType,
        IReadOnlyList<string> separators,
        int splitOptions,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        Separators = separators ?? throw new ArgumentNullException(nameof(separators));
        SplitOptions = splitOptions;
        Error = error;

        var buckets = new Dictionary<char, List<string>>();
        for (var index = 0; index < separators.Count; index++)
        {
            var separator = separators[index];
            if (string.IsNullOrEmpty(separator)) continue;
            if (!buckets.TryGetValue(separator[0], out var values))
            {
                values = new List<string>();
                buckets.Add(separator[0], values);
            }
            if (!values.Contains(separator)) values.Add(separator);
        }
        var frozen = new Dictionary<char, IReadOnlyList<string>>();
        foreach (var pair in buckets)
        {
            pair.Value.Sort(static (left, right) =>
            {
                var length = right.Length.CompareTo(left.Length);
                return length != 0
                    ? length
                    : string.Compare(left, right, StringComparison.Ordinal);
            });
            frozen.Add(pair.Key, pair.Value.ToArray());
        }
        _separatorBuckets = frozen;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public ITypeSymbol DeclaringType { get; }
    public IReadOnlyList<string> Separators { get; }
    public int SplitOptions { get; }
    public string ProviderId => Annotation.ProviderId;
    public bool RemovesEmptyEntries => (SplitOptions & RemoveEmptyEntries) != 0;
    public bool TrimsEntries => (SplitOptions & TrimEntries) != 0;
    public bool IsValid => Error == null;
    public string? Error { get; }

    public IReadOnlyList<XamlListItemInfo> Split(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (!IsValid) throw new InvalidOperationException(Error);
        var result = new List<XamlListItemInfo>();
        var itemStart = 0;
        for (var index = 0; index < text.Length;)
        {
            var separatorLength = MatchSeparator(text, index);
            if (separatorLength == 0)
            {
                index++;
                continue;
            }
            AddItem(text, itemStart, index - itemStart, result);
            index += separatorLength;
            itemStart = index;
        }
        AddItem(text, itemStart, text.Length - itemStart, result);
        return result;
    }

    private int MatchSeparator(string text, int index)
    {
        if (!_separatorBuckets.TryGetValue(text[index], out var candidates)) return 0;
        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var candidate = candidates[candidateIndex];
            if (index + candidate.Length > text.Length) continue;
            var matches = true;
            for (var offset = 1; offset < candidate.Length; offset++)
            {
                if (text[index + offset] == candidate[offset]) continue;
                matches = false;
                break;
            }
            if (matches) return candidate.Length;
        }
        return 0;
    }

    private void AddItem(
        string source,
        int start,
        int length,
        ICollection<XamlListItemInfo> result)
    {
        var raw = source.Substring(start, length);
        var itemStart = start;
        var itemLength = length;
        if (TrimsEntries)
        {
            while (itemLength != 0 && char.IsWhiteSpace(source[itemStart]))
            {
                itemStart++;
                itemLength--;
            }
            while (itemLength != 0 &&
                   char.IsWhiteSpace(source[itemStart + itemLength - 1]))
                itemLength--;
        }
        if (RemovesEmptyEntries && itemLength == 0) return;
        var value = itemStart == start && itemLength == length
            ? raw
            : source.Substring(itemStart, itemLength);
        result.Add(new XamlListItemInfo(
            value,
            raw,
            new TextSpan(itemStart, itemLength)));
    }
}

[Flags]
public enum XamlSchemaAttributeTargets
{
    None = 0,
    Assembly = 1,
    Type = 2,
    Member = 4,
    Parameter = 8,
    Module = 16,
    Any = Assembly | Type | Member | Parameter | Module
}

public enum XamlSchemaAttributeValueSource
{
    None,
    ConstructorArgument,
    NamedArgument
}

/// <summary>
/// Declarative mapping from a framework attribute metadata name to a neutral XAML semantic.
/// </summary>
public sealed class XamlSchemaAttributeRule
{
    public XamlSchemaAttributeRule(
        string attributeMetadataName,
        string semantic,
        XamlSchemaAttributeTargets targets,
        bool inherited = false,
        XamlSchemaAttributeValueSource valueSource = XamlSchemaAttributeValueSource.None,
        int constructorArgumentIndex = -1,
        string? namedArgument = null,
        bool allowMultiple = false)
    {
        AttributeMetadataName = attributeMetadataName ?? throw new ArgumentNullException(nameof(attributeMetadataName));
        Semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        Targets = targets;
        Inherited = inherited;
        ValueSource = valueSource;
        ConstructorArgumentIndex = constructorArgumentIndex;
        NamedArgument = namedArgument;
        AllowMultiple = allowMultiple;
    }

    public string AttributeMetadataName { get; }
    public string Semantic { get; }
    public XamlSchemaAttributeTargets Targets { get; }
    public bool Inherited { get; }
    public XamlSchemaAttributeValueSource ValueSource { get; }
    public int ConstructorArgumentIndex { get; }
    public string? NamedArgument { get; }
    public bool AllowMultiple { get; }
}

/// <summary>
/// Symbol-shape conventions that a framework elects to recognize as XAML schema behavior.
/// Every enabled convention is deterministic and inspectable by tooling.
/// </summary>
[Flags]
public enum XamlSymbolShapeFeatures
{
    None = 0,
    MarkupExtensionSuffixes = 1 << 0,
    CollectionAddMethods = 1 << 1,
    AddChildInterfaces = 1 << 2,
    AttachedAccessorPrefixes = 1 << 3,
    RuntimeNameFallback = 1 << 4,
    CollectionInference = 1 << 5,
    PropertySystem = 1 << 6,
    ImplicitDictionaryKeys = 1 << 7,
    ResourceMemberRoles = 1 << 8,
    ProfileTextSyntaxTypes = 1 << 9,
    PseudoContentMembers = 1 << 10,
    GetterOnlyAttachedCollections = 1 << 11,
    MarkupExtensionBaseTypes = 1 << 12,
    MarkupExtensionInterfaces = 1 << 13,
    MarkupExtensionCallableNames = 1 << 14,
    MarkupExtensionServiceProviderTypes = 1 << 15,
    MarkupExtensionAvailableServices = 1 << 16,
    MarkupExtensionServiceDeclaration = 1 << 17,
    SetValueHandlerEventArgs = 1 << 18,
    ValueSerializerBaseTypes = 1 << 19,
    ValueSerializerContextTypes = 1 << 20,
    ValueSerializerCanConvertToStringNames = 1 << 21,
    ValueSerializerConvertToStringNames = 1 << 22,
    NameScopeInterfaces = 1 << 23,
    NameScopeRegisterNames = 1 << 24,
    NameScopeUnregisterNames = 1 << 25,
    NameScopeFindNames = 1 << 26,
    NameScopeDuckTyping = 1 << 27,
    MarkupExtensionOptionSelectorNames = 1 << 28,
    MarkupExtensionOptionSelectorServiceProviderTypes = 1 << 29,
    DesignerSerializationMethods = 1 << 30,
    MarkupExtensionReceivers = unchecked((int)0x80000000)
}

public sealed class XamlSymbolShapePolicy
{
    public static XamlSymbolShapePolicy Default { get; } = new XamlSymbolShapePolicy();

    public XamlSymbolShapePolicy(
        IReadOnlyList<string>? markupExtensionSuffixes = null,
        IReadOnlyList<string>? collectionAddMethodNames = null,
        IReadOnlyList<string>? addChildInterfaceMetadataNames = null,
        string? attachedGetterPrefix = null,
        string? attachedSetterPrefix = null,
        string? runtimeNameFallback = null,
        bool? inferCollectionsFromAddMethods = null,
        string? propertyIdentifierSuffix = null,
        string? propertyIdentifierTypeMetadataName = null,
        string? propertySetterMethodName = null,
        IReadOnlyDictionary<string, string>? implicitDictionaryKeyMembers = null,
        IReadOnlyDictionary<string, XamlResourceMemberRole>? resourceMemberRoles = null,
        IReadOnlyList<string>? profileTextSyntaxTypeMetadataNames = null,
        IReadOnlyDictionary<string, XamlPseudoMemberDefinition>? pseudoContentMembers = null,
        bool? inferGetterOnlyAttachedCollections = null,
        IReadOnlyList<string>? markupExtensionBaseTypeMetadataNames = null,
        IReadOnlyList<string>? markupExtensionInterfaceMetadataNames = null,
        IReadOnlyList<string>? markupExtensionProvideValueMethodNames = null,
        IReadOnlyList<string>? markupExtensionServiceProviderTypeMetadataNames = null,
        IReadOnlyList<string>? markupExtensionAvailableServiceTypeMetadataNames = null,
        bool? requireMarkupExtensionServiceDeclaration = null,
        IReadOnlyDictionary<string, string>? setValueHandlerEventArgsTypeMetadataNames = null,
        IReadOnlyList<string>? valueSerializerBaseTypeMetadataNames = null,
        IReadOnlyList<string>? valueSerializerContextTypeMetadataNames = null,
        IReadOnlyList<string>? valueSerializerCanConvertToStringMethodNames = null,
        IReadOnlyList<string>? valueSerializerConvertToStringMethodNames = null,
        IReadOnlyList<string>? nameScopeInterfaceMetadataNames = null,
        IReadOnlyList<string>? nameScopeRegisterMethodNames = null,
        IReadOnlyList<string>? nameScopeUnregisterMethodNames = null,
        IReadOnlyList<string>? nameScopeFindMethodNames = null,
        bool? inferNameScopeFromMethods = null,
        IReadOnlyList<string>? markupExtensionOptionSelectorMethodNames = null,
        IReadOnlyList<string>? markupExtensionOptionSelectorServiceProviderTypeMetadataNames = null,
        bool? inferDesignerSerializationMethods = null,
        string? shouldSerializeMethodPrefix = null,
        string? resetMethodPrefix = null,
        IReadOnlyList<string>? markupExtensionReceiverInterfaceMetadataNames = null,
        IReadOnlyList<string>? markupExtensionReceiverMethodNames = null,
        IReadOnlyList<string>? markupExtensionReceiverMarkupExtensionTypeMetadataNames = null,
        IReadOnlyList<string>? markupExtensionReceiverServiceProviderTypeMetadataNames = null,
        bool? inferMarkupExtensionReceiversFromMethods = null)
    {
        var declared = XamlSymbolShapeFeatures.None;
        if (markupExtensionSuffixes != null) declared |= XamlSymbolShapeFeatures.MarkupExtensionSuffixes;
        if (collectionAddMethodNames != null) declared |= XamlSymbolShapeFeatures.CollectionAddMethods;
        if (addChildInterfaceMetadataNames != null) declared |= XamlSymbolShapeFeatures.AddChildInterfaces;
        if (attachedGetterPrefix != null || attachedSetterPrefix != null)
            declared |= XamlSymbolShapeFeatures.AttachedAccessorPrefixes;
        if (runtimeNameFallback != null) declared |= XamlSymbolShapeFeatures.RuntimeNameFallback;
        if (inferCollectionsFromAddMethods.HasValue) declared |= XamlSymbolShapeFeatures.CollectionInference;
        if (propertyIdentifierSuffix != null ||
            propertyIdentifierTypeMetadataName != null ||
            propertySetterMethodName != null)
            declared |= XamlSymbolShapeFeatures.PropertySystem;
        if (implicitDictionaryKeyMembers != null) declared |= XamlSymbolShapeFeatures.ImplicitDictionaryKeys;
        if (resourceMemberRoles != null) declared |= XamlSymbolShapeFeatures.ResourceMemberRoles;
        if (profileTextSyntaxTypeMetadataNames != null) declared |= XamlSymbolShapeFeatures.ProfileTextSyntaxTypes;
        if (pseudoContentMembers != null) declared |= XamlSymbolShapeFeatures.PseudoContentMembers;
        if (inferGetterOnlyAttachedCollections.HasValue)
            declared |= XamlSymbolShapeFeatures.GetterOnlyAttachedCollections;
        if (markupExtensionBaseTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionBaseTypes;
        if (markupExtensionInterfaceMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionInterfaces;
        if (markupExtensionProvideValueMethodNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionCallableNames;
        if (markupExtensionServiceProviderTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionServiceProviderTypes;
        if (markupExtensionAvailableServiceTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionAvailableServices;
        if (requireMarkupExtensionServiceDeclaration.HasValue)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionServiceDeclaration;
        if (setValueHandlerEventArgsTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.SetValueHandlerEventArgs;
        if (valueSerializerBaseTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.ValueSerializerBaseTypes;
        if (valueSerializerContextTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.ValueSerializerContextTypes;
        if (valueSerializerCanConvertToStringMethodNames != null)
            declared |= XamlSymbolShapeFeatures.ValueSerializerCanConvertToStringNames;
        if (valueSerializerConvertToStringMethodNames != null)
            declared |= XamlSymbolShapeFeatures.ValueSerializerConvertToStringNames;
        if (nameScopeInterfaceMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.NameScopeInterfaces;
        if (nameScopeRegisterMethodNames != null)
            declared |= XamlSymbolShapeFeatures.NameScopeRegisterNames;
        if (nameScopeUnregisterMethodNames != null)
            declared |= XamlSymbolShapeFeatures.NameScopeUnregisterNames;
        if (nameScopeFindMethodNames != null)
            declared |= XamlSymbolShapeFeatures.NameScopeFindNames;
        if (inferNameScopeFromMethods.HasValue)
            declared |= XamlSymbolShapeFeatures.NameScopeDuckTyping;
        if (markupExtensionOptionSelectorMethodNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionOptionSelectorNames;
        if (markupExtensionOptionSelectorServiceProviderTypeMetadataNames != null)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionOptionSelectorServiceProviderTypes;
        if (inferDesignerSerializationMethods.HasValue ||
            shouldSerializeMethodPrefix != null ||
            resetMethodPrefix != null)
            declared |= XamlSymbolShapeFeatures.DesignerSerializationMethods;
        if (markupExtensionReceiverInterfaceMetadataNames != null ||
            markupExtensionReceiverMethodNames != null ||
            markupExtensionReceiverMarkupExtensionTypeMetadataNames != null ||
            markupExtensionReceiverServiceProviderTypeMetadataNames != null ||
            inferMarkupExtensionReceiversFromMethods.HasValue)
            declared |= XamlSymbolShapeFeatures.MarkupExtensionReceivers;

        DeclaredFeatures = declared;
        MarkupExtensionSuffixes = markupExtensionSuffixes ?? new[] { "Extension" };
        CollectionAddMethodNames = collectionAddMethodNames ?? new[] { "Add" };
        AddChildInterfaceMetadataNames = addChildInterfaceMetadataNames ?? Array.Empty<string>();
        AttachedGetterPrefix = attachedGetterPrefix ?? "Get";
        AttachedSetterPrefix = attachedSetterPrefix ?? "Set";
        RuntimeNameFallback = runtimeNameFallback;
        InferCollectionsFromAddMethods = inferCollectionsFromAddMethods ?? true;
        PropertyIdentifierSuffix = propertyIdentifierSuffix;
        PropertyIdentifierTypeMetadataName = propertyIdentifierTypeMetadataName;
        PropertySetterMethodName = propertySetterMethodName;
        ImplicitDictionaryKeyMembers = implicitDictionaryKeyMembers ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        ResourceMemberRoles = resourceMemberRoles ??
            new Dictionary<string, XamlResourceMemberRole>(StringComparer.Ordinal);
        ProfileTextSyntaxTypeMetadataNames = profileTextSyntaxTypeMetadataNames ?? Array.Empty<string>();
        PseudoContentMembers = pseudoContentMembers ??
            new Dictionary<string, XamlPseudoMemberDefinition>(StringComparer.Ordinal);
        InferGetterOnlyAttachedCollections = inferGetterOnlyAttachedCollections ?? false;
        MarkupExtensionBaseTypeMetadataNames = markupExtensionBaseTypeMetadataNames ?? Array.Empty<string>();
        MarkupExtensionInterfaceMetadataNames = markupExtensionInterfaceMetadataNames ?? Array.Empty<string>();
        MarkupExtensionProvideValueMethodNames = markupExtensionProvideValueMethodNames ?? new[] { "ProvideValue" };
        MarkupExtensionServiceProviderTypeMetadataNames =
            markupExtensionServiceProviderTypeMetadataNames ?? Array.Empty<string>();
        MarkupExtensionAvailableServiceTypeMetadataNames =
            markupExtensionAvailableServiceTypeMetadataNames ?? Array.Empty<string>();
        RequireMarkupExtensionServiceDeclaration = requireMarkupExtensionServiceDeclaration ?? false;
        SetValueHandlerEventArgsTypeMetadataNames = setValueHandlerEventArgsTypeMetadataNames ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        ValueSerializerBaseTypeMetadataNames =
            valueSerializerBaseTypeMetadataNames ?? Array.Empty<string>();
        ValueSerializerContextTypeMetadataNames =
            valueSerializerContextTypeMetadataNames ?? Array.Empty<string>();
        ValueSerializerCanConvertToStringMethodNames =
            valueSerializerCanConvertToStringMethodNames ?? Array.Empty<string>();
        ValueSerializerConvertToStringMethodNames =
            valueSerializerConvertToStringMethodNames ?? Array.Empty<string>();
        NameScopeInterfaceMetadataNames =
            nameScopeInterfaceMetadataNames ?? Array.Empty<string>();
        NameScopeRegisterMethodNames =
            nameScopeRegisterMethodNames ?? new[] { "RegisterName" };
        NameScopeUnregisterMethodNames =
            nameScopeUnregisterMethodNames ?? new[] { "UnregisterName" };
        NameScopeFindMethodNames =
            nameScopeFindMethodNames ?? new[] { "FindName", "FindByName" };
        InferNameScopeFromMethods = inferNameScopeFromMethods ?? false;
        MarkupExtensionOptionSelectorMethodNames =
            markupExtensionOptionSelectorMethodNames ?? Array.Empty<string>();
        MarkupExtensionOptionSelectorServiceProviderTypeMetadataNames =
            markupExtensionOptionSelectorServiceProviderTypeMetadataNames ?? Array.Empty<string>();
        InferDesignerSerializationMethods =
            inferDesignerSerializationMethods ?? false;
        ShouldSerializeMethodPrefix =
            shouldSerializeMethodPrefix ?? "ShouldSerialize";
        ResetMethodPrefix = resetMethodPrefix ?? "Reset";
        MarkupExtensionReceiverInterfaceMetadataNames =
            markupExtensionReceiverInterfaceMetadataNames ?? Array.Empty<string>();
        MarkupExtensionReceiverMethodNames =
            markupExtensionReceiverMethodNames ?? new[] { "ReceiveMarkupExtension" };
        MarkupExtensionReceiverMarkupExtensionTypeMetadataNames =
            markupExtensionReceiverMarkupExtensionTypeMetadataNames ?? Array.Empty<string>();
        MarkupExtensionReceiverServiceProviderTypeMetadataNames =
            markupExtensionReceiverServiceProviderTypeMetadataNames ?? Array.Empty<string>();
        InferMarkupExtensionReceiversFromMethods =
            inferMarkupExtensionReceiversFromMethods ?? false;
    }

    public XamlSymbolShapeFeatures DeclaredFeatures { get; }
    public IReadOnlyList<string> MarkupExtensionSuffixes { get; }
    public IReadOnlyList<string> CollectionAddMethodNames { get; }
    public IReadOnlyList<string> AddChildInterfaceMetadataNames { get; }
    public string AttachedGetterPrefix { get; }
    public string AttachedSetterPrefix { get; }
    public string? RuntimeNameFallback { get; }
    public bool InferCollectionsFromAddMethods { get; }
    /// <summary>Optional profile-declared property-system shape; null disables inference.</summary>
    public string? PropertyIdentifierSuffix { get; }
    public string? PropertyIdentifierTypeMetadataName { get; }
    public string? PropertySetterMethodName { get; }
    /// <summary>
    /// Profile-owned, symbol-validated implicit dictionary-key conventions keyed by CLR
    /// metadata type name. This models framework behavior that is not published as an
    /// attribute without teaching the framework-neutral binder a framework type.
    /// </summary>
    public IReadOnlyDictionary<string, string> ImplicitDictionaryKeyMembers { get; }
    /// <summary>
    /// Profile-owned resource vocabulary keyed by CLR member name. The neutral compiler
    /// consumes roles rather than framework type names or spelling conventions.
    /// </summary>
    public IReadOnlyDictionary<string, XamlResourceMemberRole> ResourceMemberRoles { get; }
    /// <summary>
    /// Canonical CLR type names whose object-element text is parsed by the selected
    /// framework profile. This is schema evidence; conversion remains in the profile's
    /// structured emitter and never falls back to reflection or generated C# text.
    /// </summary>
    public IReadOnlyList<string> ProfileTextSyntaxTypeMetadataNames { get; }
    /// <summary>Parser-owned content slots intentionally absent from the CLR object model.</summary>
    public IReadOnlyDictionary<string, XamlPseudoMemberDefinition> PseudoContentMembers { get; }
    public bool InferGetterOnlyAttachedCollections { get; }
    /// <summary>Exact registered base types that establish markup-extension identity.</summary>
    public IReadOnlyList<string> MarkupExtensionBaseTypeMetadataNames { get; }
    /// <summary>Exact registered interfaces that establish markup-extension identity.</summary>
    public IReadOnlyList<string> MarkupExtensionInterfaceMetadataNames { get; }
    /// <summary>Registered callable names used to validate extension and duck-typed shapes.</summary>
    public IReadOnlyList<string> MarkupExtensionProvideValueMethodNames { get; }
    /// <summary>Exact permitted service-provider parameter types for context-aware callables.</summary>
    public IReadOnlyList<string> MarkupExtensionServiceProviderTypeMetadataNames { get; }
    /// <summary>Exact service types the profile guarantees through its runtime provider.</summary>
    public IReadOnlyList<string> MarkupExtensionAvailableServiceTypeMetadataNames { get; }
    public bool RequireMarkupExtensionServiceDeclaration { get; }
    public IReadOnlyDictionary<string, string> SetValueHandlerEventArgsTypeMetadataNames { get; }
    public IReadOnlyList<string> ValueSerializerBaseTypeMetadataNames { get; }
    public IReadOnlyList<string> ValueSerializerContextTypeMetadataNames { get; }
    public IReadOnlyList<string> ValueSerializerCanConvertToStringMethodNames { get; }
    public IReadOnlyList<string> ValueSerializerConvertToStringMethodNames { get; }
    public IReadOnlyList<string> NameScopeInterfaceMetadataNames { get; }
    public IReadOnlyList<string> NameScopeRegisterMethodNames { get; }
    public IReadOnlyList<string> NameScopeUnregisterMethodNames { get; }
    public IReadOnlyList<string> NameScopeFindMethodNames { get; }
    public bool InferNameScopeFromMethods { get; }
    public IReadOnlyList<string> MarkupExtensionOptionSelectorMethodNames { get; }
    public IReadOnlyList<string> MarkupExtensionOptionSelectorServiceProviderTypeMetadataNames { get; }
    public bool InferDesignerSerializationMethods { get; }
    public string ShouldSerializeMethodPrefix { get; }
    public string ResetMethodPrefix { get; }
    public IReadOnlyList<string> MarkupExtensionReceiverInterfaceMetadataNames { get; }
    public IReadOnlyList<string> MarkupExtensionReceiverMethodNames { get; }
    public IReadOnlyList<string> MarkupExtensionReceiverMarkupExtensionTypeMetadataNames { get; }
    public IReadOnlyList<string> MarkupExtensionReceiverServiceProviderTypeMetadataNames { get; }
    public bool InferMarkupExtensionReceiversFromMethods { get; }
}

/// <summary>
/// One provider value participating in an incompatible equal-priority symbol-shape
/// configuration. Values are canonical diagnostic representations; compiler behavior
/// continues to consume the original typed policy values.
/// </summary>
public sealed class XamlSymbolShapeConflictCandidate
{
    public XamlSymbolShapeConflictCandidate(
        string providerId,
        int providerPriority,
        string value)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ProviderPriority = providerPriority;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ProviderId { get; }
    public int ProviderPriority { get; }
    public string Value { get; }
}

/// <summary>Canonical evidence for an incompatible winning symbol-shape configuration.</summary>
public sealed class XamlSymbolShapeConflictInfo
{
    public XamlSymbolShapeConflictInfo(
        XamlSymbolShapeFeatures feature,
        string? key,
        IReadOnlyList<XamlSymbolShapeConflictCandidate> candidates)
    {
        Feature = feature;
        Key = key;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
    }

    public XamlSymbolShapeFeatures Feature { get; }
    public string? Key { get; }
    public IReadOnlyList<XamlSymbolShapeConflictCandidate> Candidates { get; }
}

public sealed class XamlPseudoMemberDefinition
{
    public XamlPseudoMemberDefinition(
        string name,
        string valueTypeMetadataName,
        XamlMemberKind kind,
        string semantic)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ValueTypeMetadataName = valueTypeMetadataName ?? throw new ArgumentNullException(nameof(valueTypeMetadataName));
        Kind = kind;
        Semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
    }

    public string Name { get; }
    public string ValueTypeMetadataName { get; }
    public XamlMemberKind Kind { get; }
    public string Semantic { get; }
}

/// <summary>Framework-neutral semantic role of a CLR member in resource lookup.</summary>
public enum XamlResourceMemberRole
{
    None,
    LexicalResources,
    MergedDictionaries,
    ThemeDictionaries,
    Source
}

public enum XamlResourceReferenceRole
{
    None,
    Static,
    Dynamic
}

public interface IXamlSchemaMetadataProvider
{
    string MetadataProviderId { get; }
    int MetadataPriority { get; }
    IReadOnlyList<XamlSchemaAttributeRule> AttributeRules { get; }
    XamlSymbolShapePolicy SymbolShapePolicy { get; }
}

/// <summary>Optional type-system service exposing provider-configuration conflicts.</summary>
public interface IXamlSymbolShapeConflictProvider
{
    IReadOnlyList<XamlSymbolShapeConflictInfo> SymbolShapeConflicts { get; }
}

public sealed class XamlSyntheticMemberDefinition
{
    public XamlSyntheticMemberDefinition(
        string name,
        string valueTypeMetadataName = "System.Object",
        XamlMemberKind kind = XamlMemberKind.Property,
        bool canWrite = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ValueTypeMetadataName = valueTypeMetadataName ?? throw new ArgumentNullException(nameof(valueTypeMetadataName));
        Kind = kind;
        CanWrite = canWrite;
    }
    public string Name { get; }
    public string ValueTypeMetadataName { get; }
    public XamlMemberKind Kind { get; }
    public bool CanWrite { get; }
}

public sealed class XamlSyntheticTypeDefinition
{
    public XamlSyntheticTypeDefinition(
        string namespaceUri,
        string name,
        bool isMarkupExtension = false,
        string? returnTypeMetadataName = null,
        IReadOnlyList<IReadOnlyList<string>>? constructors = null,
        IReadOnlyList<XamlSyntheticMemberDefinition>? members = null,
        XamlResourceReferenceRole resourceReferenceRole = XamlResourceReferenceRole.None,
        XamlExpressionRole expressionRole = XamlExpressionRole.None)
    {
        NamespaceUri = namespaceUri ?? throw new ArgumentNullException(nameof(namespaceUri));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsMarkupExtension = isMarkupExtension;
        ReturnTypeMetadataName = returnTypeMetadataName;
        Constructors = constructors ?? Array.Empty<IReadOnlyList<string>>();
        Members = members ?? Array.Empty<XamlSyntheticMemberDefinition>();
        ResourceReferenceRole = resourceReferenceRole;
        ExpressionRole = expressionRole;
    }
    public string NamespaceUri { get; }
    public string Name { get; }
    public bool IsMarkupExtension { get; }
    public string? ReturnTypeMetadataName { get; }
    public IReadOnlyList<IReadOnlyList<string>> Constructors { get; }
    public IReadOnlyList<XamlSyntheticMemberDefinition> Members { get; }
    public XamlResourceReferenceRole ResourceReferenceRole { get; }
    public XamlExpressionRole ExpressionRole { get; }
}

/// <summary>Profile vocabulary types that do not require a public CLR implementation type.</summary>
public interface IXamlSyntheticSchemaProvider
{
    IReadOnlyList<XamlSyntheticTypeDefinition> SyntheticTypes { get; }
}

/// <summary>A profile-owned directive layered over the shared XAML language namespace.</summary>
public sealed class XamlDialectDirectiveDefinition
{
    public XamlDialectDirectiveDefinition(string namespaceUri, string name, XamlAllowedLocation allowedLocation)
    {
        NamespaceUri = namespaceUri ?? throw new ArgumentNullException(nameof(namespaceUri));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        AllowedLocation = allowedLocation;
    }

    public string NamespaceUri { get; }
    public string Name { get; }
    public XamlAllowedLocation AllowedLocation { get; }
}

/// <summary>Optional framework profile vocabulary for non-MS-XAML directives.</summary>
public interface IXamlDialectDirectiveProvider
{
    IReadOnlyList<XamlDialectDirectiveDefinition> DialectDirectives { get; }
}

/// <summary>Optional type-system service consumed by the framework-neutral binder.</summary>
public interface IXamlDialectDirectiveResolver
{
    bool TryResolveDirective(string namespaceUri, string name, out XamlDialectDirectiveDefinition? definition);
}

/// <summary>Roslyn evidence for an assembly-level XML namespace to CLR namespace mapping.</summary>
public sealed class XamlNamespaceDefinitionInfo
{
    public XamlNamespaceDefinitionInfo(
        string xmlNamespace,
        string clrNamespace,
        IAssemblySymbol assembly,
        AttributeData attribute,
        string providerId,
        int providerPriority)
    {
        XmlNamespace = xmlNamespace ?? throw new ArgumentNullException(nameof(xmlNamespace));
        ClrNamespace = clrNamespace ?? throw new ArgumentNullException(nameof(clrNamespace));
        Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ProviderPriority = providerPriority;
    }

    public string XmlNamespace { get; }
    public string ClrNamespace { get; }
    public IAssemblySymbol Assembly { get; }
    public AttributeData Attribute { get; }
    public string ProviderId { get; }
    public int ProviderPriority { get; }
}

public sealed class XamlNamespacePrefixInfo
{
    public XamlNamespacePrefixInfo(string xmlNamespace, string prefix, IAssemblySymbol assembly, AttributeData attribute, string providerId)
    {
        XmlNamespace = xmlNamespace;
        Prefix = prefix;
        Assembly = assembly;
        Attribute = attribute;
        ProviderId = providerId;
    }
    public string XmlNamespace { get; }
    public string Prefix { get; }
    public IAssemblySymbol Assembly { get; }
    public AttributeData Attribute { get; }
    public string ProviderId { get; }
}

public sealed class XamlNamespaceCompatibilityInfo
{
    public XamlNamespaceCompatibilityInfo(string oldNamespace, string newNamespace, IAssemblySymbol assembly, AttributeData attribute, string providerId)
    {
        OldNamespace = oldNamespace;
        NewNamespace = newNamespace;
        Assembly = assembly;
        Attribute = attribute;
        ProviderId = providerId;
    }
    public string OldNamespace { get; }
    public string NewNamespace { get; }
    public IAssemblySymbol Assembly { get; }
    public AttributeData Attribute { get; }
    public string ProviderId { get; }
}

/// <summary>Optional schema service for tooling and framework-neutral namespace resolution.</summary>
public interface IXamlNamespaceMetadataResolver
{
    IReadOnlyList<XamlNamespaceDefinitionInfo> NamespaceDefinitions { get; }
    IReadOnlyList<XamlNamespacePrefixInfo> NamespacePrefixes { get; }
    IReadOnlyList<XamlNamespaceCompatibilityInfo> NamespaceCompatibilities { get; }
    IReadOnlyList<XamlSchemaAnnotationInfo> AssemblyAnnotations { get; }
}

/// <summary>
/// A recognized attribute together with its canonical Roslyn evidence and inheritance origin.
/// </summary>
public sealed class XamlSchemaAnnotationInfo
{
    public XamlSchemaAnnotationInfo(
        string semantic,
        AttributeData attribute,
        ISymbol declaredOn,
        string providerId,
        int providerPriority,
        string? value,
        TypedConstant? valueConstant,
        int inheritanceDepth,
        bool allowMultiple)
    {
        Semantic = semantic;
        Attribute = attribute;
        DeclaredOn = declaredOn;
        ProviderId = providerId;
        ProviderPriority = providerPriority;
        Value = value;
        ValueConstant = valueConstant;
        InheritanceDepth = inheritanceDepth;
        AllowMultiple = allowMultiple;
    }

    public string Semantic { get; }
    public AttributeData Attribute { get; }
    public ISymbol DeclaredOn { get; }
    public string ProviderId { get; }
    public int ProviderPriority { get; }
    public string? Value { get; }
    public TypedConstant? ValueConstant { get; }
    public int InheritanceDepth { get; }
    public bool IsInherited => InheritanceDepth != 0;
    public bool AllowMultiple { get; }
}

/// <summary>
/// Effective boolean schema metadata together with the exact Roslyn attribute evidence that
/// produced it. Presence-only attributes use <see langword="true"/>; value-bearing attributes
/// retain an explicit <see langword="false"/> so derived types can override inherited policy.
/// </summary>
public sealed class XamlSchemaBooleanInfo
{
    public XamlSchemaBooleanInfo(
        string semantic,
        bool value,
        XamlSchemaAnnotationInfo annotation,
        string? error = null)
    {
        Semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        Value = value;
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Error = error;
    }

    public string Semantic { get; }
    public bool Value { get; }
    public XamlSchemaAnnotationInfo Annotation { get; }
    public string? Error { get; }
    public bool IsValid => Error == null;
}

/// <summary>
/// Canonical Roslyn shape for a deferred-load contract declared on a type or member.
/// Loader invocation remains profile-owned, but profiles consume these exact symbols rather
/// than rediscovering a loader through reflection.
/// </summary>
public sealed class XamlDeferringLoaderShapeInfo
{
    public XamlDeferringLoaderShapeInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? loaderType,
        ITypeSymbol? contentType,
        IMethodSymbol? constructor,
        IMethodSymbol? loadMethod,
        IMethodSymbol? saveMethod,
        IReadOnlyList<IMethodSymbol> loadCandidates,
        IReadOnlyList<IMethodSymbol> saveCandidates,
        string providerId,
        string? loaderTypeName,
        string? contentTypeName,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        LoaderType = loaderType;
        ContentType = contentType;
        Constructor = constructor;
        LoadMethod = loadMethod;
        SaveMethod = saveMethod;
        LoadCandidates = loadCandidates ?? Array.Empty<IMethodSymbol>();
        SaveCandidates = saveCandidates ?? Array.Empty<IMethodSymbol>();
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        LoaderTypeName = loaderTypeName;
        ContentTypeName = contentTypeName;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? LoaderType { get; }
    public ITypeSymbol? ContentType { get; }
    public IMethodSymbol? Constructor { get; }
    public IMethodSymbol? LoadMethod { get; }
    public IMethodSymbol? SaveMethod { get; }
    public IReadOnlyList<IMethodSymbol> LoadCandidates { get; }
    public IReadOnlyList<IMethodSymbol> SaveCandidates { get; }
    public string ProviderId { get; }
    public string? LoaderTypeName { get; }
    public string? ContentTypeName { get; }
    public string? Error { get; }
    public bool IsValid => Error == null &&
        LoaderType != null &&
        ContentType != null &&
        Constructor != null &&
        LoadMethod != null &&
        SaveMethod != null;
}

public sealed class XamlMarkupBracketPairInfo
{
    public XamlMarkupBracketPairInfo(
        XamlSchemaAnnotationInfo annotation,
        char openingBracket,
        char closingBracket,
        string? error = null)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        OpeningBracket = openingBracket;
        ClosingBracket = closingBracket;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public char OpeningBracket { get; }
    public char ClosingBracket { get; }
    public string? Error { get; }
    public bool IsValid => Error == null;
}

/// <summary>
/// Canonical Roslyn evidence for one repeatable content-wrapper declaration on a collection.
/// Invalid declarations remain visible so use sites receive XAML-located diagnostics.
/// </summary>
public sealed class XamlContentWrapperShapeInfo
{
    public XamlContentWrapperShapeInfo(
        XamlSchemaAnnotationInfo annotation,
        XamlTypeInfo? wrapperType,
        IMethodSymbol? constructor,
        XamlMemberInfo? contentMember,
        string providerId,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        WrapperType = wrapperType;
        Constructor = constructor;
        ContentMember = contentMember;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public XamlTypeInfo? WrapperType { get; }
    public IMethodSymbol? Constructor { get; }
    public XamlMemberInfo? ContentMember { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid =>
        WrapperType != null &&
        Constructor != null &&
        ContentMember != null &&
        Error == null;
    public ITypeSymbol? ContentValueType => ContentMember?.ValueType.Symbol;
}

/// <summary>
/// Canonical save-path mapping from a serializable property to one constructor parameter.
/// The load binder does not reinterpret ordinary property assignment through this descriptor.
/// </summary>
public sealed class XamlConstructorArgumentShapeInfo
{
    public XamlConstructorArgumentShapeInfo(
        XamlSchemaAnnotationInfo annotation,
        string argumentName,
        IMethodSymbol? constructor,
        IParameterSymbol? parameter,
        IReadOnlyList<IMethodSymbol> candidates,
        string providerId,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        ArgumentName = argumentName ?? throw new ArgumentNullException(nameof(argumentName));
        Constructor = constructor;
        Parameter = parameter;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public string ArgumentName { get; }
    public IMethodSymbol? Constructor { get; }
    public IParameterSymbol? Parameter { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid => Constructor != null && Parameter != null && Error == null;
}

public sealed class XamlCollectionShapeInfo
{
    public XamlCollectionShapeInfo(IMethodSymbol addMethod, bool isDictionary)
    {
        AddMethod = addMethod;
        IsDictionary = isDictionary;
    }

    public IMethodSymbol AddMethod { get; }
    public bool IsDictionary { get; }
    public ITypeSymbol ItemType => AddMethod.Parameters[AddMethod.Parameters.Length - 1].Type;
    public ITypeSymbol? KeyType => IsDictionary ? AddMethod.Parameters[0].Type : null;
}

public enum XamlMarkupExtensionIdentityKind
{
    BaseType,
    Interface,
    Suffix
}

/// <summary>
/// Canonical Roslyn evidence for a framework-authorized CLR markup-extension shape.
/// Invalid shapes remain inspectable so the binder can report the precise source error.
/// </summary>
public sealed class XamlMarkupExtensionShapeInfo
{
    public XamlMarkupExtensionShapeInfo(
        XamlMarkupExtensionIdentityKind identityKind,
        string identity,
        INamedTypeSymbol? identitySymbol,
        IMethodSymbol? provideValueMethod,
        IReadOnlyList<IMethodSymbol> candidates,
        IReadOnlyList<ITypeSymbol> requiredServices,
        bool acceptsEmptyServiceProvider,
        string providerId,
        string? error)
    {
        IdentityKind = identityKind;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        IdentitySymbol = identitySymbol;
        ProvideValueMethod = provideValueMethod;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        RequiredServices = requiredServices ?? throw new ArgumentNullException(nameof(requiredServices));
        AcceptsEmptyServiceProvider = acceptsEmptyServiceProvider;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlMarkupExtensionIdentityKind IdentityKind { get; }
    public string Identity { get; }
    public INamedTypeSymbol? IdentitySymbol { get; }
    public IMethodSymbol? ProvideValueMethod { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public IReadOnlyList<ITypeSymbol> RequiredServices { get; }
    public bool AcceptsEmptyServiceProvider { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid => ProvideValueMethod != null && Error == null;
    public ITypeSymbol? ServiceProviderType =>
        ProvideValueMethod?.Parameters.Length == 1 ? ProvideValueMethod.Parameters[0].Type : null;
}

/// <summary>
/// Canonical Roslyn evidence for a class-level object-writer set-value callback.
/// The descriptor keeps malformed declarations inspectable and prevents emitters from
/// repeating attribute-name or method-name discovery.
/// </summary>
public sealed class XamlSetValueHandlerShapeInfo
{
    public XamlSetValueHandlerShapeInfo(
        string semantic,
        string handlerName,
        XamlSchemaAnnotationInfo annotation,
        ITypeSymbol? eventArgsType,
        IMethodSymbol? handler,
        bool isDirectlyAccessible,
        IReadOnlyList<IMethodSymbol> candidates,
        string providerId,
        string? error)
    {
        Semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        HandlerName = handlerName ?? throw new ArgumentNullException(nameof(handlerName));
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        EventArgsType = eventArgsType;
        Handler = handler;
        IsDirectlyAccessible = isDirectlyAccessible;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public string Semantic { get; }
    public string HandlerName { get; }
    public XamlSchemaAnnotationInfo Annotation { get; }
    public ITypeSymbol? EventArgsType { get; }
    public IMethodSymbol? Handler { get; }
    public bool IsDirectlyAccessible { get; }
    public bool RequiresAccessBridge => IsValid && !IsDirectlyAccessible;
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid => Handler != null && EventArgsType != null && Error == null;
}

public enum XamlMarkupExtensionReceiverIdentityKind
{
    Interface,
    DuckMethod
}

/// <summary>
/// Canonical Roslyn evidence for a profile-authorized markup-extension receiver.
/// Framework profiles own service-provider creation and invocation lowering.
/// </summary>
public sealed class XamlMarkupExtensionReceiverShapeInfo
{
    public XamlMarkupExtensionReceiverShapeInfo(
        XamlMarkupExtensionReceiverIdentityKind identityKind,
        ITypeSymbol? identityType,
        IMethodSymbol? receiveMethod,
        ITypeSymbol? markupExtensionType,
        ITypeSymbol? serviceProviderType,
        IReadOnlyList<IMethodSymbol> candidates,
        string providerId,
        string? error)
    {
        IdentityKind = identityKind;
        IdentityType = identityType;
        ReceiveMethod = receiveMethod;
        MarkupExtensionType = markupExtensionType;
        ServiceProviderType = serviceProviderType;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Error = error;
    }

    public XamlMarkupExtensionReceiverIdentityKind IdentityKind { get; }
    public ITypeSymbol? IdentityType { get; }
    public IMethodSymbol? ReceiveMethod { get; }
    public ITypeSymbol? MarkupExtensionType { get; }
    public ITypeSymbol? ServiceProviderType { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public string ProviderId { get; }
    public string? Error { get; }
    public bool IsValid =>
        IdentityType != null &&
        ReceiveMethod != null &&
        MarkupExtensionType != null &&
        ServiceProviderType != null &&
        Error == null;
}

/// <summary>
/// Canonical save-path evidence for a value serializer selected by attribute metadata.
/// This descriptor is intentionally separate from load-path text syntax and type conversion.
/// </summary>
public sealed class XamlValueSerializerShapeInfo
{
    public XamlValueSerializerShapeInfo(
        XamlSchemaAnnotationInfo annotation,
        INamedTypeSymbol? serializerType,
        IMethodSymbol? constructor,
        IMethodSymbol? canConvertToStringMethod,
        IMethodSymbol? convertToStringMethod,
        IReadOnlyList<IMethodSymbol> candidates,
        string providerId,
        bool isSuppressed,
        string? error)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        SerializerType = serializerType;
        Constructor = constructor;
        CanConvertToStringMethod = canConvertToStringMethod;
        ConvertToStringMethod = convertToStringMethod;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        IsSuppressed = isSuppressed;
        Error = error;
    }

    public XamlSchemaAnnotationInfo Annotation { get; }
    public INamedTypeSymbol? SerializerType { get; }
    public IMethodSymbol? Constructor { get; }
    public IMethodSymbol? CanConvertToStringMethod { get; }
    public IMethodSymbol? ConvertToStringMethod { get; }
    public IReadOnlyList<IMethodSymbol> Candidates { get; }
    public string ProviderId { get; }
    public bool IsSuppressed { get; }
    public string? Error { get; }
    public bool IsValid => Error == null &&
        (IsSuppressed ||
         (SerializerType != null &&
          Constructor != null &&
          CanConvertToStringMethod != null &&
          ConvertToStringMethod != null));
    public ITypeSymbol? ContextType =>
        ConvertToStringMethod?.Parameters.Length == 2
            ? ConvertToStringMethod.Parameters[1].Type
            : null;
}

/// <summary>Validated public static accessor shape used to expose an attachable XAML member.</summary>
public sealed class XamlAttachedMemberShapeInfo
{
    public XamlAttachedMemberShapeInfo(
        IMethodSymbol getter,
        IMethodSymbol? setter,
        string providerId)
    {
        Getter = getter ?? throw new ArgumentNullException(nameof(getter));
        Setter = setter;
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }

    public IMethodSymbol Getter { get; }
    public IMethodSymbol? Setter { get; }
    public string ProviderId { get; }
}

/// <summary>
/// Validated framework property-system shape. The compiler records the exact Roslyn symbols
/// selected by a profile policy so emitters never rediscover members by convention.
/// </summary>
public sealed class XamlPropertySystemShapeInfo
{
    public XamlPropertySystemShapeInfo(
        ISymbol identifier,
        IMethodSymbol setter,
        string providerId)
    {
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        Setter = setter ?? throw new ArgumentNullException(nameof(setter));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
    }

    public ISymbol Identifier { get; }
    public IMethodSymbol Setter { get; }
    public string ProviderId { get; }
}
