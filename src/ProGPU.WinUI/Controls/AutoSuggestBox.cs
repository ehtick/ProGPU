using System;
using System.Collections.Generic;

namespace Microsoft.UI.Xaml.Controls;

public enum AutoSuggestionBoxTextChangeReason
{
    UserInput = 0,
    ProgrammaticChange = 1,
    SuggestionChosen = 2
}

public sealed class AutoSuggestBoxTextChangedEventArgs : EventArgs
{
    public AutoSuggestBoxTextChangedEventArgs(AutoSuggestionBoxTextChangeReason reason) => Reason = reason;
    public AutoSuggestionBoxTextChangeReason Reason { get; }
}

public sealed class AutoSuggestBoxSuggestionChosenEventArgs : EventArgs
{
    public AutoSuggestBoxSuggestionChosenEventArgs(object selectedItem) => SelectedItem = selectedItem;
    public object SelectedItem { get; }
}

public sealed class AutoSuggestBoxQuerySubmittedEventArgs : EventArgs
{
    public AutoSuggestBoxQuerySubmittedEventArgs(string queryText, object? chosenSuggestion)
    {
        QueryText = queryText;
        ChosenSuggestion = chosenSuggestion;
    }

    public string QueryText { get; }
    public object? ChosenSuggestion { get; }
}

/// <summary>
/// Text input control that exposes a live suggestion collection.
/// </summary>
[InputProperty(Name = nameof(Text))]
public sealed class AutoSuggestBox : ItemsControl
{
    public static readonly DependencyProperty MaxSuggestionListHeightProperty = Register(nameof(MaxSuggestionListHeight), double.PositiveInfinity);
    public static readonly DependencyProperty IsSuggestionListOpenProperty = Register(nameof(IsSuggestionListOpen), false);
    public static readonly DependencyProperty TextMemberPathProperty = Register(nameof(TextMemberPath), string.Empty);
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), string.Empty, OnTextChanged);
    public static readonly DependencyProperty UpdateTextOnSelectProperty = Register(nameof(UpdateTextOnSelect), true);
    public static readonly DependencyProperty PlaceholderTextProperty = Register(nameof(PlaceholderText), string.Empty);
    public static readonly DependencyProperty HeaderProperty = Register<object?>(nameof(Header), null);
    public static readonly DependencyProperty AutoMaximizeSuggestionAreaProperty = Register(nameof(AutoMaximizeSuggestionArea), false);
    public static readonly DependencyProperty TextBoxStyleProperty = Register<Style?>(nameof(TextBoxStyle), null);
    public static readonly DependencyProperty QueryIconProperty = Register<IconElement?>(nameof(QueryIcon), null);
    public static readonly DependencyProperty LightDismissOverlayModeProperty = Register(nameof(LightDismissOverlayMode), LightDismissOverlayMode.Auto);
    public static readonly DependencyProperty DescriptionProperty = Register<object?>(nameof(Description), null);

    private AutoSuggestionBoxTextChangeReason _pendingChangeReason = AutoSuggestionBoxTextChangeReason.ProgrammaticChange;

    public double MaxSuggestionListHeight { get => (double)(GetValue(MaxSuggestionListHeightProperty) ?? double.PositiveInfinity); set => SetValue(MaxSuggestionListHeightProperty, value); }
    public bool IsSuggestionListOpen { get => (bool)(GetValue(IsSuggestionListOpenProperty) ?? false); set => SetValue(IsSuggestionListOpenProperty, value); }
    public string TextMemberPath { get => GetValue(TextMemberPathProperty) as string ?? string.Empty; set => SetValue(TextMemberPathProperty, value ?? string.Empty); }
    public string Text { get => GetValue(TextProperty) as string ?? string.Empty; set => SetText(value, AutoSuggestionBoxTextChangeReason.ProgrammaticChange); }
    public bool UpdateTextOnSelect { get => (bool)(GetValue(UpdateTextOnSelectProperty) ?? true); set => SetValue(UpdateTextOnSelectProperty, value); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty) as string ?? string.Empty; set => SetValue(PlaceholderTextProperty, value ?? string.Empty); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public bool AutoMaximizeSuggestionArea { get => (bool)(GetValue(AutoMaximizeSuggestionAreaProperty) ?? false); set => SetValue(AutoMaximizeSuggestionAreaProperty, value); }
    public Style? TextBoxStyle { get => GetValue(TextBoxStyleProperty) as Style; set => SetValue(TextBoxStyleProperty, value); }
    public IconElement? QueryIcon { get => GetValue(QueryIconProperty) as IconElement; set => SetValue(QueryIconProperty, value); }
    public LightDismissOverlayMode LightDismissOverlayMode { get => (LightDismissOverlayMode)(GetValue(LightDismissOverlayModeProperty) ?? LightDismissOverlayMode.Auto); set => SetValue(LightDismissOverlayModeProperty, value); }
    public object? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public event EventHandler<AutoSuggestBoxSuggestionChosenEventArgs>? SuggestionChosen;
    public event EventHandler<AutoSuggestBoxTextChangedEventArgs>? TextChanged;
    public event EventHandler<AutoSuggestBoxQuerySubmittedEventArgs>? QuerySubmitted;

    public void SetUserText(string? value) => SetText(value, AutoSuggestionBoxTextChangeReason.UserInput);

    public void ChooseSuggestion(object suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (UpdateTextOnSelect)
            SetText(GetSuggestionText(suggestion), AutoSuggestionBoxTextChangeReason.SuggestionChosen);
        SuggestionChosen?.Invoke(this, new AutoSuggestBoxSuggestionChosenEventArgs(suggestion));
    }

    public void SubmitQuery(object? chosenSuggestion = null) =>
        QuerySubmitted?.Invoke(this, new AutoSuggestBoxQuerySubmittedEventArgs(Text, chosenSuggestion));

    private void SetText(string? value, AutoSuggestionBoxTextChangeReason reason)
    {
        _pendingChangeReason = reason;
        SetValue(TextProperty, value ?? string.Empty);
    }

    private string GetSuggestionText(object suggestion)
    {
        if (string.IsNullOrEmpty(TextMemberPath))
            return suggestion.ToString() ?? string.Empty;

        if (suggestion is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(TextMemberPath, out var readOnlyValue))
            return readOnlyValue?.ToString() ?? string.Empty;

        if (suggestion is IDictionary<string, object?> mutable &&
            mutable.TryGetValue(TextMemberPath, out var mutableValue))
            return mutableValue?.ToString() ?? string.Empty;

        return suggestion.ToString() ?? string.Empty;
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (AutoSuggestBox)dependencyObject;
        control.TextChanged?.Invoke(control, new AutoSuggestBoxTextChangedEventArgs(control._pendingChangeReason));
        control._pendingChangeReason = AutoSuggestionBoxTextChangeReason.ProgrammaticChange;
    }

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(AutoSuggestBox),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsRender = true });
}
