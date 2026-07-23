using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using ProGPU.Xaml.Diagnostics;
using ProGPU.Xaml.Parsing;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Schema;

public enum XamlMemberKind
{
    Property,
    Event,
    AttachableProperty,
    Collection,
    Dictionary,
    DeferredContent
}

/// <summary>
/// Identifies framework vocabulary that is syntax/semantics rather than a CLR object value.
/// The neutral binder and lowerer preserve the role while framework profiles own its policy
/// and runtime publication.
/// </summary>
public enum XamlExpressionRole
{
    None,
    CompiledBinding
}

public sealed class XamlEnumValueInfo
{
    public XamlEnumValueInfo(string name, IFieldSymbol symbol)
    {
        Name = name;
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
    }

    public string Name { get; }
    public IFieldSymbol Symbol { get; }
}

public sealed class XamlConstructorInfo
{
    public XamlConstructorInfo(IMethodSymbol symbol)
        : this(symbol, null)
    {
    }
    public XamlConstructorInfo(
        IMethodSymbol symbol,
        IReadOnlyList<XamlConstructorParameterInfo>? parameters)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        ArgumentTypes = symbol.Parameters.Select(static parameter => parameter.Type).ToArray();
        Parameters = parameters ?? symbol.Parameters
            .Select(static parameter => new XamlConstructorParameterInfo(parameter))
            .ToArray();
    }
    public XamlConstructorInfo(IReadOnlyList<ITypeSymbol> argumentTypes)
    {
        ArgumentTypes = argumentTypes ?? throw new ArgumentNullException(nameof(argumentTypes));
        Parameters = Array.Empty<XamlConstructorParameterInfo>();
    }
    public IMethodSymbol? Symbol { get; }
    public IReadOnlyList<ITypeSymbol> ArgumentTypes { get; }
    public IReadOnlyList<XamlConstructorParameterInfo> Parameters { get; }
    public int Arity => ArgumentTypes.Count;
}

public sealed class XamlConstructorParameterInfo
{
    public XamlConstructorParameterInfo(
        IParameterSymbol symbol,
        XamlDataTypeInheritanceInfo? dataTypeInheritance = null)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        DataTypeInheritance = dataTypeInheritance;
    }

    public IParameterSymbol Symbol { get; }
    public XamlDataTypeInheritanceInfo? DataTypeInheritance { get; }
}

