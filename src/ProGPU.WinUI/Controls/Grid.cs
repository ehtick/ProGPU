using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace Microsoft.UI.Xaml.Controls;

public class Grid : Panel
{
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(Grid), new PropertyMetadata(null) { AffectsRender = true });
    public Brush? BorderBrush
    {
        get => GetValue(BorderBrushProperty) as Brush;
        set => SetValue(BorderBrushProperty, value);
    }

    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness), typeof(Thickness), typeof(Grid),
        new PropertyMetadata(default(Thickness)) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
    public Thickness BorderThickness
    {
        get => (Thickness)(GetValue(BorderThicknessProperty) ?? default(Thickness));
        set => SetValue(BorderThicknessProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(Grid),
        new PropertyMetadata(default(CornerRadius)) { AffectsRender = true });
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)(GetValue(CornerRadiusProperty) ?? default(CornerRadius));
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty BackgroundSizingProperty = DependencyProperty.Register(
        nameof(BackgroundSizing), typeof(BackgroundSizing), typeof(Grid),
        new PropertyMetadata(BackgroundSizing.InnerBorderEdge) { AffectsRender = true });
    public BackgroundSizing BackgroundSizing
    {
        get => (BackgroundSizing)(GetValue(BackgroundSizingProperty) ?? BackgroundSizing.InnerBorderEdge);
        set => SetValue(BackgroundSizingProperty, value);
    }
    private static readonly IReadOnlyList<ColumnDefinition> DefaultColumns =
        new[] { new ColumnDefinition { Width = GridLength.Star() } };
    private static readonly IReadOnlyList<RowDefinition> DefaultRows =
        new[] { new RowDefinition { Height = GridLength.Star() } };
    internal float[]? _actualColWidths;
    internal float[]? _actualRowHeights;

    private static readonly ConditionalWeakTable<Visual, GridCellInfo> _fallbackCellInfo = new();

    public class GridCellInfo
    {
        public int Row { get; set; }
        public int Column { get; set; }
    }

    public static readonly DependencyProperty RowProperty =
        DependencyProperty.RegisterAttached(
            "Row",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(0, OnRowColumnChanged));

    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.RegisterAttached(
            "Column",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(0, OnRowColumnChanged));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(Grid), new PropertyMetadata(1, OnRowColumnChanged));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(Grid), new PropertyMetadata(1, OnRowColumnChanged));

    private static void OnRowColumnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Visual child && child.Parent is Grid grid)
        {
            grid.Invalidate();
            grid.InvalidateMeasure();
        }
    }

    public ColumnDefinitionCollection ColumnDefinitions { get; } = new();
    public RowDefinitionCollection RowDefinitions { get; } = new();

    public static void SetRow(Visual child, int row)
    {
        if (child is DependencyObject d)
        {
            d.SetValue(RowProperty, row);
        }
        else
        {
            var info = _fallbackCellInfo.GetOrCreateValue(child);
            info.Row = row;
            if (child.Parent is Grid grid)
            {
                grid.Invalidate();
                grid.InvalidateMeasure();
            }
        }
    }

    public static void SetColumn(Visual child, int col)
    {
        if (child is DependencyObject d)
        {
            d.SetValue(ColumnProperty, col);
        }
        else
        {
            var info = _fallbackCellInfo.GetOrCreateValue(child);
            info.Column = col;
            if (child.Parent is Grid grid)
            {
                grid.Invalidate();
                grid.InvalidateMeasure();
            }
        }
    }

    public static int GetRow(Visual child)
    {
        if (child is DependencyObject d)
        {
            return (int)(d.GetValue(RowProperty) ?? 0);
        }
        if (_fallbackCellInfo.TryGetValue(child, out var info))
        {
            return info.Row;
        }
        return 0;
    }

    public static int GetColumn(Visual child)
    {
        if (child is DependencyObject d)
        {
            return (int)(d.GetValue(ColumnProperty) ?? 0);
        }
        if (_fallbackCellInfo.TryGetValue(child, out var info))
        {
            return info.Column;
        }
        return 0;
    }

    public static void SetRowSpan(Visual child, int value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        if (child is DependencyObject d) d.SetValue(RowSpanProperty, value);
    }

    public static int GetRowSpan(Visual child) => child is DependencyObject d
        ? (int)(d.GetValue(RowSpanProperty) ?? 1)
        : 1;

    public static void SetColumnSpan(Visual child, int value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
        if (child is DependencyObject d) d.SetValue(ColumnSpanProperty, value);
    }

    public static int GetColumnSpan(Visual child) => child is DependencyObject d
        ? (int)(d.GetValue(ColumnSpanProperty) ?? 1)
        : 1;

    public override void OnRender(DrawingContext context)
    {
        var pen = BorderBrush is { } brush && BorderThickness.Left > 0f
            ? new Pen(brush, BorderThickness.Left)
            : null;
        if (Background is not null || pen is not null)
        {
            context.DrawRoundedRectangle(Background, pen, new Rect(Vector2.Zero, Size), (float)CornerRadius.TopLeft);
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        int colCount = Math.Max(1, ColumnDefinitions.Count);
        int rowCount = Math.Max(1, RowDefinitions.Count);

        float[] colWidths = new float[colCount];
        float[] rowHeights = new float[rowCount];

        IReadOnlyList<ColumnDefinition> cols = ColumnDefinitions.Count > 0 ? ColumnDefinitions : DefaultColumns;
        IReadOnlyList<RowDefinition> rows = RowDefinitions.Count > 0 ? RowDefinitions : DefaultRows;

        float remainingWidth = availableSize.X;
        float remainingHeight = availableSize.Y;

        if (!float.IsInfinity(remainingWidth))
        {
            remainingWidth = Math.Max(0f, remainingWidth - Padding.Horizontal);
        }
        if (!float.IsInfinity(remainingHeight))
        {
            remainingHeight = Math.Max(0f, remainingHeight - Padding.Vertical);
        }

        float starColsWeight = 0f;
        float starRowsWeight = 0f;

        for (int i = 0; i < colCount; i++)
        {
            if (cols[i].UnitType == GridUnitType.Absolute)
            {
                colWidths[i] = cols[i].Value;
                remainingWidth -= colWidths[i];
            }
            else if (cols[i].UnitType == GridUnitType.Star)
            {
                starColsWeight += cols[i].Value;
            }
        }

        for (int i = 0; i < rowCount; i++)
        {
            if (rows[i].UnitType == GridUnitType.Absolute)
            {
                rowHeights[i] = rows[i].Value;
                remainingHeight -= rowHeights[i];
            }
            else if (rows[i].UnitType == GridUnitType.Star)
            {
                starRowsWeight += rows[i].Value;
            }
        }

        remainingWidth = Math.Max(0f, remainingWidth);
        remainingHeight = Math.Max(0f, remainingHeight);

        // Measure children belonging to Auto or Star cells
        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                int r = GetRow(node);
                int c = GetColumn(node);
                r = Math.Clamp(r, 0, rowCount - 1);
                c = Math.Clamp(c, 0, colCount - 1);

                float childAvailW = cols[c].UnitType switch
                {
                    GridUnitType.Absolute => colWidths[c],
                    GridUnitType.Star => starColsWeight > 0 && !float.IsInfinity(remainingWidth) ? (cols[c].Value / starColsWeight) * remainingWidth : float.PositiveInfinity,
                    _ => float.PositiveInfinity
                };

                float childAvailH = rows[r].UnitType switch
                {
                    GridUnitType.Absolute => rowHeights[r],
                    GridUnitType.Star => starRowsWeight > 0 && !float.IsInfinity(remainingHeight) ? (rows[r].Value / starRowsWeight) * remainingHeight : float.PositiveInfinity,
                    _ => float.PositiveInfinity
                };

                node.Measure(new Vector2(childAvailW, childAvailH));
                var desired = node.DesiredSize;

                if (cols[c].UnitType == GridUnitType.Auto)
                {
                    colWidths[c] = Math.Max(colWidths[c], desired.X);
                }
                if (rows[r].UnitType == GridUnitType.Auto)
                {
                    rowHeights[r] = Math.Max(rowHeights[r], desired.Y);
                }
            }
        }

        // Subtract measured auto cells
        for (int i = 0; i < colCount; i++)
        {
            if (cols[i].UnitType == GridUnitType.Auto)
            {
                remainingWidth = Math.Max(0f, remainingWidth - colWidths[i]);
            }
        }
        for (int i = 0; i < rowCount; i++)
        {
            if (rows[i].UnitType == GridUnitType.Auto)
            {
                remainingHeight = Math.Max(0f, remainingHeight - rowHeights[i]);
            }
        }

        // Distribute Star cells
        if (starColsWeight > 0f)
        {
            bool isInfinite = float.IsInfinity(remainingWidth);
            for (int i = 0; i < colCount; i++)
            {
                if (cols[i].UnitType == GridUnitType.Star)
                {
                    if (isInfinite)
                    {
                        float maxChildW = 0f;
                        foreach (var child in Children)
                        {
                            if (child is LayoutNode node && GetColumn(node) == i)
                            {
                                maxChildW = Math.Max(maxChildW, node.DesiredSize.X);
                            }
                        }
                        colWidths[i] = maxChildW;
                    }
                    else
                    {
                        colWidths[i] = (cols[i].Value / starColsWeight) * remainingWidth;
                    }
                }
            }
        }

        if (starRowsWeight > 0f)
        {
            bool isInfinite = float.IsInfinity(remainingHeight);
            for (int i = 0; i < rowCount; i++)
            {
                if (rows[i].UnitType == GridUnitType.Star)
                {
                    if (isInfinite)
                    {
                        float maxChildH = 0f;
                        foreach (var child in Children)
                        {
                            if (child is LayoutNode node && GetRow(node) == i)
                            {
                                maxChildH = Math.Max(maxChildH, node.DesiredSize.Y);
                            }
                        }
                        rowHeights[i] = maxChildH;
                    }
                    else
                    {
                        rowHeights[i] = (rows[i].Value / starRowsWeight) * remainingHeight;
                    }
                }
            }
        }

        float gridW = 0f;
        foreach (float w in colWidths) gridW += w;
        float gridH = 0f;
        foreach (float h in rowHeights) gridH += h;

        return new Vector2(gridW, gridH);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        int colCount = Math.Max(1, ColumnDefinitions.Count);
        int rowCount = Math.Max(1, RowDefinitions.Count);

        IReadOnlyList<ColumnDefinition> cols = ColumnDefinitions.Count > 0 ? ColumnDefinitions : DefaultColumns;
        IReadOnlyList<RowDefinition> rows = RowDefinitions.Count > 0 ? RowDefinitions : DefaultRows;

        float[] colWidths = new float[colCount];
        float[] rowHeights = new float[rowCount];

        float totalW = arrangeRect.Width;
        float totalH = arrangeRect.Height;

        if (float.IsInfinity(totalW) || float.IsNaN(totalW))
        {
            totalW = Math.Max(0f, DesiredSize.X - Margin.Horizontal - Padding.Horizontal);
        }
        if (float.IsInfinity(totalH) || float.IsNaN(totalH))
        {
            totalH = Math.Max(0f, DesiredSize.Y - Margin.Vertical - Padding.Vertical);
        }

        float starColsWeight = 0f;
        float starRowsWeight = 0f;

        for (int i = 0; i < colCount; i++)
        {
            if (cols[i].UnitType == GridUnitType.Absolute)
            {
                colWidths[i] = cols[i].Value;
                totalW -= colWidths[i];
            }
            else if (cols[i].UnitType == GridUnitType.Star)
            {
                starColsWeight += cols[i].Value;
            }
        }

        for (int i = 0; i < rowCount; i++)
        {
            if (rows[i].UnitType == GridUnitType.Absolute)
            {
                rowHeights[i] = rows[i].Value;
                totalH -= rowHeights[i];
            }
            else if (rows[i].UnitType == GridUnitType.Star)
            {
                starRowsWeight += rows[i].Value;
            }
        }

        totalW = Math.Max(0f, totalW);
        totalH = Math.Max(0f, totalH);

        // Auto sizing during arrange uses measured dimensions
        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                int r = GetRow(node);
                int c = GetColumn(node);
                r = Math.Clamp(r, 0, rowCount - 1);
                c = Math.Clamp(c, 0, colCount - 1);

                if (cols[c].UnitType == GridUnitType.Auto)
                {
                    colWidths[c] = Math.Max(colWidths[c], node.DesiredSize.X);
                }
                if (rows[r].UnitType == GridUnitType.Auto)
                {
                    rowHeights[r] = Math.Max(rowHeights[r], node.DesiredSize.Y);
                }
            }
        }

        for (int i = 0; i < colCount; i++)
        {
            if (cols[i].UnitType == GridUnitType.Auto)
            {
                totalW = Math.Max(0f, totalW - colWidths[i]);
            }
        }
        for (int i = 0; i < rowCount; i++)
        {
            if (rows[i].UnitType == GridUnitType.Auto)
            {
                totalH = Math.Max(0f, totalH - rowHeights[i]);
            }
        }

        if (starColsWeight > 0f)
        {
            for (int i = 0; i < colCount; i++)
            {
                if (cols[i].UnitType == GridUnitType.Star)
                {
                    colWidths[i] = (cols[i].Value / starColsWeight) * totalW;
                }
            }
        }

        if (starRowsWeight > 0f)
        {
            for (int i = 0; i < rowCount; i++)
            {
                if (rows[i].UnitType == GridUnitType.Star)
                {
                    rowHeights[i] = (rows[i].Value / starRowsWeight) * totalH;
                }
            }
        }

        float[] colOffsets = new float[colCount];
        float curX = arrangeRect.X;
        for (int i = 0; i < colCount; i++)
        {
            colOffsets[i] = curX;
            curX += colWidths[i];
        }

        float[] rowOffsets = new float[rowCount];
        float curY = arrangeRect.Y;
        for (int i = 0; i < rowCount; i++)
        {
            rowOffsets[i] = curY;
            curY += rowHeights[i];
        }

        foreach (var child in Children)
        {
            if (child is LayoutNode node)
            {
                int r = GetRow(node);
                int c = GetColumn(node);
                r = Math.Clamp(r, 0, rowCount - 1);
                c = Math.Clamp(c, 0, colCount - 1);

                node.Arrange(new Rect(colOffsets[c], rowOffsets[r], colWidths[c], rowHeights[r]));
            }
        }

        _actualColWidths = colWidths;
        _actualRowHeights = rowHeights;
    }
}
