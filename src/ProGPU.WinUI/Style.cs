using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml;

public class Style
{
    public Type TargetType { get; set; }
    public List<Setter> Setters { get; } = new();

    public Style()
    {
        TargetType = typeof(FrameworkElement);
    }

    public Style(Type targetType)
    {
        TargetType = targetType;
    }

    public void SetSetters(IEnumerable<Setter> setters)
    {
        Setters.Clear();
        Setters.AddRange(setters);
    }
}

public class Setter
{
    public string Property { get; set; } = string.Empty;
    public object? Value { get; set; }

    public Setter()
    {
    }

    public Setter(string property, object? value)
    {
        Property = property;
        Value = value;
    }
}