public sealed class XamlTypeInfo
{
    public XamlTypeInfo(
        string namespaceUri,
        string name,
        ITypeSymbol symbol,
        string metadataName,
        bool isValueType,
        bool isEnum,
        bool isNullable,
        bool isCollection,
        bool isDictionary,
        string? contentMemberName,
        string? runtimeNameMemberName = null,
        string? dictionaryKeyMemberName = null,
        XamlCollectionShapeInfo? collectionShape = null,
        IReadOnlyList<XamlSchemaAnnotationInfo>? annotations = null,
        IReadOnlyList<XamlEnumValueInfo>? enumValues = null,
        bool isDefaultConstructible = true,
        IReadOnlyList<XamlConstructorInfo>? constructors = null,
        bool isGeneric = false,
        int genericArity = 0,
        string? nameScopeMemberName = null,
        string? xmlLanguageMemberName = null,
        string? uidMemberName = null,
        XamlTextSyntaxInfo? textSyntax = null,
        bool isMarkupExtension = false,
        ITypeSymbol? returnValueType = null,
        XamlResourceReferenceRole resourceReferenceRole = XamlResourceReferenceRole.None,
        XamlMarkupExtensionShapeInfo? markupExtensionShape = null,
        XamlSetValueHandlerShapeInfo? markupExtensionSetHandler = null,
        XamlSetValueHandlerShapeInfo? typeConverterSetHandler = null,
        XamlValueSerializerShapeInfo? valueSerializer = null,
        XamlSchemaBooleanInfo? trimSurroundingWhitespace = null,
        XamlSchemaBooleanInfo? whitespaceSignificantCollection = null,
        XamlSchemaBooleanInfo? usableDuringInitialization = null,
        IReadOnlyList<XamlContentWrapperShapeInfo>? contentWrappers = null,
        XamlSchemaBooleanInfo? ambient = null,
        XamlDeferringLoaderShapeInfo? deferringLoader = null,
        XamlAliasedMemberShapeInfo? nameScopeProperty = null,
        XamlAliasedMemberShapeInfo? xmlLanguageProperty = null,
        XamlAliasedMemberShapeInfo? uidProperty = null,
        XamlNameScopeShapeInfo? nameScopeShape = null,
        XamlMarkupExtensionOptionSelectorShapeInfo? markupExtensionOptionSelector = null,
        XamlListSplitInfo? listSplit = null,
        IReadOnlyList<XamlTemplatePartInfo>? templateParts = null,
        IReadOnlyList<XamlTemplateVisualStateInfo>? templateVisualStates = null,
        IReadOnlyList<XamlStyleTypedPropertyInfo>? styleTypedProperties = null,
        XamlCompilationModeInfo? compilationMode = null,
        XamlFilePathInfo? filePath = null,
        XamlTypeMarkerInfo? bindable = null,
        XamlTypeMarkerInfo? fullMetadataProvider = null,
        XamlBrowsableInfo? browsable = null,
        XamlEditorBrowsableInfo? editorBrowsable = null,
        XamlBrowsableInfo? designTimeVisible = null,
        XamlLocalizabilityInfo? localizability = null,
        XamlMarkupExtensionReceiverShapeInfo? markupExtensionReceiver = null,
        XamlExpressionRole expressionRole = XamlExpressionRole.None)
    {
        NamespaceUri = namespaceUri;
        Name = name;
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        MetadataName = metadataName ?? throw new ArgumentNullException(nameof(metadataName));
        IsValueType = isValueType;
        IsEnum = isEnum;
        IsNullable = isNullable;
        IsCollection = isCollection;
        IsDictionary = isDictionary;
        ContentMemberName = contentMemberName;
        RuntimeNameMemberName = runtimeNameMemberName;
        DictionaryKeyMemberName = dictionaryKeyMemberName;
        CollectionShape = collectionShape;
        Annotations = annotations ?? Array.Empty<XamlSchemaAnnotationInfo>();
        EnumValues = enumValues ?? Array.Empty<XamlEnumValueInfo>();
        IsDefaultConstructible = isDefaultConstructible;
        Constructors = constructors ?? Array.Empty<XamlConstructorInfo>();
        IsGeneric = isGeneric;
        GenericArity = genericArity;
        NameScopeMemberName = nameScopeMemberName;
        XmlLanguageMemberName = xmlLanguageMemberName;
        UidMemberName = uidMemberName;
        TextSyntax = textSyntax ?? XamlTextSyntaxInfo.None;
        IsMarkupExtension = isMarkupExtension;
        ReturnValueType = returnValueType;
        ResourceReferenceRole = resourceReferenceRole;
        MarkupExtensionShape = markupExtensionShape;
        MarkupExtensionSetHandler = markupExtensionSetHandler;
        TypeConverterSetHandler = typeConverterSetHandler;
        ValueSerializer = valueSerializer;
        TrimSurroundingWhitespace = trimSurroundingWhitespace;
        WhitespaceSignificantCollection = whitespaceSignificantCollection;
        UsableDuringInitialization = usableDuringInitialization;
        ContentWrappers = contentWrappers ?? Array.Empty<XamlContentWrapperShapeInfo>();
        Ambient = ambient;
        DeferringLoader = deferringLoader;
        NameScopeProperty = nameScopeProperty;
        XmlLanguageProperty = xmlLanguageProperty;
        UidProperty = uidProperty;
        NameScopeShape = nameScopeShape;
        MarkupExtensionOptionSelector = markupExtensionOptionSelector;
        ListSplit = listSplit;
        TemplateParts = templateParts ?? Array.Empty<XamlTemplatePartInfo>();
        TemplateVisualStates = templateVisualStates ??
            Array.Empty<XamlTemplateVisualStateInfo>();
        StyleTypedProperties = styleTypedProperties ??
            Array.Empty<XamlStyleTypedPropertyInfo>();
        CompilationMode = compilationMode;
        FilePath = filePath;
        Bindable = bindable;
        FullMetadataProvider = fullMetadataProvider;
        Browsable = browsable;
        EditorBrowsable = editorBrowsable;
        DesignTimeVisible = designTimeVisible;
        Localizability = localizability;
        MarkupExtensionReceiver = markupExtensionReceiver;
        ExpressionRole = expressionRole;
        EffectiveTemplateParts = IndexNearestString(
            TemplateParts,
            static item => item.Name);
        EffectiveTemplateVisualStates =
            IndexNearestVisualStates(TemplateVisualStates);
        EffectiveStyleTypedProperties = IndexNearestString(
            StyleTypedProperties,
            static item => item.PropertyName);
    }

