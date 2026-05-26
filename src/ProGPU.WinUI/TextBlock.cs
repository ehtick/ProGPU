using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System;

namespace Microsoft.UI.Xaml.Controls;

public class TextBlock : RichTextBlock
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            "Text",
            typeof(string),
            typeof(TextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tb = (TextBlock)d;
        tb.Inlines.Clear();
        tb.Inlines.Add(new Run { Text = (string)(e.NewValue ?? string.Empty) });
        tb.InvalidateLayout();
        tb.Invalidate();
    }
}
