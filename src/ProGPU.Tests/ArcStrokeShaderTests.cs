using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public class ArcStrokeShaderTests
{
    [Fact]
    public void StrokedPathArc_UsesGpuArcShapeType()
    {
        var window = HeadlessWindow.Shared;
        window.Resize(240, 160);
        window.Content = new ArcStrokeVisual();

        window.Render();

        Assert.Contains(window.Compositor.VectorVertices, vertex => DecodeShapeType(vertex.ShapeType) == 11);
        Assert.DoesNotContain(window.Compositor.VectorVertices, vertex => DecodeShapeType(vertex.ShapeType) == 3);

        window.Content = null;
    }

    private static int DecodeShapeType(float shapeType)
    {
        if (shapeType >= 1000f)
        {
            shapeType -= 1000f;
        }

        if (shapeType >= 195f)
        {
            shapeType -= 200f;
        }
        else if (shapeType >= 95f)
        {
            shapeType -= 100f;
        }

        return (int)MathF.Round(shapeType);
    }

    private sealed class ArcStrokeVisual : FrameworkElement
    {
        public ArcStrokeVisual()
        {
            Width = 240f;
            Height = 160f;
        }

        public override void OnRender(ProGPU.Scene.DrawingContext context)
        {
            var path = new PathGeometry();
            var figure = new PathFigure(new Vector2(30f, 100f));
            figure.Segments.Add(new ArcSegment(
                new Vector2(210f, 100f),
                new Vector2(110f, 70f),
                20f,
                isLargeArc: false,
                SweepDirection.Clockwise));
            path.Figures.Add(figure);

            context.DrawPath(
                brush: null,
                pen: new Pen(new SolidColorBrush(new Vector4(0.1f, 0.6f, 1f, 1f)), 8f),
                path);
        }
    }
}
