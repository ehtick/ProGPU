using System.Numerics;

namespace Microsoft.UI.Xaml.Media;

public sealed class CompositeTransform : Transform
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public new double Rotation { get; set; }
    public double ScaleX { get; set; } = 1d;
    public double ScaleY { get; set; } = 1d;
    public double SkewX { get; set; }
    public double SkewY { get; set; }
    public double TranslateX { get; set; }
    public double TranslateY { get; set; }

    public override Matrix4x4 Value
    {
        get
        {
            var center = Matrix4x4.CreateTranslation((float)-CenterX, (float)-CenterY, 0f);
            var restore = Matrix4x4.CreateTranslation((float)(CenterX + TranslateX), (float)(CenterY + TranslateY), 0f);
            var scale = Matrix4x4.CreateScale((float)ScaleX, (float)ScaleY, 1f);
            var skew = Matrix4x4.Identity;
            skew.M12 = MathF.Tan((float)(SkewY * Math.PI / 180d));
            skew.M21 = MathF.Tan((float)(SkewX * Math.PI / 180d));
            var rotation = Matrix4x4.CreateRotationZ((float)(Rotation * Math.PI / 180d));
            return center * scale * skew * rotation * restore;
        }
    }
}
