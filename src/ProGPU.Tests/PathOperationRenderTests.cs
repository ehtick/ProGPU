using System.Numerics;
using Microsoft.UI.Xaml;
using ProGPU.Scene;
using ProGPU.Tests.Headless;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class PathOperationRenderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferenceWithCoincidentEdgesRendersOneSidedBorder(bool borderOnLeft)
    {
        var window = HeadlessWindow.Shared;
        window.Resize(64, 40);
        window.Content = new OneSidedBorderVisual(borderOnLeft);

        try
        {
            window.Render();
            var pixels = window.ReadPixels();
            var center = ReadPixel(pixels, window.Width, x: 27, y: 20);
            var border = ReadPixel(pixels, window.Width, x: borderOnLeft ? 10 : 43, y: 20);

            AssertWhite(center);
            Assert.True(border.R <= 120 && border.G <= 120 && border.B <= 120,
                $"Expected the one-sided border to be dark, found {border}.");
        }
        finally
        {
            window.Content = null;
        }
    }

    private static RgbaPixel ReadPixel(byte[] pixels, uint width, int x, int y)
    {
        var index = ((y * (int)width) + x) * 4;
        return new RgbaPixel(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    private static void AssertWhite(RgbaPixel pixel)
    {
        Assert.True(pixel.R >= 245 && pixel.G >= 245 && pixel.B >= 245,
            $"Expected the excluded center to remain white, found {pixel}.");
        Assert.Equal(255, pixel.A);
    }

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class OneSidedBorderVisual : FrameworkElement
    {
        private readonly PathGeometry _border;

        public OneSidedBorderVisual(bool borderOnLeft)
        {
            Width = 64f;
            Height = 40f;
            _border = new PathGeometry
            {
                IsCombined = true,
                PathA = CreateRectangleWithDegenerateCornerArc(10f, 8f, 34f, 24f),
                PathB = CreateRectangleWithDegenerateCornerArc(borderOnLeft ? 11f : 10f, 8f, 33f, 24f),
                Op = 0
            };
        }

        private static PathGeometry CreateRectangleWithDegenerateCornerArc(float x, float y, float width, float height)
        {
            var path = new PathGeometry();
            var figure = new PathFigure(new Vector2(x, y), isClosed: true);
            figure.Segments.Add(new LineSegment(new Vector2(x + width, y)));
            figure.Segments.Add(new ArcSegment(
                new Vector2(x + width, y),
                Vector2.Zero,
                rotationAngle: 0f,
                isLargeArc: false,
                SweepDirection.Clockwise));
            figure.Segments.Add(new LineSegment(new Vector2(x + width, y + height)));
            figure.Segments.Add(new LineSegment(new Vector2(x, y + height)));
            figure.Segments.Add(new LineSegment(new Vector2(x, y)));
            path.Figures.Add(figure);
            return path;
        }

        public override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                new SolidColorBrush(new Vector4(1f, 1f, 1f, 1f)),
                pen: null,
                new Rect(0f, 0f, 64f, 40f));
            context.DrawPath(
                new SolidColorBrush(new Vector4(0f, 0f, 0f, 0.6f)),
                pen: null,
                _border);
        }
    }
}
