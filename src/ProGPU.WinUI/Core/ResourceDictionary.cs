using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml;

public enum ResourceDictionaryChangeKind
{
    Local,
    MergedDictionaries,
    ThemeDictionaries,
    Source,
    Nested
}

public sealed class ResourceDictionaryChangedEventArgs : EventArgs
{
    internal ResourceDictionaryChangedEventArgs(
        ResourceDictionaryChangeKind kind,
        object? key,
        HashSet<ResourceDictionary> visited)
    { Kind = kind; Key = key; Visited = visited; }
    public ResourceDictionaryChangeKind Kind { get; }
    public object? Key { get; }
    internal HashSet<ResourceDictionary> Visited { get; }
}

[Markup.UsableDuringInitialization(true)]
public class ResourceDictionary : IDictionary<object, object>
{
    private readonly ObservableDictionary<object, object> _entries;
    private readonly ObservableList<ResourceDictionary> _mergedDictionaries;
    private readonly ObservableDictionary<object, object> _themeDictionaries;
    private Uri? _source;
    private int _updateDepth;
    private bool _updateChanged;

    public ResourceDictionary()
    {
        _entries = new ObservableDictionary<object, object>(
            _ => { }, _ => { },
            key => NotifyChanged(ResourceDictionaryChangeKind.Local, key));
        _mergedDictionaries = new ObservableList<ResourceDictionary>(
            SubscribeChild, UnsubscribeChild,
            () => NotifyChanged(ResourceDictionaryChangeKind.MergedDictionaries, null));
        _themeDictionaries = new ObservableDictionary<object, object>(
            value => { if (value is ResourceDictionary dictionary) SubscribeChild(dictionary); },
            value => { if (value is ResourceDictionary dictionary) UnsubscribeChild(dictionary); },
            key => NotifyChanged(ResourceDictionaryChangeKind.ThemeDictionaries, key));
    }

    public event EventHandler<ResourceDictionaryChangedEventArgs>? Changed;
    public long Generation { get; private set; }

    public Uri? Source
    {
        get => _source;
        set
        {
            if (Equals(_source, value)) return;
            if (value == null)
            {
                _source = null;
                NotifyChanged(ResourceDictionaryChangeKind.Source, null);
                return;
            }

            var loaded = XamlResourceProviderRegistry.Create(value);
            if (ReferenceEquals(loaded, this))
                throw new InvalidOperationException("A compiled resource provider cannot return the target dictionary itself.");
            BeginUpdate();
            try
            {
                ReplaceContents(loaded);
                _source = value;
                _updateChanged = true;
            }
            finally
            {
                EndUpdate(ResourceDictionaryChangeKind.Source);
            }
        }
    }

    public IList<ResourceDictionary> MergedDictionaries => _mergedDictionaries;
    public IDictionary<object, object> ThemeDictionaries => _themeDictionaries;

    public object this[object key] { get => _entries[key]; set => _entries[key] = value; }
    public ICollection<object> Keys => _entries.Keys;
    public ICollection<object> Values => _entries.Values;
    public int Count => _entries.Count;
    public bool IsReadOnly => false;
    public void Add(object key, object value) => _entries.Add(key, value);
    public void Add(KeyValuePair<object, object> item) => _entries.Add(item);
    public bool Remove(object key) => _entries.Remove(key);
    public bool Remove(KeyValuePair<object, object> item) => _entries.Remove(item);
    public void Clear() => _entries.Clear();
    public bool ContainsKey(object key) => _entries.ContainsKey(key);
    public bool Contains(KeyValuePair<object, object> item) => _entries.Contains(item);
    public bool TryGetValue(object key, out object value) => _entries.TryGetValue(key, out value!);
    public void CopyTo(KeyValuePair<object, object>[] array, int arrayIndex) => _entries.CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<object, object>> GetEnumerator() => _entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool TryLookup(object key, ElementTheme theme, out object? value)
    {
        return TryLookup(key, theme, isHighContrast: false, out value);
    }

