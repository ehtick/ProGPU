using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Microsoft.UI.Xaml.Markup;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TemplatePartAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
    public Type? Type { get; set; }

    public TemplatePartAttribute()
    {
    }
}
