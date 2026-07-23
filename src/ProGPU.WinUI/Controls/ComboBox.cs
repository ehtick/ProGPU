using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Specialized;
using System.Numerics;
using Silk.NET.Input;
using ProGPU.Layout;
using ProGPU.Vector;
using ProGPU.Scene;
using Microsoft.UI.Input;

namespace Microsoft.UI.Xaml.Controls;

public enum ComboBoxSelectionChangedTrigger
{
    Committed,
    Always
}

public class ComboBox : Selector
{
    private float _fontSize = 14f;
    private Border? _dropDownPopup;
    private uint _pendingTouchPointerId;
    public Border? DropDownPopup => _dropDownPopup;

    public static readonly DependencyProperty IsDropDownOpenProperty = Register(
        nameof(IsDropDownOpen),
        typeof(bool),
        false,
        static (dependencyObject, _) => ((ComboBox)dependencyObject).OnDropDownStateChanged());

    public static readonly DependencyProperty IsEditableProperty = Register(
        nameof(IsEditable),
        typeof(bool),
        false);

    public static readonly DependencyProperty MaxDropDownHeightProperty = Register(
        nameof(MaxDropDownHeight),
        typeof(double),
        double.PositiveInfinity);

    public static readonly DependencyProperty HeaderProperty = Register(
        nameof(Header),
        typeof(object),
        null);

    public static readonly DependencyProperty HeaderTemplateProperty = Register(
        nameof(HeaderTemplate),
        typeof(DataTemplate),
        null);

    public static readonly DependencyProperty PlaceholderTextProperty = Register(
        nameof(PlaceholderText),
        typeof(string),
        string.Empty);

    public static readonly DependencyProperty LightDismissOverlayModeProperty = Register(
        nameof(LightDismissOverlayMode),
        typeof(LightDismissOverlayMode),
        LightDismissOverlayMode.Auto);

    public static readonly DependencyProperty IsTextSearchEnabledProperty = Register(
        nameof(IsTextSearchEnabled),
        typeof(bool),
        true);

    public static readonly DependencyProperty SelectionChangedTriggerProperty = Register(
        nameof(SelectionChangedTrigger),
        typeof(ComboBoxSelectionChangedTrigger),
        ComboBoxSelectionChangedTrigger.Committed);

    public static readonly DependencyProperty PlaceholderForegroundProperty = Register(
        nameof(PlaceholderForeground),
        typeof(Brush),
        null);

    public static readonly DependencyProperty TextProperty = Register(
        nameof(Text),
        typeof(string),
        string.Empty);

    public static readonly DependencyProperty TextBoxStyleProperty = Register(
        nameof(TextBoxStyle),
        typeof(Style),
        null);

    public static readonly DependencyProperty DescriptionProperty = Register(
        nameof(Description),
        typeof(object),
        null);

    public static readonly DependencyProperty HeaderPlacementProperty = Register(
        nameof(HeaderPlacement),
        typeof(ControlHeaderPlacement),
        ControlHeaderPlacement.Top);

    public bool IsEditable
    {
        get => (bool)(GetValue(IsEditableProperty) ?? false);
        set => SetValue(IsEditableProperty, value);
    }

