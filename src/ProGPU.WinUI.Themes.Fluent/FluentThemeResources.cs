using Microsoft.UI.Xaml;

namespace ProGPU.WinUI.Themes.Fluent;

/// <summary>
/// Entry point for the source-generated, unchanged Microsoft UI XAML Fluent resource dictionary.
/// Referencing this type loads the assembly whose generated module initializer registers the URI.
/// </summary>
public static class FluentThemeResources
{
    public const string ResourcePath =
        "ProGPU.WinUI.Themes.Fluent/Themes/Generic.xaml";

    public static ResourceDictionary CreateDictionary() =>
        XamlResourceProviderRegistry.Create(
            new Uri(ResourcePath, UriKind.Relative));

    public static ResourceDictionary Apply(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var dictionary = CreateDictionary();
        application.Resources.MergedDictionaries.Add(dictionary);
        return dictionary;
    }
}
