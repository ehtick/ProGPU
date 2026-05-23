using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using ProGPU.Text;

namespace ProGPU.WinUI;

public class ComboBox : Control
{
    private bool _isDropDownOpen;
    private ComboBoxItem? _selectedItem;
    private string _placeholderText = "Select item...";
    private TtfFont? _font;
    private float _fontSize = 14f;

    public ObservableCollection<ComboBoxItem> Items { get; }

    public bool IsDropDownOpen
    {
        get => _isDropDownOpen;
        set
        {
            if (_isDropDownOpen != value)
            {
                _isDropDownOpen = value;
                UpdateVisualTree();
            }
        }
    }

    public ComboBoxItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                if (_selectedItem != null) _selectedItem.IsSelected = false;
                _selectedItem = value;
                if (_selectedItem != null) _selectedItem.IsSelected = true;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    public string PlaceholderText
    {
        get => _placeholderText;
        set { if (_placeholderText != value) { _placeholderText = value; Invalidate(); } }
    }

    public TtfFont? Font
    {
        get => _font;
        set { if (_font != value) { _font = value; Invalidate(); } }
    }

    public float FontSize
    {
        get => _fontSize;
        set { if (_fontSize != value) { _fontSize = value; Invalidate(); } }
    }

    public event EventHandler? SelectionChanged;

