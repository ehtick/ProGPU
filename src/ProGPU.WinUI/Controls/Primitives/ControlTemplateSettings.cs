namespace Microsoft.UI.Xaml.Controls.Primitives;

/// <summary>Calculated values exposed to an <see cref="Controls.AppBar"/> template.</summary>
public sealed class AppBarTemplateSettings : DependencyObject
{
    public Windows.Foundation.Rect ClipRect { get; internal set; }
    public double CompactVerticalDelta { get; internal set; }
    public Thickness CompactRootMargin { get; internal set; }
    public double MinimalVerticalDelta { get; internal set; }
    public Thickness MinimalRootMargin { get; internal set; }
    public double HiddenVerticalDelta { get; internal set; }
    public Thickness HiddenRootMargin { get; internal set; }
    public double NegativeCompactVerticalDelta { get; internal set; }
    public double NegativeMinimalVerticalDelta { get; internal set; }
    public double NegativeHiddenVerticalDelta { get; internal set; }
}

/// <summary>Calculated values exposed to an <see cref="Controls.AppBarButton"/> template.</summary>
public sealed class AppBarButtonTemplateSettings : DependencyObject
{
    public double KeyboardAcceleratorTextMinWidth { get; internal set; }
}

/// <summary>Calculated values exposed to an <see cref="Controls.AppBarToggleButton"/> template.</summary>
public sealed class AppBarToggleButtonTemplateSettings : DependencyObject
{
    public double KeyboardAcceleratorTextMinWidth { get; internal set; }
}

/// <summary>Calculated values exposed to a <see cref="Controls.CalendarView"/> template.</summary>
public sealed class CalendarViewTemplateSettings : DependencyObject
{
    public double MinViewWidth { get; internal set; }
    public string HeaderText { get; internal set; } = string.Empty;
    public string WeekDay1 { get; internal set; } = string.Empty;
    public string WeekDay2 { get; internal set; } = string.Empty;
    public string WeekDay3 { get; internal set; } = string.Empty;
    public string WeekDay4 { get; internal set; } = string.Empty;
    public string WeekDay5 { get; internal set; } = string.Empty;
    public string WeekDay6 { get; internal set; } = string.Empty;
    public string WeekDay7 { get; internal set; } = string.Empty;
    public bool HasMoreContentAfter { get; internal set; }
    public bool HasMoreContentBefore { get; internal set; }
    public bool HasMoreViews { get; internal set; }
    public Windows.Foundation.Rect ClipRect { get; internal set; }
    public double CenterX { get; internal set; }
    public double CenterY { get; internal set; }
}

/// <summary>Calculated values exposed to a <see cref="Controls.SplitView"/> template.</summary>
public sealed class SplitViewTemplateSettings : DependencyObject
{
    public double OpenPaneLength { get; private set; }
    public double NegativeOpenPaneLength { get; private set; }
    public double OpenPaneLengthMinusCompactLength { get; private set; }
    public double NegativeOpenPaneLengthMinusCompactLength { get; private set; }
    public GridLength OpenPaneGridLength { get; private set; }
    public GridLength CompactPaneGridLength { get; private set; }

    internal void Update(double openPaneLength, double compactPaneLength)
    {
        OpenPaneLength = openPaneLength;
        NegativeOpenPaneLength = -openPaneLength;
        OpenPaneLengthMinusCompactLength =
            openPaneLength - compactPaneLength;
        NegativeOpenPaneLengthMinusCompactLength =
            -OpenPaneLengthMinusCompactLength;
        OpenPaneGridLength = new GridLength(
            (float)openPaneLength,
            GridUnitType.Absolute);
        CompactPaneGridLength = new GridLength(
            (float)compactPaneLength,
            GridUnitType.Absolute);
    }
}

/// <summary>Calculated values exposed to a <see cref="Controls.ToggleSwitch"/> template.</summary>
public sealed class ToggleSwitchTemplateSettings : DependencyObject
{
    public double KnobCurrentToOnOffset { get; internal set; }
    public double KnobCurrentToOffOffset { get; internal set; }
    public double KnobOnToOffOffset { get; internal set; }
    public double KnobOffToOnOffset { get; internal set; }
    public double CurtainCurrentToOnOffset { get; internal set; }
    public double CurtainCurrentToOffOffset { get; internal set; }
    public double CurtainOnToOffOffset { get; internal set; }
    public double CurtainOffToOnOffset { get; internal set; }
}
