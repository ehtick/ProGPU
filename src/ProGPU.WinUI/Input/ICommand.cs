using System;

namespace Microsoft.UI.Xaml.Input;

public interface ICommand
{
    event EventHandler? CanExecuteChanged;
    bool CanExecute(object? parameter);
    void Execute(object? parameter);
}
