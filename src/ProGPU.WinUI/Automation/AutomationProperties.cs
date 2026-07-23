using Microsoft.UI.Xaml.Automation.Peers;

namespace Microsoft.UI.Xaml.Automation;

/// <summary>Hosts UI Automation values as typed WinUI attached dependency properties.</summary>
public sealed class AutomationProperties
{
    private AutomationProperties()
    {
    }

    public static readonly DependencyProperty AccessibilityViewProperty = DependencyProperty.RegisterAttached(
        "AccessibilityView",
        typeof(AccessibilityView),
        typeof(AutomationProperties),
        new PropertyMetadata(AccessibilityView.Content));

    public static AccessibilityView GetAccessibilityView(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (AccessibilityView)(element.GetValue(AccessibilityViewProperty) ?? AccessibilityView.Content);
    }

    public static void SetAccessibilityView(DependencyObject element, AccessibilityView value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AccessibilityViewProperty, value);
    }

    public static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached(
        "Name", typeof(string), typeof(AutomationProperties), new PropertyMetadata(string.Empty));

    public static string GetName(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(NameProperty) ?? string.Empty;
    }

    public static void SetName(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(NameProperty, value ?? string.Empty);
    }

    public static readonly DependencyProperty AutomationIdProperty = DependencyProperty.RegisterAttached(
        "AutomationId", typeof(string), typeof(AutomationProperties), new PropertyMetadata(string.Empty));

    public static string GetAutomationId(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(AutomationIdProperty) ?? string.Empty;
    }

    public static void SetAutomationId(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AutomationIdProperty, value ?? string.Empty);
    }
}
