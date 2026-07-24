using System;

namespace Microsoft.UI.Xaml.Markup;

/// <summary>Marks a XAML metadata provider whose component-library metadata is complete.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FullXamlMetadataProviderAttribute : Attribute
{
}
