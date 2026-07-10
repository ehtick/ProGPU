using System.Numerics;
using ProGPU.Backend;
using ProGPU.Vector;

namespace ProGPU.Scene;

public static class DrawingContextBackdropExtensions
{
    public static void DrawBackdropMaterial(
        this DrawingContext context,
        BackdropMaterialBrush material,
        Rect rect,
        float radius = 0f,
        Matrix4x4 transform = default,
        GpuTexture? sourceTexture = null,
        Rect sourceRect = default)
    {
        DrawBackdropMaterial(
            context,
            material,
            rect,
            new Vector4(radius),
            new Vector4(radius),
            transform,
            sourceTexture,
            sourceRect);
    }

    public static void DrawBackdropMaterial(
        this DrawingContext context,
        BackdropMaterialBrush material,
        Rect rect,
        Vector4 cornerRadiiX,
        Vector4 cornerRadiiY,
        Matrix4x4 transform = default,
        GpuTexture? sourceTexture = null,
        Rect sourceRect = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(material);

        context.DrawBackdropMaterial(BackdropMaterialParams.FromBrush(
            material,
            rect,
            cornerRadiiX,
            cornerRadiiY,
            sourceTexture,
            sourceRect), transform);
    }

    public static void DrawBackdropMaterial(
        this DrawingContext context,
        BackdropMaterialParams parameters,
        Matrix4x4 transform = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);

        context.DrawExtension(
            CompositorBuiltInExtensions.BackdropMaterial,
            dataParam: parameters,
            transform: transform);
    }
}
