using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>Mutable item collection owned by an <see cref="ItemsControl"/>.</summary>
public sealed class ItemCollection : IList<object>, IList
{
    private readonly ItemsControl _owner;
    private readonly List<object> _items = new();

    internal ItemCollection(ItemsControl owner) => _owner = owner;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public object this[int index]
    {
        get => _items[index];
        set
        {
            var oldValue = _items[index];
            _items[index] = value ?? throw new ArgumentNullException(nameof(value));
            _owner.OnItemsCollectionChanged();
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    value,
                    oldValue,
                    index));
        }
    }

    object IList<object>.this[int index]
    {
        get => this[index];
        set => this[index] = value;
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void Add(object item)
    {
        _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
        _owner.OnItemsCollectionChanged();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                item,
                _items.Count - 1));
    }

    int IList.Add(object? value)
    {
        Add(value ?? throw new ArgumentNullException(nameof(value)));
        return _items.Count - 1;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        _owner.OnItemsCollectionChanged();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
    }

    public bool Contains(object item) => _items.Contains(item);
    bool IList.Contains(object? value) => value != null && _items.Contains(value);
    public int IndexOf(object item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value == null ? -1 : _items.IndexOf(value);

    public void Insert(int index, object item)
    {
        _items.Insert(index, item ?? throw new ArgumentNullException(nameof(item)));
        _owner.OnItemsCollectionChanged();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                item,
                index));
    }

    void IList.Insert(int index, object? value) =>
        Insert(index, value ?? throw new ArgumentNullException(nameof(value)));

    public bool Remove(object item)
    {
        var index = _items.IndexOf(item);
        if (index < 0) return false;
        _items.RemoveAt(index);
        _owner.OnItemsCollectionChanged();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                item,
                index));
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value != null) Remove(value);
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _owner.OnItemsCollectionChanged();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                item,
                index));
    }

    public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    internal void ReplaceWith(IEnumerable? source)
    {
        _items.Clear();
        if (source != null)
        {
            foreach (var item in source)
            {
                if (item != null) _items.Add(item);
            }
        }
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
    }
}