    public string NamespaceUri { get; }
    public string Name { get; }
    public ITypeSymbol Symbol { get; }
    public string MetadataName { get; }
    public string CSharpName => Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    public bool IsValueType { get; }
    public bool IsEnum { get; }
    public bool IsNullable { get; }
    public bool IsCollection { get; }
    public bool IsDictionary { get; }
    public string? ContentMemberName { get; }
    public string? RuntimeNameMemberName { get; }
    public string? DictionaryKeyMemberName { get; }
    public XamlCollectionShapeInfo? CollectionShape { get; }
    public IReadOnlyList<XamlSchemaAnnotationInfo> Annotations { get; }
    public IReadOnlyList<XamlEnumValueInfo> EnumValues { get; }
    public bool IsDefaultConstructible { get; }
    public IReadOnlyList<XamlConstructorInfo> Constructors { get; }
    public bool IsGeneric { get; }
    public int GenericArity { get; }
    public string? NameScopeMemberName { get; }
    public string? XmlLanguageMemberName { get; }
    public string? UidMemberName { get; }
    public XamlAliasedMemberShapeInfo? NameScopeProperty { get; }
    public XamlAliasedMemberShapeInfo? XmlLanguageProperty { get; }
    public XamlAliasedMemberShapeInfo? UidProperty { get; }
    public XamlNameScopeShapeInfo? NameScopeShape { get; }
    public bool IsNameScope => NameScopeShape?.IsValid == true;
    public XamlMarkupExtensionOptionSelectorShapeInfo? MarkupExtensionOptionSelector { get; }
    public XamlListSplitInfo? ListSplit { get; }
    public IReadOnlyList<XamlTemplatePartInfo> TemplateParts { get; }
    public IReadOnlyList<XamlTemplateVisualStateInfo> TemplateVisualStates { get; }
    public IReadOnlyList<XamlStyleTypedPropertyInfo> StyleTypedProperties { get; }
    public IReadOnlyDictionary<string, XamlTemplatePartInfo> EffectiveTemplateParts { get; }
    public IReadOnlyDictionary<XamlTemplateVisualStateKey, XamlTemplateVisualStateInfo>
        EffectiveTemplateVisualStates { get; }
    public IReadOnlyDictionary<string, XamlStyleTypedPropertyInfo>
        EffectiveStyleTypedProperties { get; }
    public XamlCompilationModeInfo? CompilationMode { get; }
    public XamlFilePathInfo? FilePath { get; }
    public XamlTypeMarkerInfo? Bindable { get; }
    public XamlTypeMarkerInfo? FullMetadataProvider { get; }
    public bool IsBindable => Bindable?.IsValid == true;
    public bool IsFullMetadataProvider => FullMetadataProvider?.IsValid == true;
    public XamlBrowsableInfo? Browsable { get; }
    public XamlEditorBrowsableInfo? EditorBrowsable { get; }
    public XamlBrowsableInfo? DesignTimeVisible { get; }
    public XamlLocalizabilityInfo? Localizability { get; }
    public bool IsBrowsableForDesigner =>
        Browsable?.Value != false &&
        EditorBrowsable?.State != XamlEditorBrowsableState.Never;
    public XamlTextSyntaxInfo TextSyntax { get; }
    public bool IsMarkupExtension { get; }
    public ITypeSymbol? ReturnValueType { get; }
    public XamlResourceReferenceRole ResourceReferenceRole { get; }
    public XamlMarkupExtensionShapeInfo? MarkupExtensionShape { get; }
    public XamlSetValueHandlerShapeInfo? MarkupExtensionSetHandler { get; }
    public XamlSetValueHandlerShapeInfo? TypeConverterSetHandler { get; }
    public XamlMarkupExtensionReceiverShapeInfo? MarkupExtensionReceiver { get; }
    public XamlExpressionRole ExpressionRole { get; }
    public XamlValueSerializerShapeInfo? ValueSerializer { get; }
    public XamlSchemaBooleanInfo? TrimSurroundingWhitespace { get; }
    public XamlSchemaBooleanInfo? WhitespaceSignificantCollection { get; }
    public XamlSchemaBooleanInfo? UsableDuringInitialization { get; }
    public bool ShouldTrimSurroundingWhitespace => TrimSurroundingWhitespace?.Value == true;
    public bool IsWhitespaceSignificantCollection => WhitespaceSignificantCollection?.Value == true;
    public bool IsUsableDuringInitialization => UsableDuringInitialization?.Value == true;
    public IReadOnlyList<XamlContentWrapperShapeInfo> ContentWrappers { get; }
    public XamlSchemaBooleanInfo? Ambient { get; }
    public bool IsAmbient => Ambient?.Value == true;
    public XamlDeferringLoaderShapeInfo? DeferringLoader { get; }
    public bool IsDeferred => DeferringLoader != null;

