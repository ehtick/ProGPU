using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

namespace PackageConsumer;

public partial class MainPage : Page
{
    public MainPage() => InitializeComponent();

    public string Label => "Packaged generator";

    public int ActionCount { get; private set; }

    public string? ResourceTextValue => ResourceText.Text;

    public string? ActionContentValue => ActionButton.Content?.ToString();

    public void UpdateResourceTitle(string value) =>
        ((ConsumerModel)Resources["BindingSource"]).Title = value;

    private void OnAction(object? sender, EventArgs args) => ActionCount++;
}

public sealed class ConsumerModel : INotifyPropertyChanged
{
    private string _title = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set
        {
            if (string.Equals(_title, value, StringComparison.Ordinal)) return;
            _title = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Title)));
        }
    }
}
