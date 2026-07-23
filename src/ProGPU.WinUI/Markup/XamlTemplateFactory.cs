using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Markup;

/// <summary>Typed runtime publication seam used by generated deferred XAML factories.</summary>
public static class XamlTemplateFactory
{
    private static readonly ConditionalWeakTable<
        FrameworkElement,
        TemplateInstance> Instances = new();

    public static void SetFactory(FrameworkTemplate template, Func<object?, FrameworkElement> factory)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(factory);
        template.DeferredFactory = factory;
    }

    public static FrameworkElement? Build(FrameworkTemplate? template, object? context = null) =>
        template?.DeferredFactory?.Invoke(context);

    /// <summary>
    /// Commits one generated compiled-binding group to its materialized template root.
    /// Initialization occurs only after the complete root has been constructed. If activation
    /// fails, the group is detached transactionally before the exception escapes.
    /// </summary>
    public static void AttachBindings(
        FrameworkElement root,
        ICompiledBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(bindings);
        Release(root);
        var instance = new TemplateInstance(bindings);
        Instances.Add(root, instance);
        root.Unloaded += OnRootUnloaded;
        try
        {
            bindings.Initialize();
        }
        catch
        {
            Release(root);
            throw;
        }
    }

    /// <summary>
    /// Releases the exact compiled-binding group owned by a materialized template root.
    /// Other roots created for the same data item remain active.
    /// </summary>
    public static void Release(FrameworkElement? root)
    {
        if (root == null ||
            !Instances.TryGetValue(root, out var instance))
            return;
        Instances.Remove(root);
        root.Unloaded -= OnRootUnloaded;
        CompiledBindingOperations.DisposeBindings(instance.Bindings);
    }

    /// <summary>
    /// Releases every materialized template group contained in a visual subtree. Generated
    /// page/control replacement uses this before detaching an old tree so nested data-template
    /// subscriptions cannot survive hot reload.
    /// </summary>
    public static void ReleaseSubtree(FrameworkElement? root)
    {
        if (root == null)
            return;
        var pending = new Stack<Visual>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var visual = pending.Pop();
            if (visual is ContainerVisual container)
            {
                var children = container.Children;
                for (var index = children.Count - 1; index >= 0; index--)
                    pending.Push(children[index]);
            }
            if (visual is FrameworkElement element)
                Release(element);
        }
    }

    private static void OnRootUnloaded(object sender, RoutedEventArgs args)
    {
        _ = args;
        Release(sender as FrameworkElement);
    }

    private sealed class TemplateInstance
    {
        public TemplateInstance(ICompiledBindings bindings) =>
            Bindings = bindings;

        public ICompiledBindings Bindings { get; }
    }
}
