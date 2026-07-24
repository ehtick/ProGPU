using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>Provides the WinUI attached tooltip contract used by pointer hover input.</summary>
public static class ToolTipService
{
    public static readonly DependencyProperty ToolTipProperty = DependencyProperty.RegisterAttached(
        "ToolTip",
        typeof(object),
        typeof(ToolTipService),
        new PropertyMetadata(null));

    public static object? GetToolTip(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(ToolTipProperty);
    }

    public static void SetToolTip(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ToolTipProperty, value);
    }
}
