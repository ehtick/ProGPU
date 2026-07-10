using System.Numerics;

namespace ProGPU.Vector;

public enum BackdropMaterialKind
{
    Acrylic = 0,
    Mica = 1,
    Blur = 2,
    Tint = 3,
    Fallback = 4
}

public enum BackdropMaterialSource
{
    None = 0,
    Texture = 1,
    HostBackdrop = 2
}

public class BackdropMaterialBrush : Brush
{
    public BackdropMaterialKind Kind { get; set; } = BackdropMaterialKind.Acrylic;
    public BackdropMaterialSource Source { get; set; } = BackdropMaterialSource.HostBackdrop;
    public Vector4 TintColor { get; set; } = Vector4.One;
    public Vector4 LuminosityColor { get; set; } = new(0.96f, 0.96f, 0.96f, 0.72f);
    public Vector4 FallbackColor { get; set; } = new(0.96f, 0.96f, 0.96f, 1f);
    public Vector4 NoiseColor { get; set; } = Vector4.One;
    public float TintOpacity { get; set; } = 1f;
    public float LuminosityOpacity { get; set; } = 1f;
    public float MaterialOpacity { get; set; } = 1f;
    public float NoiseOpacity { get; set; } = 0.0225f;
    public float BlurRadius { get; set; } = 30f;
    public float Saturation { get; set; } = 1.25f;
    public bool UseFallback { get; set; }

    public static BackdropMaterialBrush CreateMica(bool darkTheme = false)
    {
        return darkTheme
            ? new BackdropMaterialBrush
            {
                Kind = BackdropMaterialKind.Mica,
                TintColor = new Vector4(0.08f, 0.08f, 0.08f, 0.62f),
                LuminosityColor = new Vector4(0.13f, 0.13f, 0.13f, 0.82f),
                FallbackColor = new Vector4(0.13f, 0.13f, 0.13f, 1f),
                NoiseOpacity = 0.015f,
                BlurRadius = 48f,
                Saturation = 0.85f
            }
            : new BackdropMaterialBrush
            {
                Kind = BackdropMaterialKind.Mica,
                TintColor = new Vector4(0.96f, 0.96f, 0.96f, 0.55f),
                LuminosityColor = new Vector4(0.94f, 0.94f, 0.94f, 0.76f),
                FallbackColor = new Vector4(0.94f, 0.94f, 0.94f, 1f),
                NoiseOpacity = 0.015f,
                BlurRadius = 48f,
                Saturation = 0.85f
            };
    }
}