    private static IReadOnlyDictionary<string, TValue> IndexNearestString<TValue>(
        IReadOnlyList<TValue> values,
        Func<TValue, string?> getKey)
    {
        var result = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = getKey(value);
            if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key!))
                result.Add(key!, value);
        }
        return result;
    }

    private static IReadOnlyDictionary<XamlTemplateVisualStateKey, XamlTemplateVisualStateInfo>
        IndexNearestVisualStates(IReadOnlyList<XamlTemplateVisualStateInfo> values)
    {
        var result =
            new Dictionary<XamlTemplateVisualStateKey, XamlTemplateVisualStateInfo>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.GroupName) ||
                string.IsNullOrWhiteSpace(value.Name))
                continue;
            var key = new XamlTemplateVisualStateKey(
                value.GroupName!,
                value.Name!);
            if (!result.ContainsKey(key)) result.Add(key, value);
        }
        return result;
    }
}

public sealed class XamlMemberInfo
{
    public XamlMemberInfo(
        string name,
        string csharpName,
        ISymbol? symbol,
        XamlTypeInfo declaringType,
        XamlTypeInfo valueType,
        XamlMemberKind kind,
        bool canWrite,
        string? attachableSetterName = null,
        IReadOnlyList<XamlSchemaAnnotationInfo>? annotations = null,
        XamlAttachedMemberShapeInfo? attachableShape = null,
        XamlTextSyntaxInfo? textSyntax = null,
        string? identity = null,
        XamlPropertySystemShapeInfo? propertySystemShape = null,
        XamlResourceMemberRole resourceRole = XamlResourceMemberRole.None,
        string? syntheticSemantic = null,
        XamlValueSerializerShapeInfo? valueSerializer = null,
        XamlConstructorArgumentShapeInfo? constructorArgument = null,
        XamlSchemaBooleanInfo? ambient = null,
        XamlDeferringLoaderShapeInfo? deferringLoader = null,
        IReadOnlyList<XamlMarkupBracketPairInfo>? markupExtensionBracketCharacters = null,
        IReadOnlyList<XamlMemberDependencyInfo>? dependencies = null,
        XamlMarkupExtensionOptionInfo? markupExtensionOption = null,
        XamlDataTypeSourceInfo? dataTypeSource = null,
        XamlDataTypeInheritanceInfo? dataTypeInheritance = null,
        XamlItemsDataTypeInheritanceInfo? itemsDataTypeInheritance = null,
        XamlBindingAssignmentInfo? bindingAssignment = null,
        IReadOnlyList<XamlAttachedPropertyBrowseRuleInfo>? attachedPropertyBrowseRules = null,
        XamlDefaultValueInfo? defaultValue = null,
        XamlDesignerSerializationVisibilityInfo? designerSerializationVisibility = null,
        XamlDesignerSerializationOptionsInfo? designerSerializationOptions = null,
        XamlBrowsableInfo? browsable = null,
        XamlEditorBrowsableInfo? editorBrowsable = null,
        XamlLocalizabilityInfo? localizability = null,
        XamlDesignerSerializationMethodsInfo? serializationMethods = null)
    {
        Name = name;
        CSharpName = csharpName;
        Symbol = symbol;
        DeclaringType = declaringType;
        ValueType = valueType;
        Kind = kind;
        CanWrite = canWrite;
        AttachableSetterName = attachableSetterName;
        Annotations = annotations ?? Array.Empty<XamlSchemaAnnotationInfo>();
        AttachableShape = attachableShape;
        TextSyntax = textSyntax ?? valueType.TextSyntax;
        Identity = identity ?? symbol?.GetDocumentationCommentId() ??
            "synthetic-member:" + declaringType.MetadataName + ":" + name;
        PropertySystemShape = propertySystemShape;
        ResourceRole = resourceRole;
        SyntheticSemantic = syntheticSemantic;
        ValueSerializer = valueSerializer;
        ConstructorArgument = constructorArgument;
        Ambient = ambient;
        DeclaredDeferringLoader = deferringLoader;
        MarkupExtensionBracketCharacters = markupExtensionBracketCharacters ??
            Array.Empty<XamlMarkupBracketPairInfo>();
        Dependencies = dependencies ?? Array.Empty<XamlMemberDependencyInfo>();
        MarkupExtensionOption = markupExtensionOption;
        DataTypeSource = dataTypeSource;
        DataTypeInheritance = dataTypeInheritance;
        ItemsDataTypeInheritance = itemsDataTypeInheritance;
        BindingAssignment = bindingAssignment;
        AttachedPropertyBrowseRules = attachedPropertyBrowseRules ??
            Array.Empty<XamlAttachedPropertyBrowseRuleInfo>();
        DefaultValue = defaultValue;
        DesignerSerializationVisibility = designerSerializationVisibility;
        DesignerSerializationOptions = designerSerializationOptions;
        Browsable = browsable;
        EditorBrowsable = editorBrowsable;
        Localizability = localizability;
        SerializationMethods = serializationMethods;
        SerializationPolicy = XamlMemberSerializationPolicy.Create(this);
    }

