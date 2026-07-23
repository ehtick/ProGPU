using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;

namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>Arranges calendar containers in a stable seven-column grid.</summary>
public sealed class CalendarPanel : Panel
{
    private const int Columns = 7;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var cellAvailable = new Vector2(
            float.IsFinite(availableSize.X) ? availableSize.X / Columns : float.PositiveInfinity,
            float.IsFinite(availableSize.Y) ? availableSize.Y / Math.Max(1, (Children.Count + Columns - 1) / Columns) : float.PositiveInfinity);
        var maximum = Vector2.Zero;
        foreach (var child in Children)
        {
            if (child is not LayoutNode node) continue;
            node.Measure(cellAvailable);
            maximum = Vector2.Max(maximum, node.DesiredSize);
        }

        var rows = Math.Max(1, (Children.Count + Columns - 1) / Columns);
        return new Vector2(maximum.X * Columns, maximum.Y * rows);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        var rows = Math.Max(1, (Children.Count + Columns - 1) / Columns);
        var cellWidth = arrangeRect.Width / Columns;
        var cellHeight = arrangeRect.Height / rows;
        for (var index = 0; index < Children.Count; index++)
        {
            if (Children[index] is not LayoutNode node) continue;
            var column = index % Columns;
            var row = index / Columns;
            node.Arrange(new Rect(
                arrangeRect.X + column * cellWidth,
                arrangeRect.Y + row * cellHeight,
                cellWidth,
                cellHeight));
        }
    }
}
