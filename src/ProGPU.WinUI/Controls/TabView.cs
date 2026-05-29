using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using ProGPU.Text;

namespace Microsoft.UI.Xaml.Controls;

public class TabAcceleratorEventArgs : EventArgs
{
    public string CommandName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class TabView : FrameworkElement
{
    private class AddTabButton : Button
    {
        public AddTabButton()
        {
            CornerRadius = 4f;
            WidthConstraint = 28f;
            HeightConstraint = 28f;
            Content = new TextVisual 
            { 
                Text = "+", 
                FontSize = 16f, 
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Padding = new Thickness(0);
        }

        protected override string GetThemePrefix() => "Button";
    }

    private readonly AddTabButton _addButton;
    private TabViewItem? _selectedItem;
    private bool _isCtrlPressed;
    private bool _isShiftPressed;

    public ObservableCollection<TabViewItem> TabItems { get; }

    public TabViewItem? SelectedItem
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
                RebuildTabViewChildren();
            }
        }
    }

    protected override void OnPropertyChanged(Microsoft.UI.Xaml.DependencyProperty dp, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(dp, oldValue, newValue);
        if (dp == FontProperty)
        {
            Invalidate();
        }
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? TabAddRequested;
    public event EventHandler<TabAcceleratorEventArgs>? TabAcceleratorTriggered;

    public TabView()
    {
        TabItems = new ObservableCollection<TabViewItem>();
        TabItems.CollectionChanged += OnTabItemsChanged;

        _addButton = new AddTabButton();
        _addButton.Click += (s, e) => TabAddRequested?.Invoke(this, EventArgs.Empty);

        RebuildTabViewChildren();
    }

    private void OnTabItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (TabViewItem item in e.NewItems)
            {
                item.Selected += OnTabSelected;
                item.CloseRequested += OnTabCloseRequested;
            }
        }
        if (e.OldItems != null)
        {
            foreach (TabViewItem item in e.OldItems)
            {
                item.Selected -= OnTabSelected;
                item.CloseRequested -= OnTabCloseRequested;
            }
        }

        // Auto-select first tab if nothing is selected and we have items
        if (SelectedItem == null && TabItems.Count > 0)
        {
            SelectedItem = TabItems[0];
        }
        else if (SelectedItem != null && !TabItems.Contains(SelectedItem))
        {
            // If the selected tab was removed, select another close one
            if (TabItems.Count > 0)
            {
                int nextIndex = Math.Clamp(e.OldStartingIndex, 0, TabItems.Count - 1);
                SelectedItem = TabItems[nextIndex];
            }
            else
            {
                SelectedItem = null;
            }
        }

        RebuildTabViewChildren();
    }

    private void OnTabSelected(object? sender, EventArgs e)
    {
        if (sender is TabViewItem item)
        {
            SelectedItem = item;
        }
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewItem item)
        {
            TabItems.Remove(item);
        }
    }

    private void RebuildTabViewChildren()
    {
        ClearChildren();

        // 1. Add all TabViewItems
        foreach (var item in TabItems)
        {
            AddChild(item);
        }

        // 2. Add the Add Tab (+) button
        AddChild(_addButton);

        // 3. Add active tab content
        if (SelectedItem != null && SelectedItem.Content != null)
        {
            AddChild(SelectedItem.Content);
        }

        Invalidate();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        float x = 0f;
        float headerH = 40f;

        // Measure all headers
        foreach (var item in TabItems)
        {
            item.Measure(new Vector2(availableSize.X, 36f));
            x += item.DesiredSize.X;
        }

        // Measure '+' button
        _addButton.Measure(new Vector2(28f, 28f));

        // Measure content page
        if (SelectedItem != null && SelectedItem.Content != null)
        {
            float contentH = Math.Max(0f, availableSize.Y - headerH);
            SelectedItem.Content.Measure(new Vector2(availableSize.X, contentH));
        }

        return availableSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        float headerH = 40f;
        float tabH = 36f;
        float xCursor = arrangeRect.X;

        // Arrange tab headers horizontally
        foreach (var item in TabItems)
        {
            float itemW = item.DesiredSize.X;
            item.Arrange(new Rect(xCursor, arrangeRect.Y, itemW, tabH));
            xCursor += itemW;
        }

        // Arrange Add (+) Button centered vertically in the header area
        _addButton.Arrange(new Rect(xCursor + 6f, arrangeRect.Y + (headerH - 28f) / 2f, 28f, 28f));

        // Arrange selected content page below headers
        if (SelectedItem != null && SelectedItem.Content != null)
        {
            float contentY = arrangeRect.Y + headerH;
            float contentH = Math.Max(0f, arrangeRect.Height - headerH);
            SelectedItem.Content.Arrange(new Rect(arrangeRect.X, contentY, arrangeRect.Width, contentH));
        }
    }

    public TtfFont? GetActiveFont()
    {
        return Font ?? PopupService.DefaultFont;
    }

    public override void OnRender(DrawingContext context)
    {
        float headerH = 40f;

        // Draw a clean modern Fluent horizontal divider below headers
        var dividerBrush = ThemeManager.GetBrush("ControlBorder");
        context.DrawRectangle(dividerBrush, null, new Rect(0f, headerH - 1f, Size.X, 1f));

        base.OnRender(context);
    }

    public void SelectNextTab()
    {
        if (TabItems.Count <= 1) return;
        int idx = SelectedItem != null ? TabItems.IndexOf(SelectedItem) : -1;
        int nextIdx = (idx + 1) % TabItems.Count;
        SelectedItem = TabItems[nextIdx];
        TabAcceleratorTriggered?.Invoke(this, new TabAcceleratorEventArgs { CommandName = "Ctrl+Tab", Message = "Selected next tab" });
    }

    public void SelectPreviousTab()
    {
        if (TabItems.Count <= 1) return;
        int idx = SelectedItem != null ? TabItems.IndexOf(SelectedItem) : -1;
        int prevIdx = (idx - 1 + TabItems.Count) % TabItems.Count;
        SelectedItem = TabItems[prevIdx];
        TabAcceleratorTriggered?.Invoke(this, new TabAcceleratorEventArgs { CommandName = "Ctrl+Shift+Tab", Message = "Selected previous tab" });
    }

    public void RequestAddTab()
    {
        TabAddRequested?.Invoke(this, EventArgs.Empty);
        TabAcceleratorTriggered?.Invoke(this, new TabAcceleratorEventArgs { CommandName = "Ctrl+T", Message = "Added new tab" });
    }

    public void CloseSelectedTab()
    {
        if (SelectedItem != null)
        {
            var itemToRemove = SelectedItem;
            TabItems.Remove(itemToRemove);
            TabAcceleratorTriggered?.Invoke(this, new TabAcceleratorEventArgs { CommandName = "Ctrl+W", Message = $"Closed tab '{itemToRemove.HeaderText}'" });
        }
    }

    public override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (e.Key == Key.ControlLeft || e.Key == Key.ControlRight || e.Key == Key.SuperLeft || e.Key == Key.SuperRight)
        {
            _isCtrlPressed = true;
        }
        else if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
        {
            _isShiftPressed = true;
        }

        if (_isCtrlPressed)
        {
            if (e.Key == Key.Tab)
            {
                if (_isShiftPressed)
                {
                    SelectPreviousTab();
                }
                else
                {
                    SelectNextTab();
                }
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.T)
            {
                RequestAddTab();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.W)
            {
                CloseSelectedTab();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    public override void OnKeyUp(KeyRoutedEventArgs e)
    {
        if (e.Key == Key.ControlLeft || e.Key == Key.ControlRight || e.Key == Key.SuperLeft || e.Key == Key.SuperRight)
        {
            _isCtrlPressed = false;
        }
        else if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
        {
            _isShiftPressed = false;
        }

        base.OnKeyUp(e);
    }
}
