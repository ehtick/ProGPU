using Microsoft.UI.Xaml.Input;

namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>Provides command and activation behavior shared by WinUI button controls.</summary>
public class ButtonBase : ContentControl
{
    public static readonly DependencyProperty ClickModeProperty = DependencyProperty.Register(
        nameof(ClickMode),
        typeof(ClickMode),
        typeof(ButtonBase),
        new PropertyMetadata(ClickMode.Release));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(ButtonBase),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(ButtonBase),
        new PropertyMetadata(null));

    public ClickMode ClickMode
    {
        get => (ClickMode)(GetValue(ClickModeProperty) ?? ClickMode.Release);
        set => SetValue(ClickModeProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty) as ICommand;
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsPressed => IsPointerPressed;

    public event RoutedEventHandler? Click;

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsEnabled && ClickMode == ClickMode.Press) OnClick();
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (IsEnabled && ClickMode == ClickMode.Release && IsPointerPressed && IsPointerOver)
            OnClick();
        base.OnPointerReleased(e);
    }

    public override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        if (IsEnabled && ClickMode == ClickMode.Hover) OnClick();
    }

    public override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsEnabled && (e.Key == Silk.NET.Input.Key.Space || e.Key == Silk.NET.Input.Key.Enter))
        {
            OnClick();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected virtual void OnClick() => RaiseClick();

    protected void RaiseClick()
    {
        var command = Command;
        if (command?.CanExecute(CommandParameter) == true)
            command.Execute(CommandParameter);
        Click?.Invoke(this, new RoutedEventArgs());
    }
}
