using System;

namespace Microsoft.UI.Xaml.Markup;

/// <summary>
/// Declares that the XAML object writer may publish an instance to its parent before
/// populating the instance's members.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UsableDuringInitializationAttribute : Attribute
{
    public UsableDuringInitializationAttribute(bool usable)
    {
        Usable = usable;
    }

    public bool Usable { get; }
}
