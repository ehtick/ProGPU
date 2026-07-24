using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public sealed class TransformCollection : Collection<Transform>
{
    private readonly Action? _changed;

    public TransformCollection()
    {
    }

    internal TransformCollection(Action changed) => _changed = changed;

    protected override void InsertItem(int index, Transform item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        _changed?.Invoke();
    }

    protected override void SetItem(int index, Transform item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
        _changed?.Invoke();
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        _changed?.Invoke();
    }

    protected override void ClearItems()
    {
        if (Count == 0) return;
        base.ClearItems();
        _changed?.Invoke();
    }
}

[ContentProperty(Name = nameof(Children))]
public class TransformGroup : Transform
{
    private readonly HashSet<Transform> _subscribedChildren = new();

    public TransformGroup()
    {
        SetValue(ChildrenProperty, new TransformCollection(OnChildrenChanged));
    }

    private void SynchronizeSubscriptions()
    {
        var current = new HashSet<Transform>(Children);
        foreach (var child in _subscribedChildren)
        {
            if (!current.Contains(child)) child.Changed -= OnSubObjectChanged;
        }

        foreach (var child in current)
            if (!_subscribedChildren.Contains(child)) child.Changed += OnSubObjectChanged;

        _subscribedChildren.Clear();
        _subscribedChildren.UnionWith(current);
    }

    private void OnChildrenChanged()
    {
        SynchronizeSubscriptions();
        OnPropertyChanged(ChildrenProperty, Children, Children);
    }

    private void OnSubObjectChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        OnPropertyChanged(ChildrenProperty, this, this);
    }

    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            "Children",
            typeof(TransformCollection),
            typeof(TransformGroup),
            new PropertyMetadata(null) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });

    public new TransformCollection Children =>
        (TransformCollection)(GetValue(ChildrenProperty) ?? throw new InvalidOperationException("TransformGroup children were not initialized."));

    public override Matrix4x4 Value
    {
        get
        {
            var result = Matrix4x4.Identity;
            foreach (var child in Children)
            {
                result *= child.Value;
            }
            return result;
        }
    }
}
