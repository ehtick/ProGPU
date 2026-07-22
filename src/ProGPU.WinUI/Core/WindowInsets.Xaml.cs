namespace Microsoft.UI.Xaml;

/// <summary>
/// Host-neutral system inset state. WinUI has an InputPane occlusion API but no
/// cross-platform safe-area contract, so ProGPU adds this immutable companion.
/// </summary>
public readonly record struct WindowInsets(
    Thickness SafeArea,
    Windows.Foundation.Rect InputPaneOccludedRect,
    Windows.Foundation.Rect VisibleBounds)
{
    public static WindowInsets Empty => default;
}

public sealed class WindowInsetsChangedEventArgs : EventArgs
{
    internal WindowInsetsChangedEventArgs(WindowInsets insets)
    {
        Insets = insets;
    }

    public WindowInsets Insets { get; }
}
