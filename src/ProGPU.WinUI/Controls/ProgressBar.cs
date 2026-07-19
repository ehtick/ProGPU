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

public class ProgressBar : Control
{
    private readonly ThemeResourceBrush _defaultTrackBrush = new("ProgressBarBackground");
    private readonly ThemeResourceBrush _defaultForegroundBrush = new("ProgressBarForeground");
    private float _minimum = 0f;
    private float _maximum = 100f;
    private float _value = 0f;
    private bool _isIndeterminate;
    private float _indeterminateOffset;

    public float Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    public float Maximum
    {
        get => _maximum;
        set { _maximum = value; Invalidate(); }
    }

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (_isIndeterminate != value)
            {
                _isIndeterminate = value;
                if (value)
                {
                    _indeterminateOffset = 0f;
                }
                SetCustomFrameAnimationActive(value);
                Invalidate();
            }
        }
    }

    public ProgressBar()
    {
        Width = 200f;
        Height = 4f; // Clean, modern WinUI thin track

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
        // 1. Draw flat track background
        var rect = new Rect(Vector2.Zero, Size);
        context.FillRoundedRectangle(Background ?? _defaultTrackBrush, rect, rect.Height / 2f);

        // 2. Draw progress segment
        if (IsIndeterminate)
        {
            // Indeterminate sliding glow track animation segment
            float segmentWidth = Size.X * 0.3f; // 30% of track length
            float xStart = -segmentWidth + _indeterminateOffset;

            // Clip the sliding segment to the track's rounded bounds using basic layout intersects
            float renderX = Math.Max(0f, xStart);
            float renderW = Math.Min(Size.X, xStart + segmentWidth) - renderX;

            if (renderW > 0f)
            {
                context.FillRoundedRectangle(BorderBrush ?? _defaultForegroundBrush, new Rect(renderX, rect.Y, renderW, rect.Height), rect.Height / 2f);
            }
        }
        else
        {
            // Determinate filled segment
            float range = _maximum - _minimum;
            float percent = range > 0f ? (_value - _minimum) / range : 0f;
            float fillWidth = Size.X * percent;

            if (fillWidth > 0f)
            {
                context.FillRoundedRectangle(BorderBrush ?? _defaultForegroundBrush, new Rect(rect.X, rect.Y, fillWidth, rect.Height), rect.Height / 2f);
            }
        }

        base.OnRender(context);
    }

    protected override void OnUpdateAnimation(float elapsedSeconds)
    {
        if (!_isIndeterminate || !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0f)
        {
            return;
        }

        float width = Size.X > 0f && float.IsFinite(Size.X) ? Size.X : Width;
        if (!(width > 0f) || !float.IsFinite(width))
        {
            return;
        }

        // Preserve the former three logical pixels per 60-Hz frame while making motion
        // elapsed-time based and advancing it before retained-scene compilation.
        float segmentWidth = width * 0.3f;
        float cycle = width + segmentWidth;
        _indeterminateOffset = (_indeterminateOffset + MathF.Min(elapsedSeconds, 0.1f) * 180f) % cycle;
        Invalidate();
    }
}
