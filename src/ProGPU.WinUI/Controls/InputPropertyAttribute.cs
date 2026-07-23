using System;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// Identifies the primary editable property of an input control.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class InputPropertyAttribute : Attribute
{
    public string Name { get; set; } = string.Empty;
}
