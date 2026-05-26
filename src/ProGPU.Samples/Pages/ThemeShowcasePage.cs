using Thickness = Microsoft.UI.Xaml.Thickness;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Vector;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using Button = Microsoft.UI.Xaml.Controls.Button;
using StackPanel = Microsoft.UI.Xaml.Controls.StackPanel;

namespace ProGPU.Samples;

public static class ThemeShowcasePage
{
    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer { Background = new ThemeResourceBrush("PageBackground"), Font = AppState._font };
        var mainStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(24) };
        scrollViewer.Content = mainStack;

        // Header
        var header = new RichTextBlock { Margin = new Thickness(0, 0, 0, 24) };
        header.Inlines.Add(new Bold(new Run("Compiled C# Theme Showcase") { FontSize = 28f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        header.Inlines.Add(new LineBreak());
        header.Inlines.Add(new Run("Demonstrating modern WinUI 3 control styles written in pure C#.") { FontSize = 14f, Foreground = new ThemeResourceBrush("TextSecondary") });
        mainStack.AddChild(header);

        // Buttons Section
        var buttonsSection = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 32) };
        var buttonsTitle = new RichTextBlock { Margin = new Thickness(0, 0, 0, 12) };
        buttonsTitle.Inlines.Add(new Bold(new Run("Buttons") { FontSize = 18f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        buttonsSection.AddChild(buttonsTitle);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        
        var defaultButton = new Button { Margin = new Thickness(0, 0, 12, 0) };
        var btnText1 = new RichTextBlock();
        btnText1.Inlines.Add(new Run("Default Button") { Foreground = new ThemeResourceBrush("TextPrimary") });
        defaultButton.Content = btnText1;
        buttonsRow.AddChild(defaultButton);

        var accentButton = new Button { Margin = new Thickness(0, 0, 12, 0) };
        accentButton.Background = new ThemeResourceBrush("SystemAccentColor");
        var btnText2 = new RichTextBlock();
        btnText2.Inlines.Add(new Run("Accent Button") { Foreground = new ThemeResourceBrush("TextPrimary") });
        accentButton.Content = btnText2;
        buttonsRow.AddChild(accentButton);

        var disabledButton = new Button { IsEnabled = false };
        var btnText3 = new RichTextBlock();
        btnText3.Inlines.Add(new Run("Disabled Button") { Foreground = new ThemeResourceBrush("TextSecondary") });
        disabledButton.Content = btnText3;
        buttonsRow.AddChild(disabledButton);

        buttonsSection.AddChild(buttonsRow);
        mainStack.AddChild(buttonsSection);

        // ToggleSwitches Section
        var togglesSection = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 32) };
        var togglesTitle = new RichTextBlock { Margin = new Thickness(0, 0, 0, 12) };
        togglesTitle.Inlines.Add(new Bold(new Run("Toggle Switches") { FontSize = 18f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        togglesSection.AddChild(togglesTitle);

        var togglesRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        
        var defaultToggle = new ToggleSwitch { IsOn = true, Margin = new Thickness(0, 0, 24, 0) };
        var toggleText1 = new RichTextBlock();
        toggleText1.Inlines.Add(new Run("Toggle Switch On") { Foreground = new ThemeResourceBrush("TextPrimary") });
        defaultToggle.Content = toggleText1;
        togglesRow.AddChild(defaultToggle);

        var offToggle = new ToggleSwitch { IsOn = false, Margin = new Thickness(0, 0, 24, 0) };
        var toggleText2 = new RichTextBlock();
        toggleText2.Inlines.Add(new Run("Toggle Switch Off") { Foreground = new ThemeResourceBrush("TextPrimary") });
        offToggle.Content = toggleText2;
        togglesRow.AddChild(offToggle);

        var disabledToggle = new ToggleSwitch { IsEnabled = false };
        var toggleText3 = new RichTextBlock();
        toggleText3.Inlines.Add(new Run("Disabled Toggle") { Foreground = new ThemeResourceBrush("TextSecondary") });
        disabledToggle.Content = toggleText3;
        togglesRow.AddChild(disabledToggle);

        togglesSection.AddChild(togglesRow);
        mainStack.AddChild(togglesSection);

        // Sliders Section
        var slidersSection = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 32) };
        var slidersTitle = new RichTextBlock { Margin = new Thickness(0, 0, 0, 12) };
        slidersTitle.Inlines.Add(new Bold(new Run("Sliders") { FontSize = 18f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        slidersSection.AddChild(slidersTitle);

        var slidersStack = new StackPanel { Orientation = Orientation.Vertical };
        
        var sliderRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var defaultSlider = new Slider { Minimum = 0, Maximum = 100, Value = 45, Width = 250f, Margin = new Thickness(0, 0, 16, 0) };
        var sliderValueText = new RichTextBlock { VerticalAlignment = VerticalAlignment.Center };
        sliderValueText.Inlines.Add(new Run("Value: ") { Foreground = new ThemeResourceBrush("TextPrimary") });
        sliderValueText.Inlines.Add(new Run("45") { Foreground = new ThemeResourceBrush("SystemAccentColor") });
        sliderRow1.AddChild(defaultSlider);
        sliderRow1.AddChild(sliderValueText);
        slidersStack.AddChild(sliderRow1);

        var sliderRow2 = new StackPanel { Orientation = Orientation.Horizontal };
        var disabledSlider = new Slider { Minimum = 0, Maximum = 100, Value = 75, Width = 250f, IsEnabled = false, Margin = new Thickness(0, 0, 16, 0) };
        var sliderDisabledText = new RichTextBlock { VerticalAlignment = VerticalAlignment.Center };
        sliderDisabledText.Inlines.Add(new Run("Disabled (75%)") { Foreground = new ThemeResourceBrush("TextSecondary") });
        sliderRow2.AddChild(disabledSlider);
        sliderRow2.AddChild(sliderDisabledText);
        slidersStack.AddChild(sliderRow2);

        slidersSection.AddChild(slidersStack);
        mainStack.AddChild(slidersSection);

        // Interactive Playground Section
        var interactiveSection = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 32) };
        var interactiveTitle = new RichTextBlock { Margin = new Thickness(0, 0, 0, 12) };
        interactiveTitle.Inlines.Add(new Bold(new Run("Interactive Playground") { FontSize = 18f, Foreground = new ThemeResourceBrush("TextPrimary") }));
        interactiveTitle.Inlines.Add(new LineBreak());
        interactiveTitle.Inlines.Add(new Run("Dynamically adjust parameters to see properties and themes in action.") { FontSize = 12f, Foreground = new ThemeResourceBrush("TextSecondary") });
        interactiveSection.AddChild(interactiveTitle);

        var interactiveRow = new StackPanel { Orientation = Orientation.Horizontal };
        
        var interactiveButton = new Button { Margin = new Thickness(0, 0, 24, 0) };
        var btnText4 = new RichTextBlock();
        btnText4.Inlines.Add(new Run("Click Counter: 0") { Foreground = new ThemeResourceBrush("TextPrimary") });
        interactiveButton.Content = btnText4;
        interactiveRow.AddChild(interactiveButton);

        var themeToggle = new ToggleSwitch { IsOn = ThemeManager.CurrentTheme == ElementTheme.Dark };
        var toggleText4 = new RichTextBlock();
        toggleText4.Inlines.Add(new Run("Dark Mode Active") { Foreground = new ThemeResourceBrush("TextPrimary") });
        themeToggle.Content = toggleText4;
        interactiveRow.AddChild(themeToggle);

        interactiveSection.AddChild(interactiveRow);
        mainStack.AddChild(interactiveSection);

        // Wire up interaction
        int clickCount = 0;
        interactiveButton.Click += (s, e) =>
        {
            clickCount++;
            var text = new RichTextBlock();
            text.Inlines.Add(new Run($"Click Counter: {clickCount}") { Foreground = new ThemeResourceBrush("TextPrimary") });
            interactiveButton.Content = text;
            interactiveButton.Invalidate();
        };

        defaultSlider.ValueChanged += (s, e) =>
        {
            sliderValueText.Inlines.Clear();
            sliderValueText.Inlines.Add(new Run("Value: ") { Foreground = new ThemeResourceBrush("TextPrimary") });
            sliderValueText.Inlines.Add(new Run($"{defaultSlider.Value:F0}") { Foreground = new ThemeResourceBrush("SystemAccentColor") });
            sliderValueText.Invalidate();
        };

        themeToggle.Toggled += (s, e) =>
        {
            ThemeManager.CurrentTheme = themeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        };

        return scrollViewer;
    }
}
