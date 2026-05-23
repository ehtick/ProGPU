using System;
using System.Numerics;
using ProGPU.Layout;
using ProGPU.Scene;

namespace ProGPU.WinUI;

public class PivotItem : FrameworkElement
{
    private string _header = string.Empty;
    private FrameworkElement? _content;

    public string Header
    {
        get => _header;
        set
        {
            if (_header != value)
            {
                _header = value;
                Invalidate();
            }
        }
    }

    public FrameworkElement? Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                if (_content != null) RemoveChild(_content);
                _content = value;
                if (_content != null) AddChild(_content);
                Invalidate();
            }
        }
    }

    public PivotItem()
    {
    }

    public PivotItem(string header, FrameworkElement? content = null)
    {
        Header = header;
        Content = content;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Content != null)
        {
            Content.Measure(availableSize);
            return Content.DesiredSize;
        }
        return Vector2.Zero;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        if (Content != null)
        {
            Content.Arrange(arrangeRect);
        }
    }
}
