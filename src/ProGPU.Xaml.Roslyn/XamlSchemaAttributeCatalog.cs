using System.Collections.Generic;
using ProGPU.Xaml.Schema;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Public-contract metadata mappings for framework profiles. The catalog contains names and
/// value locations only; semantic behavior remains in the neutral binder and IR passes.
/// </summary>
public static class XamlSchemaAttributeCatalog
{
    public static IReadOnlyList<XamlSchemaAttributeRule> Common { get; } = new[]
    {
        Rule("System.ComponentModel.TypeConverterAttribute", XamlSchemaSemantics.TypeConverter, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("System.ObsoleteAttribute", XamlSchemaSemantics.Obsolete, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, ctor: 0),
        Rule("System.Diagnostics.CodeAnalysis.ExperimentalAttribute", XamlSchemaSemantics.Experimental, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, ctor: 0),
        Rule("System.ComponentModel.DefaultValueAttribute", XamlSchemaSemantics.DefaultValue, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.ComponentModel.DesignerSerializationVisibilityAttribute", XamlSchemaSemantics.DesignerSerializationVisibility, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.ComponentModel.BrowsableAttribute", XamlSchemaSemantics.Browsable, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.ComponentModel.EditorBrowsableAttribute", XamlSchemaSemantics.EditorBrowsable, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.ComponentModel.DesignTimeVisibleAttribute", XamlSchemaSemantics.DesignTimeVisible, XamlSchemaAttributeTargets.Type, inherited: true),
        // This System.Xaml parser contract may annotate custom markup extensions consumed
        // by more than one UI framework. It is inert unless the defining assembly is present.
        Rule("System.Windows.Markup.MarkupExtensionBracketCharactersAttribute", XamlSchemaSemantics.MarkupExtensionBracketCharacters, XamlSchemaAttributeTargets.Member, ctor: 0, allowMultiple: true),
    };

    /// <summary>
    /// Public build-system contracts understood by the compiler host independently of the
    /// selected object-model profile. A future framework package may expose the same neutral
    /// semantics under additional attribute metadata names.
    /// </summary>
    public static IReadOnlyList<XamlSchemaAttributeRule> BuildControl { get; } = new[]
    {
        Rule("System.Windows.Markup.RootNamespaceAttribute", XamlSchemaSemantics.RootNamespace, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlCompilationAttribute", XamlSchemaSemantics.XamlCompilation, XamlSchemaAttributeTargets.Assembly | XamlSchemaAttributeTargets.Module | XamlSchemaAttributeTargets.Type, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlFilePathAttribute", XamlSchemaSemantics.XamlFilePath, XamlSchemaAttributeTargets.Type, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlResourceIdAttribute", XamlSchemaSemantics.XamlResourceId, XamlSchemaAttributeTargets.Assembly, ctor: 0, allowMultiple: true),
    };

    public static IReadOnlyList<XamlSchemaAttributeRule> WinUi { get; } = new[]
    {
        Named("Windows.Foundation.Metadata.CreateFromStringAttribute", XamlSchemaSemantics.CreateFromString, XamlSchemaAttributeTargets.Type, "MethodName"),
        Named("Microsoft.UI.Xaml.Markup.ContentPropertyAttribute", XamlSchemaSemantics.ContentProperty, XamlSchemaAttributeTargets.Type, "Name", inherited: true),
        Named("Microsoft.UI.Xaml.Markup.MarkupExtensionReturnTypeAttribute", XamlSchemaSemantics.MarkupExtensionReturnType, XamlSchemaAttributeTargets.Type, "ReturnType", inherited: true),
        Rule("Microsoft.UI.Xaml.Markup.FullXamlMetadataProviderAttribute", XamlSchemaSemantics.FullXamlMetadataProvider, XamlSchemaAttributeTargets.Type),
        Rule("Microsoft.UI.Xaml.Markup.UsableDuringInitializationAttribute", XamlSchemaSemantics.UsableDuringInitialization, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("Microsoft.UI.Xaml.Data.BindableAttribute", XamlSchemaSemantics.Bindable, XamlSchemaAttributeTargets.Type),
        Rule("Microsoft.UI.Xaml.StyleTypedPropertyAttribute", XamlSchemaSemantics.StyleTypedProperty, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
        Rule("Microsoft.UI.Xaml.TemplatePartAttribute", XamlSchemaSemantics.TemplatePart, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
        Rule("Microsoft.UI.Xaml.TemplateVisualStateAttribute", XamlSchemaSemantics.TemplateVisualState, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
    };

    public static IReadOnlyList<XamlSchemaAttributeRule> Wpf { get; } = new[]
    {
        Rule("System.Windows.Markup.AmbientAttribute", XamlSchemaSemantics.Ambient, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.Windows.Markup.ContentPropertyAttribute", XamlSchemaSemantics.ContentProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.RuntimeNamePropertyAttribute", XamlSchemaSemantics.RuntimeNameProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.DictionaryKeyPropertyAttribute", XamlSchemaSemantics.DictionaryKeyProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.ConstructorArgumentAttribute", XamlSchemaSemantics.ConstructorArgument, XamlSchemaAttributeTargets.Member, ctor: 0),
        Rule("System.Windows.Markup.ContentWrapperAttribute", XamlSchemaSemantics.ContentWrapper, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0, allowMultiple: true),
        Rule("System.Windows.Markup.DependsOnAttribute", XamlSchemaSemantics.DependsOn, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0, allowMultiple: true),
        Rule("System.Windows.Markup.NameScopePropertyAttribute", XamlSchemaSemantics.NameScopeProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.XmlLangPropertyAttribute", XamlSchemaSemantics.XmlLanguageProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.UidPropertyAttribute", XamlSchemaSemantics.UidProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.TrimSurroundingWhitespaceAttribute", XamlSchemaSemantics.TrimSurroundingWhitespace, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("System.Windows.Markup.WhitespaceSignificantCollectionAttribute", XamlSchemaSemantics.WhitespaceSignificantCollection, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("System.Windows.Markup.UsableDuringInitializationAttribute", XamlSchemaSemantics.UsableDuringInitialization, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.ValueSerializerAttribute", XamlSchemaSemantics.ValueSerializer, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.MarkupExtensionReturnTypeAttribute", XamlSchemaSemantics.MarkupExtensionReturnType, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.MarkupExtensionBracketCharactersAttribute", XamlSchemaSemantics.MarkupExtensionBracketCharacters, XamlSchemaAttributeTargets.Member, ctor: 0, allowMultiple: true),
        Rule("System.Windows.Markup.XamlSetMarkupExtensionAttribute", XamlSchemaSemantics.SetMarkupExtensionHandler, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.XamlSetTypeConverterAttribute", XamlSchemaSemantics.SetTypeConverterHandler, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.XamlDeferLoadAttribute", XamlSchemaSemantics.DeferredLoad, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("System.Windows.Markup.RootNamespaceAttribute", XamlSchemaSemantics.RootNamespace, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("System.Windows.Markup.XmlnsDefinitionAttribute", XamlSchemaSemantics.XmlnsDefinition, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("System.Windows.Markup.XmlnsPrefixAttribute", XamlSchemaSemantics.XmlnsPrefix, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("System.Windows.Markup.XmlnsCompatibleWithAttribute", XamlSchemaSemantics.XmlnsCompatibleWith, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("System.Windows.AttachedPropertyBrowsableForTypeAttribute", XamlSchemaSemantics.AttachedPropertyBrowseRule, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0, allowMultiple: true),
        Rule("System.Windows.AttachedPropertyBrowsableForChildrenAttribute", XamlSchemaSemantics.AttachedPropertyBrowseRule, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.Windows.AttachedPropertyBrowsableWhenAttributePresentAttribute", XamlSchemaSemantics.AttachedPropertyBrowseRule, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("System.Windows.StyleTypedPropertyAttribute", XamlSchemaSemantics.StyleTypedProperty, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
        Rule("System.Windows.TemplatePartAttribute", XamlSchemaSemantics.TemplatePart, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
        Rule("System.Windows.TemplateVisualStateAttribute", XamlSchemaSemantics.TemplateVisualState, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
        Rule("System.Windows.Markup.DesignerSerializationOptionsAttribute", XamlSchemaSemantics.DesignerSerializationOptions, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("System.Windows.LocalizabilityAttribute", XamlSchemaSemantics.Localizability, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true),
    };

    public static IReadOnlyList<XamlSchemaAttributeRule> Avalonia { get; } = new[]
    {
        Rule("Avalonia.Metadata.ContentAttribute", XamlSchemaSemantics.ContentProperty, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Metadata.AmbientAttribute", XamlSchemaSemantics.Ambient, XamlSchemaAttributeTargets.Type | XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Metadata.ConstructorArgumentAttribute", XamlSchemaSemantics.ConstructorArgument, XamlSchemaAttributeTargets.Member, ctor: 0),
        Rule("Avalonia.Metadata.DependsOnAttribute", XamlSchemaSemantics.DependsOn, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0, allowMultiple: true),
        Rule("Avalonia.Metadata.TrimSurroundingWhitespaceAttribute", XamlSchemaSemantics.TrimSurroundingWhitespace, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("Avalonia.Metadata.WhitespaceSignificantCollectionAttribute", XamlSchemaSemantics.WhitespaceSignificantCollection, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("Avalonia.Metadata.UsableDuringInitializationAttribute", XamlSchemaSemantics.UsableDuringInitialization, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("Avalonia.Metadata.TemplateContentAttribute", XamlSchemaSemantics.TemplateContent, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Metadata.ControlTemplateScopeAttribute", XamlSchemaSemantics.ControlTemplateScope, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("Avalonia.Metadata.DataTypeAttribute", XamlSchemaSemantics.DataType, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Metadata.InheritDataTypeFromAttribute", XamlSchemaSemantics.InheritDataType, XamlSchemaAttributeTargets.Member | XamlSchemaAttributeTargets.Parameter, inherited: true, ctor: 0),
        Rule("Avalonia.Metadata.InheritDataTypeFromItemsAttribute", XamlSchemaSemantics.InheritDataTypeFromItems, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("Avalonia.Metadata.MarkupExtensionDefaultOptionAttribute", XamlSchemaSemantics.MarkupExtensionDefaultOption, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Metadata.MarkupExtensionOptionAttribute", XamlSchemaSemantics.MarkupExtensionOption, XamlSchemaAttributeTargets.Member, inherited: true, ctor: 0),
        Rule("Avalonia.Metadata.AvaloniaListAttribute", XamlSchemaSemantics.ListSeparator, XamlSchemaAttributeTargets.Type, inherited: true),
        Rule("Avalonia.Metadata.XmlnsDefinitionAttribute", XamlSchemaSemantics.XmlnsDefinition, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("Avalonia.Metadata.XmlnsPrefixAttribute", XamlSchemaSemantics.XmlnsPrefix, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("Avalonia.Data.AssignBindingAttribute", XamlSchemaSemantics.AssignBinding, XamlSchemaAttributeTargets.Member, inherited: true),
        Rule("Avalonia.Controls.Metadata.TemplatePartAttribute", XamlSchemaSemantics.TemplatePart, XamlSchemaAttributeTargets.Type, inherited: true, allowMultiple: true),
    };

    public static IReadOnlyList<XamlSchemaAttributeRule> Maui { get; } = new[]
    {
        Rule("Microsoft.Maui.Controls.ContentPropertyAttribute", XamlSchemaSemantics.ContentProperty, XamlSchemaAttributeTargets.Type, inherited: true, ctor: 0),
        Rule("Microsoft.Maui.Controls.XmlnsDefinitionAttribute", XamlSchemaSemantics.XmlnsDefinition, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("Microsoft.Maui.Controls.XmlnsPrefixAttribute", XamlSchemaSemantics.XmlnsPrefix, XamlSchemaAttributeTargets.Assembly, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.AcceptEmptyServiceProviderAttribute", XamlSchemaSemantics.AcceptEmptyServiceProvider, XamlSchemaAttributeTargets.Type),
        Rule("Microsoft.Maui.Controls.Xaml.RequireServiceAttribute", XamlSchemaSemantics.RequireService, XamlSchemaAttributeTargets.Type, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlCompilationAttribute", XamlSchemaSemantics.XamlCompilation, XamlSchemaAttributeTargets.Assembly | XamlSchemaAttributeTargets.Module | XamlSchemaAttributeTargets.Type, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlFilePathAttribute", XamlSchemaSemantics.XamlFilePath, XamlSchemaAttributeTargets.Type, ctor: 0),
        Rule("Microsoft.Maui.Controls.Xaml.XamlResourceIdAttribute", XamlSchemaSemantics.XamlResourceId, XamlSchemaAttributeTargets.Assembly, ctor: 0, allowMultiple: true),
    };

    public static IReadOnlyList<XamlSchemaAttributeRule> Combine(
        IReadOnlyList<XamlSchemaAttributeRule> first,
        IReadOnlyList<XamlSchemaAttributeRule> second)
    {
        var result = new XamlSchemaAttributeRule[first.Count + second.Count];
        for (var index = 0; index < first.Count; index++) result[index] = first[index];
        for (var index = 0; index < second.Count; index++) result[first.Count + index] = second[index];
        return result;
    }

    private static XamlSchemaAttributeRule Rule(
        string name,
        string semantic,
        XamlSchemaAttributeTargets targets,
        bool inherited = false,
        int ctor = -1,
        bool allowMultiple = false) => new XamlSchemaAttributeRule(
            name,
            semantic,
            targets,
            inherited,
            ctor >= 0 ? XamlSchemaAttributeValueSource.ConstructorArgument : XamlSchemaAttributeValueSource.None,
            ctor,
            allowMultiple: allowMultiple);

    private static XamlSchemaAttributeRule Named(
        string name,
        string semantic,
        XamlSchemaAttributeTargets targets,
        string argument,
        bool inherited = false) => new XamlSchemaAttributeRule(
            name,
            semantic,
            targets,
            inherited,
            XamlSchemaAttributeValueSource.NamedArgument,
            namedArgument: argument);
}
