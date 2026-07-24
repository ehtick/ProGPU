using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Xaml.Controls;

public class TemplateBinding : IDisposable
{
    private static readonly ConditionalWeakTable<DependencyObject, BindingStore> Stores = new();
    private readonly WeakReference<DependencyObject> _targetRef;
    private readonly DependencyProperty _targetProperty;
    private readonly WeakReference<DependencyObject> _sourceRef;
    private readonly DependencyProperty _sourceProperty;
    private readonly long _token;
    private bool _disposed;

    public TemplateBinding(DependencyObject target, DependencyProperty targetProperty, DependencyObject source, DependencyProperty sourceProperty)
    {
        _targetRef = new WeakReference<DependencyObject>(target);
        _targetProperty = targetProperty;
        _sourceRef = new WeakReference<DependencyObject>(source);
        _sourceProperty = sourceProperty;

        // Apply initial value immediately
        UpdateTarget(source.GetValue(sourceProperty));

        // Hook up standard DependencyProperty callback
        _token = source.RegisterPropertyChangedCallback(sourceProperty, OnSourcePropertyChanged);
        Stores.GetOrCreateValue(source).Bindings.Add(this);
    }

    public static TemplateBinding Bind(DependencyObject target, DependencyProperty targetProperty, DependencyObject source, DependencyProperty sourceProperty)
    {
        return new TemplateBinding(target, targetProperty, source, sourceProperty);
    }

    internal static void ClearBindingsForSource(DependencyObject source)
    {
        if (!Stores.TryGetValue(source, out var store))
            return;
        Stores.Remove(source);
        var bindings = store.Bindings.ToArray();
        store.Bindings.Clear();
        for (var index = 0; index < bindings.Length; index++)
            bindings[index].Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_sourceRef.TryGetTarget(out var source))
            return;
        source.UnregisterPropertyChangedCallback(_sourceProperty, _token);
        if (Stores.TryGetValue(source, out var store))
            store.Bindings.Remove(this);
    }

    private void OnSourcePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_disposed)
            UpdateTarget(e.NewValue);
    }

    private void UpdateTarget(object? newValue)
    {
        if (_targetRef.TryGetTarget(out var target))
        {
            var converted = XamlValueConverter.ConvertTo(_targetProperty.PropertyType, newValue);
            target.SetValue(_targetProperty, converted);
        }
        else
        {
            if (_sourceRef.TryGetTarget(out var source))
            {
                source.UnregisterPropertyChangedCallback(_sourceProperty, _token);
            }
        }
    }

    private sealed class BindingStore
    {
        public HashSet<TemplateBinding> Bindings { get; } = new();
    }
}

/// <summary>String-path fallback used only when a framework property has no public identifier field.</summary>
public static class XamlTemplateBindingRuntime
{
    public static TemplateBinding Bind(
        DependencyObject target,
        string targetPropertyName,
        object source,
        string sourcePropertyName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(targetPropertyName);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(sourcePropertyName);
        if (source is not DependencyObject dependencySource)
            throw new ArgumentException("Template binding source must be a DependencyObject.", nameof(source));
        var targetProperty = DependencyProperty.Lookup(target.GetType(), targetPropertyName) ??
            throw new InvalidOperationException(
                $"Dependency property '{targetPropertyName}' was not registered for '{target.GetType().FullName}'.");
        var sourceProperty =
            DependencyProperty.Lookup(dependencySource.GetType(), sourcePropertyName) ??
            ResolveAttachedProperty(sourcePropertyName) ??
            throw new InvalidOperationException(
                $"Dependency property '{sourcePropertyName}' was not registered for '{dependencySource.GetType().FullName}'.");
        return TemplateBinding.Bind(target, targetProperty, dependencySource, sourceProperty);
    }

    private static DependencyProperty? ResolveAttachedProperty(string propertyName)
    {
        var separator = propertyName.IndexOf('.', StringComparison.Ordinal);
        return separator >= 0
            ? DependencyProperty.LookupRegisteredOwner(
                propertyName[..separator],
                propertyName[(separator + 1)..])
            : DependencyProperty.LookupUniqueRegisteredProperty(propertyName);
    }
}
