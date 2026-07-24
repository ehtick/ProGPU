namespace Microsoft.UI.Xaml.Media;

public enum FastPlayFallbackBehaviour
{
    Skip = 0,
    Hide = 1,
    Disable = 2
}

/// <summary>
/// Base contract for image sources accepted by media controls.
/// </summary>
public abstract class ImageSource : DependencyObject
{
}
