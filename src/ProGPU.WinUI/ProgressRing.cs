using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls;

public class ProgressRing : Control
{
    private bool _isActive = true;
    private float _rotationOffset;
    private SolidColorBrush[]? _dotBrushes;

    public override void OnVisualStateChanged()
    {
        _dotBrushes = null; // invalidate cached brushes
        base.OnVisualStateChanged();
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                Invalidate();
            }
        }
    }

    public ProgressRing()
    {
        Width = 32f;
        Height = 32f;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            Style = defaultStyle;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(Width, Height);
    }
    public override void OnRender(DrawingContext context)
    {
        if (IsActive)
        {
            float cx = Size.X / 2f;
            float cy = Size.Y / 2f;
            float radius = (Size.X - 8f) / 2f; // Keep dot bounds inside control nicely
            float dotRadius = 3f;

            if (_dotBrushes == null)
            {
                var brush = Foreground ?? ThemeManager.GetBrush("ProgressRingForeground");
                Vector4 colorVec = new Vector4(0.0f, 0.47f, 0.83f, 1.0f); // Default Accent Blue
                if (brush is SolidColorBrush scb)
                {
                    colorVec = scb.Color;
                }

                _dotBrushes = new SolidColorBrush[8];
                for (int i = 0; i < 8; i++)
                {
                    float opacityFraction = (i / 8f);
                    _dotBrushes[i] = new SolidColorBrush(new Vector4(colorVec.X, colorVec.Y, colorVec.Z, opacityFraction));
                }
            }

            // Draw 8 circular dots in a loop with a tail fading opacity sweep
            for (int i = 0; i < 8; i++)
            {
                // Angle with dynamic rotation sweep offset
                float angle = (float)(i * (2.0 * Math.PI / 8.0) + _rotationOffset);
                float x = cx + radius * (float)Math.Cos(angle);
                float y = cy + radius * (float)Math.Sin(angle);

                var dotColor = _dotBrushes[i];

                context.FillCircle(dotColor, new Vector2(x, y), dotRadius);
            }

            // Animate spin speed smoothly at 60 FPS
            _rotationOffset = (float)((_rotationOffset + 0.08f) % (2.0 * Math.PI));

            // Self-invalidate to trigger continuous render presents on the WebGPU surface
            Invalidate();
        }

        base.OnRender(context);
    }
}
