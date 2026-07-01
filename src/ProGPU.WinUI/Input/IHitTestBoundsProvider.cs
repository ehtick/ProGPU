using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Input;

public interface IHitTestBoundsProvider
{
    Rect GetHitTestBounds(Rect defaultBounds);
}
