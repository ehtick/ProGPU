using Thickness = Microsoft.UI.Xaml.Thickness;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using ProGPU.Scene;
using ProGPU.Vector;
using ProGPU.Text;

namespace ProGPU.Designer;

public class DragEventArgs : RoutedEventArgs
{
    public DataPackage Data { get; }
    public Vector2 Position { get; }

    public DragEventArgs(DataPackage data, Vector2 position)
    {
        Data = data;
        Position = position;
    }
}

public class DataPackage
{
    private readonly Dictionary<string, object> _properties = new();

    public void SetText(string text) => _properties["Text"] = text;
    public string GetText() => _properties.TryGetValue("Text", out var val) ? (string)val : string.Empty;

    public void SetData(string formatId, object value) => _properties[formatId] = value;
    public object GetData(string formatId) => _properties.TryGetValue(formatId, out var val) ? val : null;
    public bool Contains(string formatId) => _properties.ContainsKey(formatId);
}

public static class StandardDataFormats
{
    public const string Tool = "Tool";
}

public class DesignerCanvas : Panel
{
    public Brush? Background { get; set; }
    public Canvas DesignSurface { get; }
    public Canvas AdornerSurface { get; }

    public FrameworkElement? SelectedElement { get; private set; }
    private SelectionAdorner? _selectionAdorner;

    public bool AllowDrop { get; set; } = true;

    public event EventHandler<DragEventArgs>? DragOver;
    public event EventHandler<DragEventArgs>? Drop;

    public float? ActiveVerticalSnapX { get; set; }
    public float? ActiveHorizontalSnapY { get; set; }

    // Pointer movement state
    private bool _isDraggingElement;
    private Vector2 _dragStartOffset;
    private float _elementStartLeft;
    private float _elementStartTop;

    public DesignerCanvas()
    {
        DesignSurface = new Canvas();
        AdornerSurface = new Canvas();

        Children.Add(DesignSurface);
        Children.Add(AdornerSurface);

        // Bind background dynamically using ThemeResourceBrush to comply with guidelines
        Background = new ThemeResourceBrush("PageBackground");
    }

    public void SelectElement(FrameworkElement? element)
    {
        if (SelectedElement == element) return;

        if (_selectionAdorner != null)
        {
            AdornerSurface.Children.Remove(_selectionAdorner);
            _selectionAdorner = null;
        }

        SelectedElement = element;

        if (SelectedElement != null)
        {
            _selectionAdorner = new SelectionAdorner(SelectedElement, this);
            AdornerSurface.Children.Add(_selectionAdorner);
            _selectionAdorner.UpdatePositionAndSize();
        }

        InvalidateArrange();
        Invalidate();
    }

    public void UpdateSelectionAdorner()
    {
        _selectionAdorner?.UpdatePositionAndSize();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        DesignSurface.Measure(availableSize);
        AdornerSurface.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        DesignSurface.Arrange(arrangeRect);
        AdornerSurface.Arrange(arrangeRect);
        
        _selectionAdorner?.UpdatePositionAndSize();
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        var clicked = e.OriginalSource as FrameworkElement;
        FrameworkElement? childOfDesignSurface = null;
        while (clicked != null && clicked != DesignSurface)
        {
            if (clicked.Parent == DesignSurface)
            {
                childOfDesignSurface = clicked;
                break;
            }
            clicked = clicked.Parent as FrameworkElement;
        }

        if (childOfDesignSurface != null)
        {
            SelectElement(childOfDesignSurface);
            
            // Start dragging
            _isDraggingElement = true;
            _dragStartOffset = e.Position;
            _elementStartLeft = Canvas.GetLeft(childOfDesignSurface);
            _elementStartTop = Canvas.GetTop(childOfDesignSurface);
            InputSystem.CapturePointer(this);
            e.Handled = true;
        }
        else if (e.OriginalSource == this || e.OriginalSource == DesignSurface)
        {
            SelectElement(null);
        }
        
        base.OnPointerPressed(e);
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (_isDraggingElement && SelectedElement != null)
        {
            Vector2 delta = e.Position - _dragStartOffset;
            float candidateLeft = _elementStartLeft + delta.X;
            float candidateTop = _elementStartTop + delta.Y;

            // Snaps coordinates if close (within 8 pixels)
            Vector2 snapped = SnapPosition(SelectedElement, new Vector2(candidateLeft, candidateTop));

            Canvas.SetLeft(SelectedElement, snapped.X);
            Canvas.SetTop(SelectedElement, snapped.Y);

            _selectionAdorner?.UpdatePositionAndSize();
            
            SelectedElement.InvalidateMeasure();
            SelectedElement.InvalidateArrange();
            SelectedElement.Invalidate();

            InvalidateArrange();
            Invalidate();
            e.Handled = true;
        }
        
        base.OnPointerMoved(e);
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_isDraggingElement)
        {
            InputSystem.ReleasePointerCapture();
            _isDraggingElement = false;
            
            // Clear guidelines
            ActiveVerticalSnapX = null;
            ActiveHorizontalSnapY = null;
            
            Invalidate();
            e.Handled = true;
        }
        