    public bool IsDropDownOpen
    {
        get
        {
            // If the popup is closed from the outside, keep state synced
            var value = (bool)(GetValue(IsDropDownOpenProperty) ?? false);
            if (value && _dropDownPopup != null && !PopupService.ActivePopups.Contains(_dropDownPopup))
            {
                SetValue(IsDropDownOpenProperty, false);
                return false;
            }
            return value;
        }
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => (double)(GetValue(MaxDropDownHeightProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public DataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty) as DataTemplate;
        set => SetValue(HeaderTemplateProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty) as string ?? string.Empty;
        set => SetValue(PlaceholderTextProperty, value ?? string.Empty);
    }

    public LightDismissOverlayMode LightDismissOverlayMode
    {
        get => (LightDismissOverlayMode)(GetValue(LightDismissOverlayModeProperty) ?? LightDismissOverlayMode.Auto);
        set => SetValue(LightDismissOverlayModeProperty, value);
    }

    public bool IsTextSearchEnabled
    {
        get => (bool)(GetValue(IsTextSearchEnabledProperty) ?? true);
        set => SetValue(IsTextSearchEnabledProperty, value);
    }

    public ComboBoxSelectionChangedTrigger SelectionChangedTrigger
    {
        get => (ComboBoxSelectionChangedTrigger)(
            GetValue(SelectionChangedTriggerProperty) ??
            ComboBoxSelectionChangedTrigger.Committed);
        set => SetValue(SelectionChangedTriggerProperty, value);
    }

    public Brush? PlaceholderForeground
    {
        get => GetValue(PlaceholderForegroundProperty) as Brush;
        set => SetValue(PlaceholderForegroundProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty) as string ?? string.Empty;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public Style? TextBoxStyle
    {
        get => GetValue(TextBoxStyleProperty) as Style;
        set => SetValue(TextBoxStyleProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ControlHeaderPlacement HeaderPlacement
    {
        get => (ControlHeaderPlacement)(
            GetValue(HeaderPlacementProperty) ?? ControlHeaderPlacement.Top);
        set => SetValue(HeaderPlacementProperty, value);
    }

    public bool IsSelectionBoxHighlighted => IsFocused;
    public object? SelectionBoxItem =>
        (SelectedItem as ComboBoxItem)?.Content ?? SelectedItem;
    public DataTemplate? SelectionBoxItemTemplate => ItemTemplate;
    public ComboBoxTemplateSettings TemplateSettings { get; } = new();

    protected override void OnPropertyChanged(Microsoft.UI.Xaml.DependencyProperty dp, object? oldValue, object? newValue)
    {
        base.OnPropertyChanged(dp, oldValue, newValue);
        if (dp == FontProperty)
        {
            Invalidate();
        }
    }

    public new float FontSize
    {
        get => _fontSize;
        set { if (_fontSize != value) { _fontSize = value; Invalidate(); } }
    }

    public new event EventHandler? SelectionChanged;
    public event EventHandler? DropDownOpening;
    public event EventHandler? DropDownOpened;
    public event EventHandler? DropDownClosed;

    static ComboBox()
    {
        BindingMemberAccessorRegistry.Register<ComboBox, ComboBoxTemplateSettings>(
            nameof(TemplateSettings),
            static source => source.TemplateSettings);
    }

    public ComboBox()
    {
        Items.CollectionChanged += OnItemsChanged;
        CornerRadius = 4f;
        Padding = new Thickness(10, 6, 32, 6); // Extra right padding for arrow
        HeightConstraint = 32f;
        WidthConstraint = 180f;

        var defaultStyle = ThemeManager.GetDefaultStyle(GetType());
        if (defaultStyle != null)
        {
            SetDefaultStyle(defaultStyle);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var value in e.NewItems)
            {
                if (value is ComboBoxItem item)
                    item.Selected += OnItemSelected;
            }
        }
        if (e.OldItems != null)
        {
            foreach (var value in e.OldItems)
            {
                if (value is ComboBoxItem item)
                    item.Selected -= OnItemSelected;
            }
        }
        if (IsDropDownOpen)
        {
            UpdatePopupState();
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

    protected override void OnSelectedItemChanged(object? oldValue, object? newValue)
    {
        if (oldValue is ComboBoxItem oldItem) oldItem.IsSelected = false;
        if (newValue is ComboBoxItem newItem)
        {
            newItem.IsSelected = true;
            if (!IsEditable) Text = newItem.Text;
        }
        base.OnSelectedItemChanged(oldValue, newValue);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDropDownStateChanged()
    {
        if ((bool)(GetValue(IsDropDownOpenProperty) ?? false))
            DropDownOpening?.Invoke(this, EventArgs.Empty);
        UpdatePopupState();
        if ((bool)(GetValue(IsDropDownOpenProperty) ?? false))
            DropDownOpened?.Invoke(this, EventArgs.Empty);
        else
            DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private Vector2 GetAbsolutePosition()
    {
        return Vector2.Transform(Vector2.Zero, GetGlobalTransformMatrix());
    }

    private Rect LogicalToPhysical(Rect rect) =>
        FlowDirection == FlowDirection.RightToLeft
            ? new Rect(Size.X - rect.Right, rect.Y, rect.Width, rect.Height)
            : rect;

    private ProGPU.Text.TextShapingOptions GetTextShapingOptions() =>
        ProGPU.Text.TextShapingOptions.Default.WithDirection(
            FlowDirection == FlowDirection.RightToLeft
                ? ProGPU.Text.Shaping.ShapingDirection.RightToLeft
                : ProGPU.Text.Shaping.ShapingDirection.LeftToRight);

    private void UpdatePopupState()
    {
        if ((bool)(GetValue(IsDropDownOpenProperty) ?? false))
        {
            if (_dropDownPopup == null)
            {
                var stack = new StackPanel 
                { 
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(2f, 2f, 14f, 2f)
                };
                foreach (var item in Items)
                {
                    if (item is FrameworkElement element) stack.AddChild(element);
                }

                var scrollViewer = new ScrollViewer
                {
                    Content = stack,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                _dropDownPopup = new Border
                {
                    Background = new ThemeResourceBrush("CardBackground"),
                    BorderBrush = new ThemeResourceBrush("ControlBorder"),
                    BorderThickness = new Thickness(1f),
                    CornerRadius = 4f,
                    Child = scrollViewer
                };
            }
            else
            {
                var scrollViewer = (ScrollViewer)_dropDownPopup.Child!;
                var stack = (StackPanel)scrollViewer.Content!;
                stack.ClearChildren();
                foreach (var item in Items)
                {
                    if (item is FrameworkElement element) stack.AddChild(element);
                }
            }

            var absPos = GetAbsolutePosition();
            float mainH = HeightConstraint ?? 32f;
            _dropDownPopup.Width = Size.X;
            var maximumHeight = double.IsPositiveInfinity(MaxDropDownHeight)
                ? 300f
                : Math.Min(300f, (float)Math.Max(0d, MaxDropDownHeight));
            _dropDownPopup.Height = Math.Min(maximumHeight, Items.Count * 32f + 2f);
            _dropDownPopup.FlowDirection = FlowDirection;
            TemplateSettings.Update(
                Size.X,
                _dropDownPopup.Height,
                headerHeight: HeightConstraint ?? 32f);

            // Force theme synchronization right before showing the popup
            _dropDownPopup.NotifyThemeChanged();

            PopupService.ShowPopup(_dropDownPopup, new Vector2(absPos.X, absPos.Y + mainH + 2f), this);
        }
        else
        {
            if (_dropDownPopup != null)
            {
                PopupService.HidePopup(_dropDownPopup);
            }
        }
        Invalidate();
    }

    public override void OnVisualStateChanged()
    {
        // Automatically collapse dropdown when focus is lost
        if (!IsFocused && !IsPointerPressed && IsDropDownOpen)
        {
            bool focusIsWithinPopup = false;
            var focused = InputSystem.FocusedElement;
            if (focused != null && _dropDownPopup != null)
            {
                Visual? current = focused;
                while (current != null)
                {
                    if (current == _dropDownPopup)
                    {
                        focusIsWithinPopup = true;
                        break;
                    }
                    current = current.Parent;
                }
            }

            if (!focusIsWithinPopup)
            {
                IsDropDownOpen = false;
            }
        }
        base.OnVisualStateChanged();
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (IsEnabled)
        {
            if (e.Pointer.PointerDeviceType is PointerDeviceType.Touch or PointerDeviceType.Pen)
            {
                _pendingTouchPointerId = e.Pointer.PointerId;
                e.Handled = true;
                base.OnPointerPressed(e);
                return;
            }

            // Toggle dropdown if clicked on the header button area
            if (e.GetCurrentPoint(this).Position.Y < 32f)
            {
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
            }

            base.OnPointerPressed(e); // Sets focus to this ComboBox
        }
    }

    public override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_pendingTouchPointerId == e.Pointer.PointerId)
        {
            if (IsEnabled && IsPointerPressed && IsPointerOver && e.GetCurrentPoint(this).Position.Y < 32f)
            {
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
            }
            _pendingTouchPointerId = 0;
        }
        base.OnPointerReleased(e);
    }

    public override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        if (_pendingTouchPointerId == e.Pointer.PointerId) _pendingTouchPointerId = 0;
        base.OnPointerCanceled(e);
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
        TemplateSettings.Update(w, 0f, h);
        return new Vector2(w, h);
    }



    public override Brush? GetCurrentBackground()
    {
        if (!IsEnabled) return ThemeManager.GetBrush("ComboBoxBackgroundDisabled") ?? Background;
        if (IsDropDownOpen) return ThemeManager.GetBrush("ComboBoxBackgroundPressed") ?? ThemeManager.GetBrush("CardBackground");
        return base.GetCurrentBackground();
    }

    public override Brush? GetCurrentBorderBrush()
    {
        if (!IsEnabled) return ThemeManager.GetBrush("ComboBoxBorderBrushDisabled") ?? BorderBrush;
        if (IsDropDownOpen) return ThemeManager.GetBrush("ComboBoxBorderBrushFocused") ?? ThemeManager.GetBrush("SystemAccentColor");
        return base.GetCurrentBorderBrush();
    }

    public override void OnRender(DrawingContext context)
    {
        base.OnRender(context); // Draw template background child first

        var activeFamily = ActualThemeFamily;
        var activeTheme = ActualTheme;

        float headerH = HeightConstraint ?? 32f;
        Rect headerRect = new Rect(0, 0, Size.X, headerH);

        if (!HasTemplate)
        {
            // ComboBox main button card
            Brush? bg = GetCurrentBackground();
            Brush? borderBrush = GetCurrentBorderBrush();
            Pen pen = new Pen(borderBrush ?? ThemeManager.GetBrush("ControlBorder"), BorderThickness.Left > 0 ? BorderThickness.Left : 1f);

            // Draw header background shape
            context.DrawRoundedRectangle(bg, pen, headerRect, CornerRadius.RenderingRadius);
        }

        // Draw active Selected Text or Placeholder Text
        var activeFont = GetActiveFont();
        if (activeFont != null)
        {
            float textY = (headerH - FontSize) / 2f;
            var selectedItem = SelectedItem as ComboBoxItem;
            string textToDraw = selectedItem?.Text ?? PlaceholderText;
            Brush textBrush = SelectedItem != null
                ? (Foreground ?? ThemeManager.GetBrush("TextPrimary")) 
                : (PlaceholderForeground ?? ThemeManager.GetBrush("TextSecondary"));

            Rect logicalTextBounds = new Rect(
                Padding.Left,
                textY,
                Math.Max(0f, Size.X - Padding.Left - Padding.Right),
                FontSize);
            Rect textBounds = LogicalToPhysical(logicalTextBounds);
            context.DrawText(
                textToDraw,
                activeFont,
                FontSize,
                textBrush,
                new Vector2(textBounds.X, textY),
                Matrix4x4.Identity,
                textBounds,
                textShapingOptions: GetTextShapingOptions(),
                textAlignment: FlowDirection == FlowDirection.RightToLeft
                    ? ProGPU.Text.TextAlignment.Right
                    : ProGPU.Text.TextAlignment.Left);

            if (activeFamily == VisualThemeFamily.macOS)
            {
                float capW = 22f;
                float capH = headerH - 4f;
                Rect capRect = LogicalToPhysical(new Rect(Size.X - capW - 2f, 2f, capW, capH));
                
                // Draw capsule background using central theme tokens
                Brush capBg = ThemeManager.GetBrush("ControlBackground", activeTheme, activeFamily);
                context.FillRoundedRectangle(capBg, capRect, 4f);
                
                // Draw capsule border using central theme tokens
                Pen capPen = ThemeManager.GetPen("ControlBorder", 0.5f, activeTheme, activeFamily);
                context.DrawRoundedRectangle(null, capPen, capRect, 4f);

                Brush arrowBrush;
                if (IsPointerOver || IsDropDownOpen)
                {
                    arrowBrush = ThemeManager.GetBrush("SystemAccentColor", activeTheme, activeFamily);
                }
                else
                {
                    arrowBrush = ThemeManager.GetBrush("TextSecondary", activeTheme, activeFamily);
                }

                DrawDropDownChevron(context, arrowBrush, capRect.X + capRect.Width * 0.5f, headerH * 0.5f);
            }
            else
            {
                float arrowX = FlowDirection == FlowDirection.RightToLeft ? 16f : Size.X - 16f;
                DrawDropDownChevron(context, ThemeManager.GetBrush("TextSecondary"), arrowX, headerH * 0.5f);
            }
        }

        base.OnRender(context);
    }

    private static void DrawDropDownChevron(DrawingContext context, Brush brush, float centerX, float centerY)
    {
        var pen = new Pen(brush, 1.5f);
        context.DrawLine(pen, new Vector2(centerX - 3.5f, centerY - 1.5f), new Vector2(centerX, centerY + 2f));
        context.DrawLine(pen, new Vector2(centerX, centerY + 2f), new Vector2(centerX + 3.5f, centerY - 1.5f));
    }

    private static DependencyProperty Register(
        string name,
        Type propertyType,
        object? defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            propertyType,
            typeof(ComboBox),
            new PropertyMetadata(defaultValue, callback)
            {
                AffectsMeasure = true,
                AffectsRender = true
            });
}
