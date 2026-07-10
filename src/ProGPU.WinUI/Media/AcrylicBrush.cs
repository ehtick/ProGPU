using System.Numerics;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Media;

public enum AcrylicBackgroundSource
{
    HostBackdrop = 0,
    Texture = 1,
    None = 2
}

public class AcrylicBrush : BackdropMaterialBrush
{
    public AcrylicBrush()
    {
        Kind = BackdropMaterialKind.Acrylic;
        Source = BackdropMaterialSource.HostBackdrop;
    }

    public AcrylicBackgroundSource BackgroundSource
    {
        get => Source switch
        {
            BackdropMaterialSource.Texture => AcrylicBackgroundSource.Texture,
            BackdropMaterialSource.None => AcrylicBackgroundSource.None,
            _ => AcrylicBackgroundSource.HostBackdrop
        };
        set => Source = value switch
        {
            AcrylicBackgroundSource.Texture => BackdropMaterialSource.Texture,
            AcrylicBackgroundSource.None => BackdropMaterialSource.None,
            _ => BackdropMaterialSource.HostBackdrop
        };
    }
}

public sealed class BackdropBlurBrush : BackdropMaterialBrush
{
    public BackdropBlurBrush()
    {
        Kind = BackdropMaterialKind.Blur;
        Source = BackdropMaterialSource.Texture;
        TintColor = Vector4.Zero;
        LuminosityColor = Vector4.Zero;
        NoiseOpacity = 0f;
        Saturation = 1f;
    }
}
