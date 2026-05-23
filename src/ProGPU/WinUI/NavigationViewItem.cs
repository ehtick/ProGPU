using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;
using ProGPU.Text;

namespace ProGPU.WinUI;

public class NavigationViewItem : Control
{
    private string _text = string.Empty;
    private string _icon = string.Empty;
    private bool _isSelected;
    private bool _isExpanded;
    private int _level;
    private FrameworkElement? _page;

    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; Invalidate(); } }
    }

    public string Icon
    {
        get => _icon;
        set { if (_icon != value) { _icon = value; Invalidate(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; Invalidate(); } }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; Invalidate(); } }
    }

    public int Level
    {
        get => _level;
        internal set { if (_level != value) { _level = value; Invalidate(); } }
    }

    public FrameworkElement? Page
    {
        get => _page;
        set { _page = value; }
    }

    public ObservableCollection<NavigationViewItem> Items { get; }

    public NavigationViewItem()
    {
        Items = new ObservableCollection<NavigationViewItem>();
        Items.CollectionChanged += (s, e) => Invalidate();
        HeightConstraint = 40f;
    }

    public NavigationViewItem(string text, string icon = "", FrameworkElement? page = null) : this()
    {
        Text = text;
        Icon = icon;
        Page = page;
    }

    private NavigationView? FindParentNavigationView()
    {
        var p = Parent;
        while (p != null)
        {
            if (p is NavigationView nav) return nav;
            p = p.Parent;
        }
        return null;
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            base.OnPointerPressed(e);
            
            var nav = FindParentNavigationView();
            if (nav != null)
            {
                // In expanded view and clicking on the right expand/collapse indicator (arrow)
                if (Items.Count > 0 && nav.IsPaneOpen && e.Position.X >= Size.X - 40f)
                {
                    IsExpanded = !IsExpanded;
                    nav.OnItemExpandedChanged(this);
                }
                else
                {
                    nav.SelectedItem = this;
                }
                e.Handled = true;
            }
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float w = WidthConstraint ?? availableSize.X;
        float h = HeightConstraint ?? 40f;
        return new Vector2(w, h);
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        Size = new Vector2(arrangeRect.Width, arrangeRect.Height);
    }

    public override void OnRender(DrawingContext context)
    {
        var nav = FindParentNavigationView();
        bool isPaneOpen = nav?.IsPaneOpen ?? false;
        
        // 1. Draw modern backgrounds depending on active selection or hover
        if (IsSelected)
        {
            context.DrawRectangle(new SolidColorBrush(0xFFFFFF12), null, new Rect(0f, 0f, Size.X, Size.Y));
        }
        else if (IsPointerOver)
        {
            context.DrawRectangle(new SolidColorBrush(0xFFFFFF0F), null, new Rect(0f, 0f, Size.X, Size.Y));
        }

        // 2. Draw 3px left accent stripe indicator
        if (IsSelected)
        {
            context.DrawRectangle(new SolidColorBrush(0x0078D4FF), null, new Rect(3f, 6f, 3f, Size.Y - 12f));
        }

        var font = nav?.GetActiveFont();
        if (font != null)
        {
            float startX = 16f + (Level * 16f); // nesting indentation
            float textY = (Size.Y - 14f) / 2f;

            // 3. Draw Icon in white
            if (!string.IsNullOrEmpty(Icon))
            {
                context.DrawText(Icon, font, 16f, new SolidColorBrush(0xFFFFFFFF), new Vector2(startX, (Size.Y - 16f) / 2f));
                startX += 28f;
            }

            // 4. Draw label text in white (or semi-translucent if unselected)
            if (isPaneOpen && !string.IsNullOrEmpty(Text))
            {
                var textBrush = IsSelected ? new SolidColorBrush(0xFFFFFFFF) : new SolidColorBrush(0xFFFFFFD0);
                context.DrawText(Text, font, 14f, textBrush, new Vector2(startX, textY));
            }

            // 5. Draw nested expandable arrow indicator
            if (isPaneOpen && Items.Count > 0)
            {
                string arrow = IsExpanded ? "▼" : "▶";
                context.DrawText(arrow, font, 10f, new SolidColorBrush(0xFFFFFF80), new Vector2(Size.X - 24f, (Size.Y - 10f) / 2f));
            }
        }

        base.OnRender(context);
    }
}
