using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace ProGPU.Xaml.Runtime;

/// <summary>
/// Typed runtime boundary used by generated XAML for custom WinUI markup extensions.
/// Compiler output supplies canonical target metadata; no runtime member discovery occurs.
/// </summary>
public static class WinUiMarkupExtensionRuntime
{
    public static T Evaluate<T>(
        MarkupExtension extension,
        object? targetObject,
        Type? targetType,
        string? targetMemberName,
        object? rootObject,
        string? resourceUri)
    {
        if (extension == null) throw new ArgumentNullException(nameof(extension));
        var targetProperty = targetType == null || string.IsNullOrEmpty(targetMemberName)
            ? null
            : new ProvideValueTargetProperty(targetType, targetMemberName!);
        var baseUri = string.IsNullOrEmpty(resourceUri)
            ? null
            : new Uri(resourceUri!, UriKind.RelativeOrAbsolute);
        var value = extension.Evaluate(new ServiceProvider(targetObject, targetProperty, rootObject, baseUri));
        if (value == null) return default!;
        return (T)value;
    }

    private sealed class ServiceProvider : IXamlServiceProvider, IProvideValueTarget, IRootObjectProvider, IUriContext
    {
        public ServiceProvider(object? targetObject, object? targetProperty, object? rootObject, Uri? baseUri)
        {
            TargetObject = targetObject;
            TargetProperty = targetProperty;
            RootObject = rootObject;
            BaseUri = baseUri;
        }

        public object? TargetObject { get; }
        public object? TargetProperty { get; }
        public object? RootObject { get; }
        public Uri? BaseUri { get; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IProvideValueTarget)) return this;
            if (serviceType == typeof(IRootObjectProvider)) return this;
            if (serviceType == typeof(IUriContext)) return this;
            if (serviceType == typeof(IXamlServiceProvider)) return this;
            return null;
        }
    }
}
