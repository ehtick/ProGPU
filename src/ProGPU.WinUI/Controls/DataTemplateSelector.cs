namespace Microsoft.UI.Xaml.Controls;

/// <summary>Selects a data template for content or an item using overridable typed hooks.</summary>
public class DataTemplateSelector
{
    public DataTemplate? SelectTemplate(object? item) => SelectTemplateCore(item);

    public DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        SelectTemplateCore(item, container);

    protected virtual DataTemplate? SelectTemplateCore(object? item) => null;

    protected virtual DataTemplate? SelectTemplateCore(object? item, DependencyObject container) =>
        SelectTemplateCore(item);
}