    public bool TryLookup(
        object key,
        ElementTheme theme,
        bool isHighContrast,
        out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var themeKey = isHighContrast ? "HighContrast" : theme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Default"
        };
        return TryLookupCore(key, themeKey, new HashSet<ResourceDictionary>(), out value);
    }

    private bool TryLookupCore(
        object key,
        string themeKey,
        HashSet<ResourceDictionary> visited,
        out object? value)
    {
        if (!visited.Add(this)) { value = null; return false; }
        try
        {
            if (_themeDictionaries.TryGetValue(themeKey, out var themedValue) &&
                themedValue is ResourceDictionary themed &&
                themed.TryLookupCore(key, themeKey, visited, out value)) return true;
            if (!string.Equals(themeKey, "Default", StringComparison.Ordinal) &&
                _themeDictionaries.TryGetValue("Default", out var fallbackValue) &&
                fallbackValue is ResourceDictionary fallback &&
                fallback.TryLookupCore(key, "Default", visited, out value)) return true;
            if (_entries.TryGetValue(key, out var found)) { value = found; return true; }
            for (var index = _mergedDictionaries.Count - 1; index >= 0; index--)
                if (_mergedDictionaries[index].TryLookupCore(key, themeKey, visited, out value)) return true;
            value = null;
            return false;
        }
        finally { visited.Remove(this); }
    }

    private void ReplaceContents(ResourceDictionary source)
    {
        Clear();
        foreach (var pair in source) this[pair.Key] = pair.Value;
        _mergedDictionaries.ReplaceWith(source._mergedDictionaries);
        _themeDictionaries.ReplaceWith(source._themeDictionaries);
    }

    private void SubscribeChild(ResourceDictionary child) => child.Changed += OnChildChanged;
    private void UnsubscribeChild(ResourceDictionary child) => child.Changed -= OnChildChanged;
    private void OnChildChanged(object? sender, ResourceDictionaryChangedEventArgs args) =>
        Publish(ResourceDictionaryChangeKind.Nested, args.Key, args.Visited);

    private void BeginUpdate() => _updateDepth++;
    private void EndUpdate(ResourceDictionaryChangeKind kind)
    {
        if (--_updateDepth == 0 && _updateChanged)
        {
            _updateChanged = false;
            Publish(kind, null, new HashSet<ResourceDictionary>());
        }
    }

    private void NotifyChanged(ResourceDictionaryChangeKind kind, object? key)
    {
        if (_updateDepth != 0) { _updateChanged = true; return; }
        Publish(kind, key, new HashSet<ResourceDictionary>());
    }

    private void Publish(ResourceDictionaryChangeKind kind, object? key, HashSet<ResourceDictionary> visited)
    {
        if (!visited.Add(this)) return;
        Generation = unchecked(Generation + 1);
        Changed?.Invoke(this, new ResourceDictionaryChangedEventArgs(kind, key, visited));
    }

    private sealed class ObservableList<T> : IList<T>
    {
        private readonly List<T> _items = new();
        private readonly Action<T> _added;
        private readonly Action<T> _removed;
        private readonly Action _changed;
        public ObservableList(Action<T> added, Action<T> removed, Action changed)
        { _added = added; _removed = removed; _changed = changed; }
        public T this[int index] { get => _items[index]; set { var old = _items[index]; if (EqualityComparer<T>.Default.Equals(old, value)) return; _removed(old); _items[index] = value; _added(value); _changed(); } }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(T item) { _items.Add(item); _added(item); _changed(); }
        public void Clear() { if (_items.Count == 0) return; foreach (var item in _items) _removed(item); _items.Clear(); _changed(); }
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(T item) => _items.IndexOf(item);
        public void Insert(int index, T item) { _items.Insert(index, item); _added(item); _changed(); }
        public bool Remove(T item) { if (!_items.Remove(item)) return false; _removed(item); _changed(); return true; }
        public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); _removed(item); _changed(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void ReplaceWith(IEnumerable<T> values) { Clear(); foreach (var value in values) Add(value); }
    }

    private sealed class ObservableDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _items = new();
        private readonly Action<TValue> _added;
        private readonly Action<TValue> _removed;
        private readonly Action<TKey?> _changed;
        public ObservableDictionary(Action<TValue> added, Action<TValue> removed, Action<TKey?> changed)
        { _added = added; _removed = removed; _changed = changed; }
        public TValue this[TKey key] { get => _items[key]; set { if (_items.TryGetValue(key, out var old)) { if (EqualityComparer<TValue>.Default.Equals(old, value)) return; _removed(old); } _items[key] = value; _added(value); _changed(key); } }
        public ICollection<TKey> Keys => _items.Keys;
        public ICollection<TValue> Values => _items.Values;
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(TKey key, TValue value) { _items.Add(key, value); _added(value); _changed(key); }
        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
        public void Clear() { if (_items.Count == 0) return; foreach (var value in _items.Values) _removed(value); _items.Clear(); _changed(default); }
        public bool Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item);
        public bool ContainsKey(TKey key) => _items.ContainsKey(key);
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
        public bool Remove(TKey key) { if (!_items.Remove(key, out var value)) return false; _removed(value); _changed(key); return true; }
        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);
        public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value!);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void ReplaceWith(IEnumerable<KeyValuePair<TKey, TValue>> values) { Clear(); foreach (var pair in values) Add(pair); }
    }
}
