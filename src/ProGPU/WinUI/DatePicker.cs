using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;
using ProGPU.Vector;

namespace ProGPU.WinUI;

public class DatePicker : Control
{
    private DateTime? _selectedDate;
    private string _header = "Select Date";
    private CalendarView? _popupCalendar;
    private bool _isHovered;

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                Invalidate();
                SelectedDateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Header
    {
        get => _header;
        set { _header = value; Invalidate(); }
    }

    public event EventHandler? SelectedDateChanged;

    public DatePicker()
    {
        Width = 180f;
        Height = 32f;
        Background = new SolidColorBrush(0x13131AFF); // Mica dark background
        BorderBrush = new SolidColorBrush(0xFFFFFF15); // Glassy border
        BorderThickness = new Thickness(1f);
        CornerRadius = 4f;
        Padding = new Thickness(12f, 0f, 12f, 0f);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(Width, Height);
    }

    private Vector2 GetAbsolutePosition()
    {
        Vector2 pos = Offset;
        Visual? current = Parent;
        while (current != null)
        {
            pos += current.Offset;
            current = current.Parent;
        }
        return pos;
    }

    public override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        _isHovered = new Rect(Vector2.Zero, Size).Contains(e.Position);
        base.OnPointerMoved(e);
    }

    public override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (e.Position.X >= 0 && e.Position.X <= Size.X && e.Position.Y >= 0 && e.Position.Y <= Size.Y)
        {
            // Spawn calendar dropdown
            if (_popupCalendar == null)
            {
                _popupCalendar = new CalendarView();
                _popupCalendar.SelectedDatesChanged += (s, ev) =>
                {
                    SelectedDate = _popupCalendar.SelectedDate;
                    PopupService.HidePopup(_popupCalendar);
                };
            }

            _popupCalendar.SelectedDate = SelectedDate ?? DateTime.Today;

            var absPos = GetAbsolutePosition();
            // Position exactly underneath the DatePicker input box
            PopupService.ShowPopup(_popupCalendar, new Vector2(absPos.X, absPos.Y + Size.Y + 4f));
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    public override void OnRender(DrawingContext context)
    {
        var font = PopupService.DefaultFont;
        if (font == null)
        {
            base.OnRender(context);
            return;
        }

        // 1. Draw input box background and border outline
        var rect = new Rect(Vector2.Zero, Size);
        
        var borderPen = _isHovered 
            ? new Pen(new SolidColorBrush(0xFFFFFF30), BorderThickness.Left) 
            : new Pen(BorderBrush ?? new SolidColorBrush(0xFFFFFF15), BorderThickness.Left);
            
        context.DrawRoundedRectangle(Background, borderPen, rect, CornerRadius);

        // 2. Render selected date label or placeholder text
        string dateText = SelectedDate.HasValue 
            ? SelectedDate.Value.ToString("yyyy-MM-dd") 
            : "Select a date...";
            
        var textBrush = SelectedDate.HasValue 
            ? new SolidColorBrush(0xFFFFFFFF) 
            : new SolidColorBrush(0xFFFFFF60); // Muted text if placeholder

        float textY = (Size.Y - 14f) / 2f;
        context.DrawText(dateText, font, 12f, textBrush, new Vector2(Padding.Left, textY));

        // 3. Render modern vector calendar icon "📅" on the right side
        float iconX = Size.X - 26f;
        float iconY = (Size.Y - 14f) / 2f;
        context.DrawText("📅", font, 11f, new SolidColorBrush(0xFFFFFFB0), new Vector2(iconX, iconY));

        base.OnRender(context);
    }
}