    public string Name { get; }
    public string CSharpName { get; }
    public ISymbol? Symbol { get; }
    public XamlTypeInfo DeclaringType { get; }
    public XamlTypeInfo ValueType { get; }
    public XamlMemberKind Kind { get; }
    public bool CanWrite { get; }
    public string? AttachableSetterName { get; }
    public IReadOnlyList<XamlSchemaAnnotationInfo> Annotations { get; }
    public XamlAttachedMemberShapeInfo? AttachableShape { get; }
    public XamlTextSyntaxInfo TextSyntax { get; }
    public string Identity { get; }
    public XamlPropertySystemShapeInfo? PropertySystemShape { get; }
    public XamlResourceMemberRole ResourceRole { get; }
    public string? SyntheticSemantic { get; }
    public XamlValueSerializerShapeInfo? ValueSerializer { get; }
    public XamlConstructorArgumentShapeInfo? ConstructorArgument { get; }
    public XamlSchemaBooleanInfo? Ambient { get; }
    public bool IsDeclaredAmbient => Ambient?.Value == true;
    public bool IsAmbient => IsDeclaredAmbient || ValueType.IsAmbient;
    public XamlDeferringLoaderShapeInfo? DeclaredDeferringLoader { get; }
    public XamlDeferringLoaderShapeInfo? DeferringLoader =>
        DeclaredDeferringLoader ?? ValueType.DeferringLoader;
    public bool IsDeferred => DeferringLoader != null;
    public IReadOnlyList<XamlMarkupBracketPairInfo> MarkupExtensionBracketCharacters { get; }
    public IReadOnlyList<XamlMemberDependencyInfo> Dependencies { get; }
    public XamlMarkupExtensionOptionInfo? MarkupExtensionOption { get; }
    public XamlDataTypeSourceInfo? DataTypeSource { get; }
    public XamlDataTypeInheritanceInfo? DataTypeInheritance { get; }
    public XamlItemsDataTypeInheritanceInfo? ItemsDataTypeInheritance { get; }
    public XamlBindingAssignmentInfo? BindingAssignment { get; }
    public bool AssignsBindingObject => BindingAssignment?.IsValid == true;
    public IReadOnlyList<XamlAttachedPropertyBrowseRuleInfo> AttachedPropertyBrowseRules { get; }
    public XamlDefaultValueInfo? DefaultValue { get; }
    public XamlDesignerSerializationVisibilityInfo? DesignerSerializationVisibility { get; }
    public XamlDesignerSerializationOptionsInfo? DesignerSerializationOptions { get; }
    public XamlBrowsableInfo? Browsable { get; }
    public XamlEditorBrowsableInfo? EditorBrowsable { get; }
    public XamlLocalizabilityInfo? Localizability { get; }
    public XamlDesignerSerializationMethodsInfo? SerializationMethods { get; }
    public XamlMemberSerializationPolicy SerializationPolicy { get; }
    public bool ShouldSerialize => SerializationPolicy.Include;
    public bool SerializesContent => SerializationPolicy.IsContent;
    public bool ShouldSerializeAsAttribute => SerializationPolicy.PreferAttribute;
    public bool IsBrowsableForDesigner =>
        Browsable?.Value != false &&
        EditorBrowsable?.State != XamlEditorBrowsableState.Never;
}

