using System;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml.Markup;

public class MarkupExtension
{
    protected virtual object? ProvideValue() => null;

    protected virtual object? ProvideValue(IXamlServiceProvider serviceProvider) => ProvideValue();

    internal object? Evaluate(IXamlServiceProvider? serviceProvider) =>
        serviceProvider == null ? ProvideValue() : ProvideValue(serviceProvider);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MarkupExtensionReturnTypeAttribute : Attribute
{
    public Type? ReturnType;
}

public interface IProvideValueTarget
{
    object? TargetObject { get; }
    object? TargetProperty { get; }
}

public interface IRootObjectProvider
{
    object? RootObject { get; }
}

public interface IUriContext
{
    Uri? BaseUri { get; }
}

public sealed class ProvideValueTargetProperty
{
    public ProvideValueTargetProperty(Type type, string name)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public Type Type { get; }
    public string Name { get; }
}
