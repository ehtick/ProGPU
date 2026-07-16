namespace Microsoft.UI.Xaml.Input;

/// <summary>
/// Identifies custom containers that paint a background and should remain
/// pointer hit-test targets when none of their children is hit-testable.
/// </summary>
public interface IHitTestBackgroundProvider
{
    bool HasHitTestBackground { get; }
}
