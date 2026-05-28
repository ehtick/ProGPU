using Thickness = Microsoft.UI.Xaml.Thickness;
using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using StackPanel = Microsoft.UI.Xaml.Controls.StackPanel;

namespace ProGPU.Samples;

public static class ColorPickerPage
{
    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var mainStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scrollViewer.Content = mainStack;

        // Header Title
        var title = new RichTextBlock { Font = AppState._font, FontSize = 22f, Margin = new Thickness(0, 0, 0, 6) };
        title.Inlines.Add(new Bold(new Run("ColorPicker Widget")));
        mainStack.AddChild(title);

        var desc = new RichTextBlock { Font = AppState._font, FontSize = 12f, Margin = new Thickness(0, 0, 0, 24) };
        desc.Inlines.Add(new Run("ColorPicker provides a visual Fluent interface for choosing colors. It features a Saturation-Value spectrum pad, a Hue rainbow slider, an Alpha transparency slider, Hex/RGBA string parsing inputs, and dual preview panels."));
        mainStack.AddChild(desc);

        // Grid split layout
        var splitGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        splitGrid.ColumnDefinitions.Add(new GridLength(1f, GridUnitType.Star));       // Left: Picker
        splitGrid.ColumnDefinitions.Add(new GridLength(20f, GridUnitType.Absolute));  // Gap
        splitGrid.ColumnDefinitions.Add(new GridLength(1.2f, GridUnitType.Star));     // Right: Live Target Card

        // Column 0: The Color Picker Control
        var pickerCard = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };

        var pickerStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var picker = new ColorPicker { Color = new Vector4(0.0f, 0.47f, 0.83f, 1.0f) }; // Start with Segoe Blue
        pickerStack.AddChild(picker);
        pickerCard.Child = pickerStack;
        splitGrid.AddChild(pickerCard);
        Grid.SetColumn(pickerCard, 0);

        // Column 2: Live target preview
        var targetCard = new Border
        {
            Background = new ThemeResourceBrush("CardBackground"),
            BorderBrush = new ThemeResourceBrush("ControlBorder"),
            BorderThickness = new Thickness(1f),
            CornerRadius = 8f,
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var targetStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch };
        var targetTitle = new RichTextBlock { Font = AppState._font, FontSize = 15f, Margin = new Thickness(0, 0, 0, 16) };
        targetTitle.Inlines.Add(new Bold(new Run("Live Render Binding Target")));
        targetStack.AddChild(targetTitle);

        // Decorative block to receive selected color
        var paintTarget = new Border
        {
            Height = 120f,
            Background = new SolidColorBrush(picker.Color),
            BorderBrush = new ThemeResourceBrush("TextPrimary"),
            BorderThickness = new Thickness(2f),
            CornerRadius = 12f,
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var paintText = new RichTextBlock
        {
            Font = AppState._font,
            FontSize = 13f,
            Foreground = new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        paintText.Inlines.Add(new Bold(new Run("DYNAMIC BRUSH")));
        paintTarget.Child = paintText;
        targetStack.AddChild(paintTarget);

        // Dynamic text readout
        var readout = new RichTextBlock { Font = AppState._font, FontSize = 12f, Margin = new Thickness(4, 0, 0, 0) };
        readout.Inlines.Add(new Run("Selected Color Vector Values:\n"));
        var vectorRun = new Run($"RGBA: {picker.Color.X:F2}, {picker.Color.Y:F2}, {picker.Color.Z:F2}, {picker.Color.W:F2}");
        readout.Inlines.Add(new Bold(vectorRun));
        targetStack.AddChild(readout);

        // Live Binding hook
        picker.ColorChanged += (s, ev) =>
        {
            paintTarget.Background = new SolidColorBrush(ev.NewColor);
            
            // Adjust the text color dynamically for contrast: if dark background, white text; if bright background, dark text
            float brightness = ev.NewColor.X * 0.299f + ev.NewColor.Y * 0.587f + ev.NewColor.Z * 0.114f;
            paintText.Foreground = (brightness > 0.55f) ? new SolidColorBrush(new Vector4(0.08f, 0.08f, 0.12f, 1f)) : new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f));

            vectorRun.Text = $"RGBA: {ev.NewColor.X:F2}, {ev.NewColor.Y:F2}, {ev.NewColor.Z:F2}, {ev.NewColor.W:F2}";
            
            paintTarget.Invalidate();
            readout.Invalidate();
        };

        targetCard.Child = targetStack;
        splitGrid.AddChild(targetCard);
        Grid.SetColumn(targetCard, 2);

        mainStack.AddChild(splitGrid);

        return scrollViewer;
    }
}
