using System;
using System.Collections.Generic;
using ProGPU.Xaml.Syntax;

namespace ProGPU.Xaml.Schema;

public enum XamlAllowedLocation
{
    None,
    Any,
    AttributeOnly,
    InitialMemberElementsOnly,
    AttributeOrInitialMemberElementsOnly
}

/// <summary>Canonical schema data for intrinsic language and XML directives.</summary>
public sealed class XamlIntrinsicMemberDefinition
{
    public XamlIntrinsicMemberDefinition(string namespaceUri, string name, XamlAllowedLocation allowedLocation)
    {
        NamespaceUri = namespaceUri;
        Name = name;
        AllowedLocation = allowedLocation;
    }
    public string NamespaceUri { get; }
    public string Name { get; }
    public XamlAllowedLocation AllowedLocation { get; }
    public bool IsDirective => true;
}

public static class XamlIntrinsicSchema
{
    private static readonly IReadOnlyDictionary<string, XamlIntrinsicMemberDefinition> Directives = CreateDirectives();

    public static bool TryGetDirective(string namespaceUri, string name, out XamlIntrinsicMemberDefinition? definition) =>
        Directives.TryGetValue(Key(namespaceUri, name), out definition);

    public static bool CanUseAsMemberElement(string namespaceUri, string name) =>
        TryGetDirective(namespaceUri, name, out var definition) &&
        (definition!.AllowedLocation == XamlAllowedLocation.InitialMemberElementsOnly ||
         definition.AllowedLocation == XamlAllowedLocation.AttributeOrInitialMemberElementsOnly);

    public static bool UsesInitializationText(string namespaceUri, string typeName)
    {
        if (!string.Equals(namespaceUri, XamlNamespaces.Language2006, StringComparison.Ordinal)) return false;
        switch (typeName)
        {
            case "String": case "Boolean": case "Byte": case "Int16": case "Int32": case "Int64":
            case "Single": case "Double": case "Decimal": case "Char": case "Uri": case "TimeSpan": case "Timespan":
                return true;
            default: return false;
        }
    }

    private static IReadOnlyDictionary<string, XamlIntrinsicMemberDefinition> CreateDirectives()
    {
        var result = new Dictionary<string, XamlIntrinsicMemberDefinition>(StringComparer.Ordinal);
        Add(result, XamlNamespaces.Language2006, "Name", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "Key", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "Uid", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "Class", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "Subclass", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "ClassModifier", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "FieldModifier", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "TypeArguments", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Language2006, "Arguments", XamlAllowedLocation.InitialMemberElementsOnly);
        Add(result, XamlNamespaces.Language2006, "FactoryMethod", XamlAllowedLocation.AttributeOrInitialMemberElementsOnly);
        Add(result, XamlNamespaces.Language2006, "Initialization", XamlAllowedLocation.None);
        Add(result, XamlNamespaces.Xml, "lang", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Xml, "space", XamlAllowedLocation.AttributeOnly);
        Add(result, XamlNamespaces.Xml, "base", XamlAllowedLocation.AttributeOnly);
        return result;
    }

    private static void Add(
        IDictionary<string, XamlIntrinsicMemberDefinition> values,
        string namespaceUri,
        string name,
        XamlAllowedLocation location) =>
        values.Add(Key(namespaceUri, name), new XamlIntrinsicMemberDefinition(namespaceUri, name, location));

    private static string Key(string namespaceUri, string name) => namespaceUri + "\0" + name;
}
