using Microsoft.UI.Xaml.Markup;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace Microsoft.UI.Xaml.Data;

/// <summary>
/// A reflection-free accessor for one CLR binding-path segment. Source generators and
/// applications register these descriptors once, then the runtime composes them with
/// dependency-property segments.
/// </summary>
public interface IBindingMemberAccessor
{
    Type SourceType { get; }
    Type ValueType { get; }
    bool CanWrite { get; }
    object? GetValue(object source);
    void SetValue(object source, object? value);
}

/// <summary>
/// Process-wide typed binding accessor registry. Lookup is exact first, then base types and
/// interfaces in deterministic name order. Registration performs no object activation.
/// </summary>
public static class BindingMemberAccessorRegistry
{
    private static readonly ConcurrentDictionary<(Type SourceType, string MemberName), IBindingMemberAccessor>
        Accessors = new();

    public static void Register<TSource, TValue>(
        string memberName,
        Func<TSource, TValue> getter,
        Action<TSource, TValue>? setter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentNullException.ThrowIfNull(getter);
        Accessors[(typeof(TSource), memberName)] =
            new BindingMemberAccessor<TSource, TValue>(getter, setter);
    }

    public static bool TryGetAccessor(
        Type sourceType,
        string memberName,
        out IBindingMemberAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        for (var current = sourceType; current != null; current = current.BaseType)
        {
            if (Accessors.TryGetValue((current, memberName), out accessor!))
                return true;
        }

        var interfaces = sourceType.GetInterfaces();
        Array.Sort(
            interfaces,
            static (left, right) =>
                string.CompareOrdinal(left.FullName, right.FullName));
        for (var index = 0; index < interfaces.Length; index++)
        {
            if (Accessors.TryGetValue((interfaces[index], memberName), out accessor!))
                return true;
        }

        accessor = null!;
        return false;
    }

    private sealed class BindingMemberAccessor<TSource, TValue> : IBindingMemberAccessor
    {
        private readonly Func<TSource, TValue> _getter;
        private readonly Action<TSource, TValue>? _setter;

        public BindingMemberAccessor(
            Func<TSource, TValue> getter,
            Action<TSource, TValue>? setter)
        {
            _getter = getter;
            _setter = setter;
        }

        public Type SourceType => typeof(TSource);
        public Type ValueType => typeof(TValue);
        public bool CanWrite => _setter != null;

        public object? GetValue(object source) => _getter((TSource)source);

        public void SetValue(object source, object? value)
        {
            if (_setter == null)
                throw new InvalidOperationException(
                    $"Binding member on '{typeof(TSource).FullName}' is read-only.");
            var converted = XamlValueConverter.ConvertTo(typeof(TValue), value);
            _setter((TSource)source, (TValue)converted!);
        }
    }
}

public enum BindingExpressionStatus
{
    Inactive,
    Active,
    PathError,
    UpdateError,
    Detached
}

/// <summary>
/// Live binding state for one target dependency property. Path evaluation and subscription
/// rebuilding are O(P), where P is the number of path segments; steady updates allocate only
/// when an intermediate source instance changes.
/// </summary>
public sealed class BindingExpression : IDisposable
{
    private readonly DependencyObject _target;
    private readonly DependencyProperty _targetProperty;
    private readonly object? _context;
    private readonly object? _lookupRoot;
    private readonly List<Action> _sourceUnsubscribe = new();
    private long _targetCallbackToken;
    private bool _updating;
    private bool _disposed;
    private object? _leafSource;
    private DependencyProperty? _leafProperty;
    private IBindingMemberAccessor? _leafAccessor;
    private long _focusCallbackToken;

    internal BindingExpression(
        DependencyObject target,
        DependencyProperty targetProperty,
        Binding binding,
        object? context,
        object? lookupRoot)
    {
        _target = target;
        _targetProperty = targetProperty;
        ParentBinding = binding;
        _context = context;
        _lookupRoot = lookupRoot;
        Status = BindingExpressionStatus.Inactive;

