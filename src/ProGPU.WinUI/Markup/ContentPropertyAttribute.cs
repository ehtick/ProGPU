using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Microsoft.UI.Xaml.Markup;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ContentPropertyAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;

    public ContentPropertyAttribute()
    {
    }
}
