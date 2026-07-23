using System;

namespace Microsoft.UI.Xaml.Data;

/// <summary>Marks a projected runtime class as eligible for runtime Binding discovery.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class BindableAttribute : Attribute
{
}
