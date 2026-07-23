namespace Microsoft.UI.Xaml;

/// <summary>
/// Provides the old and new values for a routed property change.
/// </summary>
public class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
{
    public RoutedPropertyChangedEventArgs(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public T OldValue { get; }

    public T NewValue { get; }
}
