using System.Numerics;
using ProGPU.Scene;
using Xunit;
using WpfFillRule = System.Windows.Media.FillRule;
using VectorPen = ProGPU.Vector.Pen;
using VectorSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using WpfLineSegment = System.Windows.Media.LineSegment;
using WpfMatrix = System.Windows.Media.Matrix;
using WpfMatrixTransform = System.Windows.Media.MatrixTransform;
using WpfPathFigure = System.Windows.Media.PathFigure;
using WpfPathGeometry = System.Windows.Media.PathGeometry;
using WpfPoint = System.Windows.Point;
using WpfStreamGeometry = System.Windows.Media.StreamGeometry;

namespace ProGPU.Tests;

public sealed class WpfPathGeometryDrawTests
{
    [Fact]
    public void DrawFillSkipsUnfilledFigures()
    {
        var geometry = new WpfPathGeometry();
        geometry.Figures.Add(CreateTriangle(new Vector2(0f, 0f), isFilled: true));
        geometry.Figures.Add(CreateTriangle(new Vector2(100f, 0f), isFilled: false));
        var context = new DrawingContext();

        geometry.Draw(
            context,
            fill: new VectorSolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
            pen: null);

        var command = Assert.Single(context.Commands);
        Assert.NotNull(command.Brush);
        Assert.Null(command.Pen);

        var figure = Assert.Single(command.Path!.Figures);
        Assert.Equal(new Vector2(0f, 0f), figure.StartPoint);
        Assert.True(figure.IsFilled);
    }

    [Fact]
    public void DrawFillAndStrokeSplitsUnfilledFigures()
    {
        var geometry = new WpfPathGeometry();
        geometry.Figures.Add(CreateTriangle(new Vector2(0f, 0f), isFilled: true));
        geometry.Figures.Add(CreateTriangle(new Vector2(100f, 0f), isFilled: false));
        var context = new DrawingContext();
        var brush = new VectorSolidColorBrush(new Vector4(0f, 0f, 1f, 1f));

        geometry.Draw(
            context,
            fill: brush,
            pen: new VectorPen(brush, 2f));

        Assert.Equal(2, context.Commands.Count);

        var fillCommand = context.Commands[0];
        Assert.NotNull(fillCommand.Brush);
        Assert.Null(fillCommand.Pen);
        Assert.Single(fillCommand.Path!.Figures);

        var strokeCommand = context.Commands[1];
        Assert.Null(strokeCommand.Brush);
        Assert.NotNull(strokeCommand.Pen);
        Assert.Equal(2, strokeCommand.Path!.Figures.Count);
    }

    [Fact]
    public void DrawFillCarriesWpfEvenOddFillRule()
    {
        var geometry = new WpfPathGeometry { FillRule = WpfFillRule.EvenOdd };
        geometry.Figures.Add(CreateTriangle(new Vector2(0f, 0f), isFilled: true));
        var context = new DrawingContext();

        geometry.Draw(
            context,
            fill: new VectorSolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
            pen: null);

        var command = Assert.Single(context.Commands);
        Assert.Equal(ProGPU.Vector.FillRule.EvenOdd, command.Path!.FillRule);
    }

    [Fact]
    public void DrawFillCarriesWpfNonzeroFillRule()
    {
        var geometry = new WpfPathGeometry { FillRule = WpfFillRule.Nonzero };
        geometry.Figures.Add(CreateTriangle(new Vector2(0f, 0f), isFilled: true));
        var context = new DrawingContext();

        geometry.Draw(
            context,
            fill: new VectorSolidColorBrush(new Vector4(1f, 0f, 0f, 1f)),
            pen: null);

        var command = Assert.Single(context.Commands);
        Assert.Equal(ProGPU.Vector.FillRule.Nonzero, command.Path!.FillRule);
    }

    [Fact]
    public void StreamGeometryPreservesSegmentIsStroked()
    {
        var geometry = new WpfStreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new WpfPoint(0, 0), isFilled: false, isClosed: false);
            context.LineTo(new WpfPoint(10, 0), isStroked: false, isSmoothJoin: false);
        }

        var drawingContext = new DrawingContext();
        var brush = new VectorSolidColorBrush(new Vector4(1f, 0f, 0f, 1f));
        geometry.Draw(drawingContext, fill: null, pen: new VectorPen(brush, 1f));

        var command = Assert.Single(drawingContext.Commands);
        var figure = Assert.Single(command.Path!.Figures);
        var segment = Assert.IsType<ProGPU.Vector.LineSegment>(Assert.Single(figure.Segments));
        Assert.False(segment.IsStroked);
    }

    [Fact]
    public void StreamGeometryBoundsApplyOwnTransformBeforeDraw()
    {
        var transform = new WpfMatrix
        {
            M11 = 1,
            M22 = 1,
            OffsetX = 3,
            OffsetY = 4
        };
        var geometry = new WpfStreamGeometry
        {
            Transform = new WpfMatrixTransform(transform)
        };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new WpfPoint(0, 0), isFilled: true, isClosed: false);
            context.LineTo(new WpfPoint(10, 5), isStroked: true, isSmoothJoin: false);
        }

        var bounds = geometry.Bounds;

        Assert.Equal(3, bounds.X, 3);
        Assert.Equal(4, bounds.Y, 3);
        Assert.Equal(10, bounds.Width, 3);
        Assert.Equal(5, bounds.Height, 3);
    }

    private static WpfPathFigure CreateTriangle(Vector2 origin, bool isFilled)
    {
        var figure = new WpfPathFigure
        {
            StartPoint = ToPoint(origin),
            IsClosed = true,
            IsFilled = isFilled
        };
        figure.Segments.Add(new WpfLineSegment(origin + new Vector2(10f, 0f)));
        figure.Segments.Add(new WpfLineSegment(origin + new Vector2(0f, 10f)));
        return figure;
    }

    private static WpfPoint ToPoint(Vector2 point)
    {
        return new WpfPoint(point.X, point.Y);
    }
}