        base.OnPointerReleased(e);
    }

    public float SnapToGrid(float val, float gridSpacing = 10f)
    {
        return MathF.Round(val / gridSpacing) * gridSpacing;
    }

    public Vector2 SnapPositionToGrid(Vector2 pos, float gridSpacing = 10f)
    {
        return new Vector2(SnapToGrid(pos.X, gridSpacing), SnapToGrid(pos.Y, gridSpacing));
    }

    public Rect GetElementRect(FrameworkElement element)
    {
        float left = Canvas.GetLeft(element);
        float top = Canvas.GetTop(element);
        float width = float.IsNaN(element.Width) ? element.Size.X : element.Width;
        float height = float.IsNaN(element.Height) ? element.Size.Y : element.Height;
        
        if (width <= 0) width = 120f;
        if (height <= 0) height = 36f;
        
        return new Rect(left, top, width, height);
    }

    public Vector2 SnapPosition(FrameworkElement element, Vector2 newPos)
    {
        Rect rect = GetElementRect(element);
        float w = rect.Width;
        float h = rect.Height;

        float x = newPos.X;
        float y = newPos.Y;

        // Grid snap default
        float gridSnappedX = SnapToGrid(x, 10f);
        float gridSnappedY = SnapToGrid(y, 10f);

        float snapThreshold = 8f;
        float? snapX = null;
        float? snapY = null;
        
        float snappedLeft = gridSnappedX;
        float snappedTop = gridSnappedY;

        foreach (var child in DesignSurface.Children)
        {
            if (child == element || child is not FrameworkElement other)
                continue;

            Rect otherRect = GetElementRect(other);
            float otherLeft = otherRect.X;
            float otherTop = otherRect.Y;
            float otherRight = otherRect.X + otherRect.Width;
            float otherBottom = otherRect.Y + otherRect.Height;
            float otherCenterX = otherRect.X + otherRect.Width / 2f;
            float otherCenterY = otherRect.Y + otherRect.Height / 2f;

            // Check vertical alignments
            float[] myXs = { x, x + w, x + w / 2f };
            float[] otherXs = { otherLeft, otherRight, otherCenterX };

            for (int i = 0; i < myXs.Length; i++)
            {
                for (int j = 0; j < otherXs.Length; j++)
                {
                    if (Math.Abs(myXs[i] - otherXs[j]) <= snapThreshold)
                    {
                        snapX = otherXs[j];
                        if (i == 0) snappedLeft = otherXs[j];
                        else if (i == 1) snappedLeft = otherXs[j] - w;
                        else if (i == 2) snappedLeft = otherXs[j] - w / 2f;
                        break;
                    }
                }
                if (snapX != null) break;
            }

            // Check horizontal alignments
            float[] myYs = { y, y + h, y + h / 2f };
            float[] otherYs = { otherTop, otherBottom, otherCenterY };

            for (int i = 0; i < myYs.Length; i++)
            {
                for (int j = 0; j < otherYs.Length; j++)
                {
                    if (Math.Abs(myYs[i] - otherYs[j]) <= snapThreshold)
                    {
                        snapY = otherYs[j];
                        if (i == 0) snappedTop = otherYs[j];
                        else if (i == 1) snappedTop = otherYs[j] - h;
                        else if (i == 2) snappedTop = otherYs[j] - h / 2f;
                        break;
                    }
                }
                if (snapY != null) break;
            }
        }

        ActiveVerticalSnapX = snapX;
        ActiveHorizontalSnapY = snapY;

        if (snapX != null || snapY != null)
        {
            Invalidate();
        }

        return new Vector2(snappedLeft, snappedTop);
    }

    public float? GetSnapX(FrameworkElement element, float targetX, float snapThreshold = 8f)
    {
        float? snapVal = null;
        foreach (var child in DesignSurface.Children)
        {
            if (child == element || child is not FrameworkElement other)
                continue;

            Rect otherRect = GetElementRect(other);
            float[] otherXs = { otherRect.X, otherRect.X + otherRect.Width, otherRect.X + otherRect.Width / 2f };

            foreach (var ox in otherXs)
            {
                if (Math.Abs(targetX - ox) <= snapThreshold)
                {
                    snapVal = ox;
                    break;
                }
            }
            if (snapVal != null) break;
        }

        ActiveVerticalSnapX = snapVal;
        if (snapVal != null)
        {
            Invalidate();
        }
        return snapVal;
    }

    public float? GetSnapY(FrameworkElement element, float targetY, float snapThreshold = 8f)
    {
        float? snapVal = null;
        foreach (var child in DesignSurface.Children)
        {
            if (child == element || child is not FrameworkElement other)
                continue;

            Rect otherRect = GetElementRect(other);
            float[] otherYs = { otherRect.Y, otherRect.Y + otherRect.Height, otherRect.Y + otherRect.Height / 2f };

            foreach (var oy in otherYs)
            {
                if (Math.Abs(targetY - oy) <= snapThreshold)
                {
                    snapVal = oy;
                    break;
                }
            }
            if (snapVal != null) break;
        }

        ActiveHorizontalSnapY = snapVal;
        if (snapVal != null)
        {
            Invalidate();
        }
        return snapVal;
    }

    public void OnDrop(DragEventArgs args)
    {
        if (!AllowDrop) return;
        
        Drop?.Invoke(this, args);

        if (args.Handled) return;

        if (args.Data.Contains(StandardDataFormats.Tool))
        {
            var toolData = args.Data.GetData(StandardDataFormats.Tool);
            string? toolName = toolData as string;
            if (string.IsNullOrEmpty(toolName)) return;

            Type? controlType = null;

            string[] searchNamespaces = {
                "Microsoft.UI.Xaml.Controls",
                "Microsoft.UI.Xaml",
                "ProGPU.Designer"
            };

            foreach (var ns in searchNamespaces)
            {
                var typeName = $"{ns}.{toolName}";
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    controlType = assembly.GetType(typeName);
                    if (controlType != null) break;
                }
                if (controlType != null) break;
            }

            if (controlType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                        {
                            controlType = type;
                            break;
                        }
                    }
                    if (controlType != null) break;
                }
            }

            if (controlType != null && typeof(FrameworkElement).IsAssignableFrom(controlType))
            {
                try
                {
                    var newInstance = Activator.CreateInstance(controlType) as FrameworkElement;
                    if (newInstance != null)
                    {
                        Vector2 snappedPos = SnapPositionToGrid(args.Position, 10f);

                        Canvas.SetLeft(newInstance, snappedPos.X);
                        Canvas.SetTop(newInstance, snappedPos.Y);

                        if (float.IsNaN(newInstance.Width) || newInstance.Width <= 0) newInstance.Width = 120f;
                        if (float.IsNaN(newInstance.Height) || newInstance.Height <= 0) newInstance.Height = 36f;

                        if (newInstance is Button button)
                        {
                            var richText = new RichTextBlock { Font = ThemeResourceFont() };
                            richText.Inlines.Add(new Run(toolName));
                            button.Content = richText;
                        }
                        else if (newInstance is TextBlock textBlock)
                        {
                            textBlock.Text = toolName;
                        }

                        DesignSurface.Children.Add(newInstance);
                        SelectElement(newInstance);

                        InvalidateMeasure();
                        InvalidateArrange();
                        Invalidate();
                        DesignSurface.InvalidateArrange();
                        DesignSurface.Invalidate();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DesignerCanvas] Error instantiating {toolName}: {ex.Message}");
                }
            }
        }
    }

    private TtfFont? ThemeResourceFont()
    {
        return PopupService.DefaultFont;
    }

    public override void OnRender(DrawingContext context)
    {
        if (Background is SolidColorBrush solidBg)
        {
            context.DrawRectangle(solidBg, null, new Rect(0, 0, Size.X, Size.Y));
        }
        else if (Background is ThemeResourceBrush themeBg)
        {
            var brush = ThemeManager.GetBrush(themeBg.ResourceKey, ActualTheme);
            context.DrawRectangle(brush, null, new Rect(0, 0, Size.X, Size.Y));
        }

        base.OnRender(context);

        // 1. Grid Background
        float gridSpacing = 10f;
        var gridBrush = ActualTheme == ElementTheme.Dark
            ? new SolidColorBrush(new Vector4(1f, 1f, 1f, 0.08f))
            : new SolidColorBrush(new Vector4(0f, 0f, 0f, 0.06f));

        float dpiScale = 1.0f;
        var activeWindow = WindowManager.ActiveWindows.Count > 0 ? WindowManager.ActiveWindows[0] : null;
        if (activeWindow != null && activeWindow.SilkWindow != null)
        {
            dpiScale = (float)activeWindow.SilkWindow.FramebufferSize.X / activeWindow.SilkWindow.Size.X;
        }

        for (float x = gridSpacing; x < Size.X; x += gridSpacing)
        {
            for (float y = gridSpacing; y < Size.Y; y += gridSpacing)
            {
                // DPI-Aware Snapping: snaps in physical coordinates snapped to 1/4th of a physical pixel, then snap-backed
                float physX = MathF.Round(x * dpiScale * 4f) / 4f;
                float physY = MathF.Round(y * dpiScale * 4f) / 4f;

                Vector2 snapBackPos = new Vector2(physX, physY) / dpiScale;
                
                context.FillCircle(gridBrush, snapBackPos, 0.75f);
            }
        }

        // 2. High-Contrast Dashed Neon Guidelines
        var neonBrush = new SolidColorBrush(new Vector4(1f, 0.078f, 0.576f, 1f)); // Neon Pink!
        var neonPen = new Pen(neonBrush, 1.5f);

        if (ActiveVerticalSnapX != null)
        {
            float snapX = ActiveVerticalSnapX.Value;
            DrawDashedVerticalLine(context, neonPen, snapX, 0f, Size.Y);
        }

        if (ActiveHorizontalSnapY != null)
        {
            float snapY = ActiveHorizontalSnapY.Value;
            DrawDashedHorizontalLine(context, neonPen, snapY, 0f, Size.X);
        }
    }

    private void DrawDashedVerticalLine(DrawingContext context, Pen pen, float x, float y1, float y2, float dashLength = 6f, float gapLength = 4f)
    {
        float y = y1;
        while (y < y2)
        {
            float nextY = Math.Min(y + dashLength, y2);
            context.DrawLine(pen, new Vector2(x, y), new Vector2(x, nextY));
            y += dashLength + gapLength;
        }
    }

    private void DrawDashedHorizontalLine(DrawingContext context, Pen pen, float y, float x1, float x2, float dashLength = 6f, float gapLength = 4f)
    {
        float x = x1;
        while (x < x2)
        {
            float nextX = Math.Min(x + dashLength, x2);
            context.DrawLine(pen, new Vector2(x, y), new Vector2(nextX, y));
            x += dashLength + gapLength;
        }
    }
}