        if (binding.Mode == BindingMode.TwoWay &&
            binding.UpdateSourceTrigger is UpdateSourceTrigger.Default or
                UpdateSourceTrigger.PropertyChanged)
        {
            _targetCallbackToken = target.RegisterPropertyChangedCallback(
                targetProperty,
                OnTargetPropertyChanged);
        }
        else if (binding.Mode == BindingMode.TwoWay &&
                 binding.UpdateSourceTrigger == UpdateSourceTrigger.LostFocus &&
                 target is global::Microsoft.UI.Xaml.Controls.Control control)
        {
            _focusCallbackToken = control.RegisterPropertyChangedCallback(
                global::Microsoft.UI.Xaml.Controls.Control.IsFocusedProperty,
                OnTargetFocusChanged);
        }

        UpdateTarget();
    }

    public Binding ParentBinding { get; }
    internal DependencyObject Target => _target;
    internal DependencyProperty TargetProperty => _targetProperty;
    internal object? Context => _context;
    public BindingExpressionStatus Status { get; private set; }
    public string? Error { get; private set; }

    public void UpdateTarget()
    {
        ThrowIfDisposed();
        if (_updating) return;
        _updating = true;
        try
        {
            ResetSourceSubscriptions();
            if (!TryEvaluatePath(out var value, subscribe: ParentBinding.Mode != BindingMode.OneTime))
            {
                value = ParentBinding.FallbackValue ?? _targetProperty.Metadata?.DefaultValue;
                Status = BindingExpressionStatus.PathError;
            }
            else
            {
                if (value == null && ParentBinding.TargetNullValue != null)
                    value = ParentBinding.TargetNullValue;
                Status = BindingExpressionStatus.Active;
                Error = null;
            }

            if (ParentBinding.Converter is IValueConverter converter)
            {
                value = converter.Convert(
                    value,
                    _targetProperty.PropertyType,
                    ParentBinding.ConverterParameter,
                    ParentBinding.ConverterLanguage ?? string.Empty);
            }

            _target.SetValue(
                _targetProperty,
                XamlValueConverter.ConvertTo(_targetProperty.PropertyType, value));
        }
        catch (Exception exception)
        {
            Status = BindingExpressionStatus.UpdateError;
            Error = exception.Message;
            var fallback = ParentBinding.FallbackValue ?? _targetProperty.Metadata?.DefaultValue;
            _target.SetValue(
                _targetProperty,
                XamlValueConverter.ConvertTo(_targetProperty.PropertyType, fallback));
        }
        finally
        {
            _updating = false;
        }
    }

