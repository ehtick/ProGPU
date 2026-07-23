using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml;

[ContentProperty(Name = nameof(Setters))]
public class Style : DependencyObject
{
    public Type? TargetType { get; set; }
    public Style? BasedOn { get; set; }
    public bool IsSealed { get; private set; }
    public List<Setter> Setters { get; } = new();

    public Style()
    {
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

    internal void Seal() => IsSealed = true;
}

public class Setter
{
    public string Property { get; set; } = string.Empty;
    public TargetPropertyPath? Target { get; set; }
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
