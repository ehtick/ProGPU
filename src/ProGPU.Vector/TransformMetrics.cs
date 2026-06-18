using System;
using System.Numerics;

namespace ProGPU.Vector;

public static class TransformMetrics
{
    public static float GetStrokeScale(Matrix4x4 transform)
    {
        var a = transform.M11;
        var b = transform.M12;
        var c = transform.M21;
        var d = transform.M22;
        var sum = a * a + b * b + c * c + d * d;
        var determinant = (a * d) - (b * c);
        var discriminant = MathF.Max(0f, (sum * sum) - (4f * determinant * determinant));
        var scale = MathF.Sqrt(MathF.Max(0f, (sum + MathF.Sqrt(discriminant)) * 0.5f));

        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }
}
