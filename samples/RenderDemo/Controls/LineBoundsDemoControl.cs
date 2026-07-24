using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RenderDemo.Controls
{
    public class LineBoundsDemoControl : Control
    {
        static LineBoundsDemoControl()
        {
            AffectsRender<LineBoundsDemoControl>(AngleProperty);
        }

        public LineBoundsDemoControl()
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1 / 60.0);
            timer.Tick += (sender, e) => Angle += Math.PI / 360;
            timer.Start();
        }

        public static readonly StyledProperty<double> AngleProperty =
            AvaloniaProperty.Register<LineBoundsDemoControl, double>(nameof(Angle));        

        public double Angle
        {
            get => GetValue(AngleProperty);
            set => SetValue(AngleProperty, value);
        }

        public override void Render(DrawingContext drawingContext)
        {
            var lineLength = Math.Sqrt((100 * 100) + (100 * 100));

            var diffX = Math.Cos(Angle) * lineLength;
            var diffY = Math.Sin(Angle) * lineLength;


            var p1 = new Point(200, 200);
            var p2 = new Point(p1.X + diffX, p1.Y + diffY);

            var pen = new Pen(Brushes.Green, 20, lineCap: PenLineCap.Square);
            var boundPen = new Pen(Brushes.Black);

            drawingContext.DrawLine(pen, p1, p2);

            drawingContext.DrawRectangle(boundPen, CalculateStrokeBounds(p1, p2, pen));
        }

        // Computes the axis-aligned bounds of a stroked segment in fixed O(1) work.
        private static Rect CalculateStrokeBounds(Point start, Point end, Pen pen)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var half = pen.Thickness / 2;
            if (length <= double.Epsilon)
                return new Rect(start.X - half, start.Y - half, pen.Thickness, pen.Thickness);

            var ux = dx / length;
            var uy = dy / length;
            var cap = pen.LineCap == PenLineCap.Flat ? 0 : half;
            var sx = start.X - ux * cap;
            var sy = start.Y - uy * cap;
            var ex = end.X + ux * cap;
            var ey = end.Y + uy * cap;
            var px = -uy * half;
            var py = ux * half;

            var minX = Math.Min(Math.Min(sx + px, sx - px), Math.Min(ex + px, ex - px));
            var minY = Math.Min(Math.Min(sy + py, sy - py), Math.Min(ey + py, ey - py));
            var maxX = Math.Max(Math.Max(sx + px, sx - px), Math.Max(ex + px, ex - px));
            var maxY = Math.Max(Math.Max(sy + py, sy - py), Math.Max(ey + py, ey - py));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
