using System;

namespace System.Windows;

public interface IPortablePresentationSourceHost : IDisposable
{
    event EventHandler? RenderRequested;

    event EventHandler? CursorRequested;

    object? RootVisual { get; set; }

    object? CompositionTarget { get; }

    IntPtr Handle { get; }

    object? RequestedCursor { get; }

    string? RequestedCursorName { get; }

    Func<double, double, object?>? HitTestOverride { get; set; }

    Func<double, double, object?[]?>? HitTestAllOverride { get; set; }

    Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

    Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

    void SetDeviceScale(double dpiScaleX, double dpiScaleY);

    void SetClientSize(double width, double height);
}