    public ComboBox()
    {
        Items = new ObservableCollection<ComboBoxItem>();
        Items.CollectionChanged += OnItemsChanged;
        CornerRadius = 4f;
        Padding = new Thickness(10, 6, 32, 6); // Extra right padding for arrow
        HeightConstraint = 32f;
        WidthConstraint = 180f;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ComboBoxItem item in e.NewItems)
            {
                item.Selected += OnItemSelected;
            }
        }
        if (e.OldItems != null)
        {
            foreach (ComboBoxItem item in e.OldItems)
            {
                item.Selected -= OnItemSelected;
            }
        }
        if (IsDropDownOpen)
        {
            UpdateVisualTree();
        }
    }

    private void OnItemSelected(object? sender, EventArgs e)
    {
        if (sender is ComboBoxItem item)
        {
            SelectedItem = item;
            IsDropDownOpen = false;
        }
    }

    private void UpdateVisualTree()
    {
        ClearChildren();
        if (_isDropDownOpen)
        {
            foreach (var item in Items)
            {
                AddChild(item);
            }
        }
        Invalidate();
    }

    public override void OnVisualStateChanged()
    {
        // Automatically collapse dropdown when focus is lost
        if (!IsFocused && IsDropDownOpen)
        {
            IsDropDownOpen = false;
        }
        base.OnVisualStateChanged();
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerPressed(e); // Sets focus to this ComboBox

            // Toggle dropdown if clicked on the header button area
            if (e.Position.Y < 32f)
            {
                IsDropDownOpen = !IsDropDownOpen;
            }
        }
    }

    public override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsEnabled && IsFocused)
        {
            int count = Items.Count;
            int currentIdx = SelectedItem != null ? Items.IndexOf(SelectedItem) : -1;

            if (e.Key == Key.Down)
            {
                if (count > 0)
                {
                    int nextIdx = (currentIdx + 1) % count;
                    SelectedItem = Items[nextIdx];
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (count > 0)
                {
                    int prevIdx = currentIdx - 1;
                    if (prevIdx < 0) prevIdx = count - 1;
                    SelectedItem = Items[prevIdx];
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (IsDropDownOpen)
                {
                    IsDropDownOpen = false;
                    e.Handled = true;
                }
            }
        }
        base.OnKeyDown(e);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float w = WidthConstraint ?? Math.Max(120f, availableSize.X);
        float h = HeightConstraint ?? 32f;

        if (IsDropDownOpen)
        {
            // Dropdown adds its height dynamically for container allocation
            float dropDownHeight = Items.Count * 32f;
            foreach (var item in Items)
            {
                item.Measure(new Vector2(w, 32f));
            }
        }

        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        float mainH = HeightConstraint ?? 32f;
        
        if (IsDropDownOpen)
        {
            float currentY = mainH;
            foreach (var item in Items)
            {
                item.Arrange(new Rect(0, currentY, arrangeRect.Width, 32f));
                currentY += 32f;
            }
            Size = new Vector2(arrangeRect.Width, currentY);
        }
        else
        {
            Size = new Vector2(arrangeRect.Width, mainH);
        }
    }

    public TtfFont? GetActiveFont()
    {
        if (Font != null) return Font;
        var p = Parent;
        while (p != null)
        {
            var prop = p.GetType().GetProperty("Font");
            if (prop != null && prop.GetValue(p) is TtfFont f) return f;
            p = p.Parent;
        }

        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in asm)
            {
                var type = assembly.GetType("ProGPU.Samples.Program");
                if (type != null)
                {
                    var method = type.GetMethod("GetFont");
                    if (method != null && method.Invoke(null, null) is TtfFont staticFont)
                    {
                        return staticFont;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public override void OnRender(DrawingContext context)
    {
        float headerH = HeightConstraint ?? 32f;
        Rect headerRect = new Rect(0, 0, Size.X, headerH);

        // 1. Draw ComboBox main button card
        Brush bg;
        Pen borderPen;

        if (!IsEnabled)
        {
            bg = new SolidColorBrush(0x2A2A3540);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF08), 1f);
        }
        else if (IsDropDownOpen || IsFocused)
        {
            bg = new SolidColorBrush(0x13131AFF);
            borderPen = new Pen(new SolidColorBrush(0x0078D4FF), 2f); // Segoe Blue active focus ring/active state
        }
        else if (IsPointerOver)
        {
            bg = new SolidColorBrush(0xFFFFFF15);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF30), 1f);
        }
        else
        {
            bg = new SolidColorBrush(0xFFFFFF0A);
            borderPen = new Pen(new SolidColorBrush(0xFFFFFF15), 1f);
        }

        // Draw header background shape
        if (CornerRadius <= 0f)
        {
            context.DrawRectangle(bg, borderPen, headerRect);
        }
        else
        {
            var roundedPath = CreateRoundedRectPath(headerRect, CornerRadius);
            context.DrawPath(bg, borderPen, roundedPath);
        }

        // 2. Draw active Selected Text or Placeholder Text
        var activeFont = GetActiveFont();
        if (activeFont != null)
        {
            float textY = (headerH - FontSize) / 2f;
            string textToDraw = SelectedItem != null ? SelectedItem.Text : PlaceholderText;
            Brush textBrush = SelectedItem != null 
                ? new SolidColorBrush(0xFFFFFFFF) 
                : new SolidColorBrush(0xFFFFFF50);

            context.DrawText(textToDraw, activeFont, FontSize, textBrush, new Vector2(Padding.Left, textY));

            // Draw Down Arrow (▼) character
            context.DrawText("▼", activeFont, FontSize - 2f, new SolidColorBrush(0xFFFFFFB0), new Vector2(Size.X - 22f, textY + 1f));
        }

        // 3. Draw Dropdown panel background if open
        if (IsDropDownOpen)
        {
            float dropDownHeight = Items.Count * 32f;
            Rect dropDownRect = new Rect(0, headerH, Size.X, dropDownHeight);

            // Mica style dropdown back panel with subtle borders
            var dropDownBg = new SolidColorBrush(0x1F1F1FFF);
            var dropDownBorder = new Pen(new SolidColorBrush(0xFFFFFF1F), 1f);

            context.DrawRectangle(dropDownBg, dropDownBorder, dropDownRect);
        }

        base.OnRender(context);
    }

    private static PathGeometry CreateRoundedRectPath(Rect rect, float r)
    {
        var geo = new PathGeometry();
        var fig = new PathFigure(new Vector2(rect.X + r, rect.Y), isClosed: true);
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + rect.Width - r, rect.Y)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X + rect.Width, rect.Y), new Vector2(rect.X + rect.Width, rect.Y + r)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + rect.Width, rect.Y + rect.Height - r)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X + rect.Width, rect.Y + rect.Height), new Vector2(rect.X + rect.Width - r, rect.Y + rect.Height)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X + r, rect.Y + rect.Height)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X, rect.Y + rect.Height), new Vector2(rect.X, rect.Y + rect.Height - r)));
        fig.Segments.Add(new LineSegment(new Vector2(rect.X, rect.Y + r)));
        fig.Segments.Add(new QuadraticBezierSegment(new Vector2(rect.X, rect.Y), new Vector2(rect.X + r, rect.Y)));
        geo.Figures.Add(fig);
        return geo;
    }
}