    public void UpdateSource()
    {
        ThrowIfDisposed();
        if (_updating || ParentBinding.Mode != BindingMode.TwoWay) return;
        _updating = true;
        try
        {
            if (_leafSource == null && !TryEvaluatePath(out _, subscribe: true))
            {
                Status = BindingExpressionStatus.PathError;
                return;
            }

            var value = _target.GetValue(_targetProperty);
            if (ParentBinding.Converter is IValueConverter converter)
            {
                var sourceType = _leafProperty?.PropertyType ??
                                 _leafAccessor?.ValueType ??
                                 typeof(object);
                value = converter.ConvertBack(
                    value,
                    sourceType,
                    ParentBinding.ConverterParameter,
                    ParentBinding.ConverterLanguage ?? string.Empty);
            }

            if (_leafSource is DependencyObject dependencySource &&
                _leafProperty is not null)
            {
                dependencySource.SetValue(
                    _leafProperty,
                    XamlValueConverter.ConvertTo(_leafProperty.PropertyType, value));
                Status = BindingExpressionStatus.Active;
                Error = null;
                return;
            }
            if (_leafSource != null && _leafAccessor?.CanWrite == true)
            {
                _leafAccessor.SetValue(_leafSource, value);
                Status = BindingExpressionStatus.Active;
                Error = null;
                return;
            }

            Status = BindingExpressionStatus.PathError;
            Error = "The binding path does not end in a writable member.";
        }
        catch (Exception exception)
        {
            Status = BindingExpressionStatus.UpdateError;
            Error = exception.Message;
        }
        finally
        {
            _updating = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetSourceSubscriptions();
        if (_targetCallbackToken != 0)
        {
            _target.UnregisterPropertyChangedCallback(
                _targetProperty,
                _targetCallbackToken);
            _targetCallbackToken = 0;
        }
        if (_focusCallbackToken != 0 &&
            _target is global::Microsoft.UI.Xaml.Controls.Control control)
        {
            control.UnregisterPropertyChangedCallback(
                global::Microsoft.UI.Xaml.Controls.Control.IsFocusedProperty,
                _focusCallbackToken);
            _focusCallbackToken = 0;
        }
        Status = BindingExpressionStatus.Detached;
    }

    private bool TryEvaluatePath(out object? value, bool subscribe)
    {
        _leafSource = null;
        _leafProperty = null;
        _leafAccessor = null;

        object? current = ResolveSource(subscribe);
        var path = ParentBinding.Path?.Trim();
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            value = current;
            return current != null;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (current == null)
            {
                value = null;
                Error = $"Binding path '{path}' reached null before '{segments[index]}'.";
                return false;
            }

            var segment = segments[index].Trim();
            var isLeaf = index == segments.Length - 1;
            if (current is DependencyObject dependencySource &&
                DependencyProperty.Lookup(dependencySource.GetType(), segment) is { } property)
            {
                if (subscribe)
                {
                    var token = dependencySource.RegisterPropertyChangedCallback(
                        property,
                        OnSourcePropertyChanged);
                    _sourceUnsubscribe.Add(
                        () => dependencySource.UnregisterPropertyChangedCallback(property, token));
                }
                if (isLeaf)
                {
                    _leafSource = dependencySource;
                    _leafProperty = property;
                }
                current = dependencySource.GetValue(property);
                continue;
            }

            if (!BindingMemberAccessorRegistry.TryGetAccessor(
                    current.GetType(),
                    segment,
                    out var accessor))
            {
                value = null;
                Error =
                    $"No typed binding accessor is registered for '{current.GetType().FullName}.{segment}'.";
                return false;
            }
            if (subscribe && current is INotifyPropertyChanged notifier)
            {
                PropertyChangedEventHandler handler = (_, args) =>
                {
                    if (string.IsNullOrEmpty(args.PropertyName) ||
                        string.Equals(args.PropertyName, segment, StringComparison.Ordinal))
                        UpdateTarget();
                };
                notifier.PropertyChanged += handler;
                _sourceUnsubscribe.Add(() => notifier.PropertyChanged -= handler);
            }
            if (isLeaf)
            {
                _leafSource = current;
                _leafAccessor = accessor;
            }
            current = accessor.GetValue(current);
        }

        value = current;
        return true;
    }

    private object? ResolveSource(bool subscribe)
    {
        if (ParentBinding.Source != null)
            return ParentBinding.Source;
        switch (ParentBinding.RelativeSource?.Mode)
        {
            case RelativeSourceMode.Self:
                return _target;
            case RelativeSourceMode.TemplatedParent:
                return _context;
        }
        if (!string.IsNullOrWhiteSpace(ParentBinding.ElementName))
        {
            return (_lookupRoot as FrameworkElement)?.FindName(ParentBinding.ElementName!) ??
                   (_target as FrameworkElement)?.FindName(ParentBinding.ElementName!);
        }
        if (_target is FrameworkElement targetElement)
        {
            if (subscribe)
            {
                var token = targetElement.RegisterPropertyChangedCallback(
                    FrameworkElement.DataContextProperty,
                    OnSourcePropertyChanged);
                _sourceUnsubscribe.Add(
                    () => targetElement.UnregisterPropertyChangedCallback(
                        FrameworkElement.DataContextProperty,
                        token));
            }
            return targetElement.DataContext;
        }
        return _context;
    }

    private void OnSourcePropertyChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (!_disposed)
            UpdateTarget();
    }

    private void OnTargetPropertyChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (!_disposed)
            UpdateSource();
    }

    private void OnTargetFocusChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        if (!_disposed && args.NewValue is false)
            UpdateSource();
    }

    private void ResetSourceSubscriptions()
    {
        for (var index = _sourceUnsubscribe.Count - 1; index >= 0; index--)
            _sourceUnsubscribe[index]();
        _sourceUnsubscribe.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BindingExpression));
    }
}
