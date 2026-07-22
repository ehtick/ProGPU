using Microsoft.UI.Xaml;

namespace Windows.UI.ViewManagement;

/// <summary>
/// Provides the official UWP/WinUI input-pane contract for ProGPU hosts.
/// Rectangles use logical client coordinates (DIPs).
/// </summary>
public sealed class InputPaneVisibilityEventArgs : EventArgs
{
    internal InputPaneVisibilityEventArgs(Windows.Foundation.Rect occludedRect)
    {
        OccludedRect = occludedRect;
    }

    public bool EnsuredFocusedElementInView { get; set; }
    public Windows.Foundation.Rect OccludedRect { get; }
}

public sealed class InputPane
{
    private readonly Window _window;
    private Func<bool>? _tryShow;
    private Func<bool>? _tryHide;

    internal InputPane(Window window)
    {
        _window = window;
    }

    public Windows.Foundation.Rect OccludedRect { get; private set; }
    public bool Visible => OccludedRect.Width > 0d && OccludedRect.Height > 0d;

    public event Windows.Foundation.TypedEventHandler<InputPane, InputPaneVisibilityEventArgs>? Showing;
    public event Windows.Foundation.TypedEventHandler<InputPane, InputPaneVisibilityEventArgs>? Hiding;

    public static InputPane GetForCurrentView()
    {
        IReadOnlyList<Window> windows = WindowManager.ActiveWindows;
        if (windows.Count == 0)
            throw new InvalidOperationException("No active Window is associated with the current view.");
        return windows[^1].InputPane;
    }

    /// <summary>ProGPU host-neutral overload for applications that own more than one window.</summary>
    public static InputPane GetForWindow(Window window) =>
        (window ?? throw new ArgumentNullException(nameof(window))).InputPane;

    public bool TryShow() => _tryShow?.Invoke() == true;
    public bool TryHide() => _tryHide?.Invoke() == true;

    internal void SetPlatformCallbacks(Func<bool>? tryShow, Func<bool>? tryHide)
    {
        _tryShow = tryShow;
        _tryHide = tryHide;
    }

    internal void UpdateOccludedRect(Windows.Foundation.Rect value, bool ensuredFocusedElementInView = false)
    {
        if (OccludedRect.Equals(value)) return;
        bool wasVisible = Visible;
        OccludedRect = value;
        bool isVisible = Visible;
        var args = new InputPaneVisibilityEventArgs(value)
        {
            EnsuredFocusedElementInView = ensuredFocusedElementInView
        };
        if (!wasVisible && isVisible) Showing?.Invoke(this, args);
        else if (wasVisible && !isVisible) Hiding?.Invoke(this, args);
        else if (isVisible) Showing?.Invoke(this, args);
    }
}
