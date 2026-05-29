using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using Silk.NET.Input;

namespace Microsoft.UI.Xaml.Input;

[Flags]
public enum VirtualKeyModifiers
{
    None = 0,
    Control = 1,
    Menu = 2,
    Shift = 4,
    Windows = 8
}

public class KeyboardAcceleratorInvokedEventArgs : EventArgs
{
    public bool Handled { get; set; }
    public FrameworkElement Element { get; }

    public KeyboardAcceleratorInvokedEventArgs(FrameworkElement element)
    {
        Element = element;
    }
}

public class KeyboardAccelerator
{
    public Key Key { get; set; }
    public VirtualKeyModifiers Modifiers { get; set; }
    public bool IsEnabled { get; set; } = true;

    public event EventHandler<KeyboardAcceleratorInvokedEventArgs>? Invoked;

    public bool Invoke(FrameworkElement element)
    {
        var args = new KeyboardAcceleratorInvokedEventArgs(element);
        Invoked?.Invoke(this, args);
        return args.Handled;
    }
}