public interface IXamlTypeSystem
{
    XamlTypeInfo? ResolveType(string namespaceUri, string localName);

    XamlTypeInfo? ResolveType(
        string namespaceUri,
        string localName,
        IReadOnlyList<XamlTypeInfo> typeArguments);

    XamlMemberInfo? ResolveMember(
        XamlTypeInfo objectType,
        string memberNamespaceUri,
        string? ownerTypeName,
        string memberName);

    bool IsAssignable(XamlTypeInfo sourceType, XamlTypeInfo targetType);

    bool IsAssignable(ITypeSymbol sourceType, ITypeSymbol targetType);
}

/// <summary>
/// Optional canonical metadata-name lookup used by features whose source type is declared by
/// language metadata (for example, an <c>x:Class</c> compiled-binding root).
/// </summary>
public interface IXamlMetadataTypeResolver
{
    XamlTypeInfo? ResolveMetadataType(string metadataName);
}

/// <summary>Optional canonical projection from an existing Roslyn type symbol.</summary>
public interface IXamlSymbolTypeResolver
{
    XamlTypeInfo? ResolveSymbolType(ITypeSymbol symbol);
}

/// <summary>Optional language conversion query used by strongly typed expression syntax.</summary>
public interface IXamlSymbolConversionService
{
    bool HasExplicitConversion(ITypeSymbol sourceType, ITypeSymbol targetType);
}

/// <summary>Optional Roslyn accessibility query for generated members in a known partial type.</summary>
public interface IXamlSymbolAccessibilityService
{
    bool IsAccessibleWithin(ISymbol symbol, ITypeSymbol withinType);
}

/// <summary>
/// Optional framework dialect policy for text forms accepted in addition to the
/// framework-neutral intrinsic XAML lexical grammar.
/// </summary>
public interface IXamlTextValuePolicy
{
    /// <summary>
    /// Returns <see langword="true"/> when the policy handles the target type. The
    /// <paramref name="isValid"/> result then determines whether the text is accepted.
    /// </summary>
    bool TryValidateTextValue(XamlTypeInfo targetType, string text, out bool isValid);
}

/// <summary>
/// Optional framework dialect policy for language directives that act as dictionary-key
/// aliases. The canonical <c>x:Key</c> directive is always checked first by the core.
/// </summary>
public interface IXamlDictionaryKeyDirectivePolicy
{
    IReadOnlyList<string> DictionaryKeyDirectiveAliases { get; }
}

public interface IXamlFrameworkProfile
{
    string Id { get; }
    int ContractVersion { get; }
    XamlFrameworkCapabilities Capabilities { get; }
    IReadOnlyList<string> FileExtensions { get; }
    IReadOnlyList<string> GetClrNamespaceCandidates(string xamlNamespaceUri);

}

[Flags]
public enum XamlFrameworkCapabilities
{
    None = 0,
    SchemaMetadata = 1 << 0,
    NamespaceMetadata = 1 << 1,
    StructuredCSharpEmission = 1 << 2,
    MarkupExtensions = 1 << 3,
    Resources = 1 << 4,
    Bindings = 1 << 5,
    Templates = 1 << 6,
    HotReload = 1 << 7,
    ConditionalNamespaces = 1 << 8
}

