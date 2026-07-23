using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProGPU.WinUI.Themes.Fluent;
using Xunit;

namespace ProGPU.Tests.Headless;

[Collection("HeadlessTests")]
public sealed class FluentThemeRenderTests
{
    [Fact]
    public void EveryReachableFluentStyleRendersInOneHeadlessGallery()
    {
        var previousApplication =
            Application.Current;
        var previousTheme =
            ThemeManager.CurrentTheme;
        var previousHighContrast =
            ThemeManager.IsHighContrast;
        try
        {
            ThemeManager.CurrentTheme =
                ElementTheme.Light;
            ThemeManager.IsHighContrast =
                false;
            var application = new Application();
            Application.Current = application;
            var dictionary =
                FluentThemeResources.Apply(
                    application);
            var gallery = new WrapPanel
            {
                ItemWidth = 145f,
                ItemHeight = 70f,
                HorizontalSpacing = 2f,
                VerticalSpacing = 2f
            };
            var visitedDictionaries =
                new HashSet<ResourceDictionary>(
                    ReferenceEqualityComparer.Instance);
            var visitedStyles =
                new HashSet<Style>(
                    ReferenceEqualityComparer.Instance);
            var count = 0;

            foreach (var (_, style) in
                     EnumerateStyles(
                         dictionary,
                         visitedDictionaries))
            {
                if (!visitedStyles.Add(style) ||
                    style.TargetType is not
                        { } targetType ||
                    targetType.IsAbstract ||
                    !typeof(FrameworkElement)
                        .IsAssignableFrom(
                            targetType))
                {
                    continue;
                }

                var element =
                    Assert.IsAssignableFrom<
                        FrameworkElement>(
                        Activator.CreateInstance(
                            targetType));
                if (element is TextBox textBox)
                    textBox.Text =
                        targetType.Name;
                element.Style = style;
                gallery.Children.Add(element);
                count++;
            }

            Assert.True(
                count >= 100,
                "Expected at least 100 Fluent style targets in the render gallery, but added " +
                count +
                ".");

            using var window =
                new HeadlessWindow(
                    1600,
                    900)
                {
                    Content = gallery
                };
            window.Render();
            window.Render();
            var pixels = window.ReadPixels();
            var nonBackgroundPixels = 0;
            for (var index = 0;
                 index < pixels.Length;
                 index += 4)
            {
                if (Math.Abs(
                        pixels[index] -
                        20) > 5 ||
                    Math.Abs(
                        pixels[index + 1] -
                        20) > 5 ||
                    Math.Abs(
                        pixels[index + 2] -
                        30) > 5)
                {
                    nonBackgroundPixels++;
                }
            }

            Assert.True(
                nonBackgroundPixels >= 1_000,
                "The compiled Fluent gallery reached the compositor but rendered only " +
                nonBackgroundPixels +
                " non-background pixels.");
        }
        finally
        {
            Application.Current =
                previousApplication;
            ThemeManager.CurrentTheme =
                previousTheme;
            ThemeManager.IsHighContrast =
                previousHighContrast;
        }
    }

    private static IEnumerable<(object Key, Style Style)>
        EnumerateStyles(
            ResourceDictionary dictionary,
            ISet<ResourceDictionary> visited)
    {
        if (!visited.Add(dictionary))
            yield break;
        foreach (var pair in dictionary)
        {
            if (pair.Value is Style style)
                yield return (pair.Key, style);
        }

        foreach (var merged in
                 dictionary.MergedDictionaries)
        {
            foreach (var pair in
                     EnumerateStyles(
                         merged,
                         visited))
            {
                yield return pair;
            }
        }

        foreach (var themed in
                 dictionary.ThemeDictionaries
                     .Values)
        {
            if (themed is not
                ResourceDictionary theme)
            {
                continue;
            }
            foreach (var pair in
                     EnumerateStyles(
                         theme,
                         visited))
            {
                yield return pair;
            }
        }
    }
}
