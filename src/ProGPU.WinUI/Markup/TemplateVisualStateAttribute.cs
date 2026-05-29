using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Microsoft.UI.Xaml.Markup;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TemplateVisualStateAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    public TemplateVisualStateAttribute()
    {
    }
}