public static class XamlFrameworkContract
{
    public const int CurrentVersion = 2;
}

public enum XamlStaticResourceForwardReferenceMode
{
    /// <summary>Enforce the standard lexical no-forward-reference rule.</summary>
    Error,
    /// <summary>
    /// Reorder entries in a compiled resource provider by their static dependencies while
    /// preserving declaration order among otherwise independent entries.
    /// </summary>
    Reorder
}

public sealed class XamlCompilerOptions
{
    public string Framework { get; set; } = "WinUI";
    /// <summary>
    /// Stable project-relative identity used for compiled resource registration and hint names.
    /// The physical document path remains the diagnostic/source-map path.
    /// </summary>
    public string? ResourceUri { get; set; }
    public ProGPU.Xaml.Resources.XamlResourceDependencySlice? ResourceDependencies { get; set; }
    public bool Strict { get; set; } = true;
    public bool EmitHotReloadHooks { get; set; } = true;
    public bool EmitSourceComments { get; set; } = true;
    public XamlStaticResourceForwardReferenceMode StaticResourceForwardReferenceMode { get; set; } =
        XamlStaticResourceForwardReferenceMode.Error;
}

public sealed class XamlGeneratedSource
{
    public XamlGeneratedSource(
        string hintName,
        string source,
        SyntaxTree? generatedSyntaxTree = null,
        SyntaxTree? unformattedSyntaxTree = null,
        XamlCompiledResourceArtifact? compiledResource = null)
    {
        HintName = hintName;
        Source = source;
        GeneratedSyntaxTree = generatedSyntaxTree;
        UnformattedSyntaxTree = unformattedSyntaxTree;
        CompiledResource = compiledResource;
    }

    public string HintName { get; }
    public string Source { get; }
    public SyntaxTree? GeneratedSyntaxTree { get; }
    public SyntaxTree? UnformattedSyntaxTree { get; }
    public XamlCompiledResourceArtifact? CompiledResource { get; }
}

/// <summary>Language-neutral identity of a generated classless XAML factory.</summary>
public sealed class XamlCompiledResourceArtifact
{
    public XamlCompiledResourceArtifact(
        string resourceUri,
        string factoryNamespace,
        string factoryTypeName,
        string buildMethodName,
        string populateMethodName)
    {
        ResourceUri = resourceUri ?? throw new ArgumentNullException(nameof(resourceUri));
        FactoryNamespace = factoryNamespace ?? throw new ArgumentNullException(nameof(factoryNamespace));
        FactoryTypeName = factoryTypeName ?? throw new ArgumentNullException(nameof(factoryTypeName));
        BuildMethodName = buildMethodName ?? throw new ArgumentNullException(nameof(buildMethodName));
        PopulateMethodName = populateMethodName ?? throw new ArgumentNullException(nameof(populateMethodName));
    }
    public string ResourceUri { get; }
    public string FactoryNamespace { get; }
    public string FactoryTypeName { get; }
    public string BuildMethodName { get; }
    public string PopulateMethodName { get; }
}

public sealed class XamlCompilationResult
{
    public XamlCompilationResult(
        XamlDocumentSyntax syntax,
        IReadOnlyList<XamlGeneratedSource> sources,
        IReadOnlyList<Diagnostic> diagnostics,
        XamlDocumentBuildMetadata? buildMetadata = null,
        bool wasSkipped = false)
    {
        Syntax = syntax;
        Sources = sources;
        Diagnostics = diagnostics;
        BuildMetadata = buildMetadata;
        WasSkipped = wasSkipped;
    }

    public XamlDocumentSyntax Syntax { get; }
    public IReadOnlyList<XamlGeneratedSource> Sources { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public XamlDocumentBuildMetadata? BuildMetadata { get; }
    public bool WasSkipped { get; }
}

public interface IXamlCodeEmitter
{
    XamlCompilationResult Emit(
        XamlDocumentSyntax document,
        IXamlTypeSystem typeSystem,
        IXamlFrameworkProfile framework,
        XamlCompilerOptions options);
}
