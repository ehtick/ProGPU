namespace Microsoft.UI.Xaml;

/// <summary>
/// Describes how a window entered or left the activated state.
/// Values match Microsoft.UI.Xaml.WindowActivationState.
/// </summary>
public enum WindowActivationState
{
    CodeActivated = 0,
    Deactivated = 1,
    PointerActivated = 2
}

public sealed class WindowActivatedEventArgs : EventArgs
{
    internal WindowActivatedEventArgs(WindowActivationState windowActivationState)
    {
        WindowActivationState = windowActivationState;
    }

    public bool Handled { get; set; }
    public WindowActivationState WindowActivationState { get; }
}

public sealed class WindowEventArgs : EventArgs
{
    public bool Handled { get; set; }
}

public sealed class WindowSizeChangedEventArgs : EventArgs
{
    internal WindowSizeChangedEventArgs(Windows.Foundation.Size size)
    {
        Size = size;
    }

    public bool Handled { get; set; }
    public Windows.Foundation.Size Size { get; }
}

public sealed class WindowVisibilityChangedEventArgs : EventArgs
{
    internal WindowVisibilityChangedEventArgs(bool visible)
    {
        Visible = visible;
    }

    public bool Handled { get; set; }
    public bool Visible { get; }
}
