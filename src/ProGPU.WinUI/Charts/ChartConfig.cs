using System;
using System.Collections.Generic;
using ProGPU.Backend;

namespace ProGPU.WinUI.Charts
{
    /// <summary>
    /// Type of the scale used for the axis.
    /// </summary>
    public enum AxisType
    {
        Value,
        Time,
        Category
    }

    /// <summary>
    /// Padding/Grid bounds around the plot area (in pixels).
    /// </summary>
    public class GridConfig
    {
        public double? Left { get; set; }
        public double? Right { get; set; }
        public double? Top { get; set; }
        public double? Bottom { get; set; }
    }

    /// <summary>
    /// Custom formatter for axis tick labels.
    /// Returns the formatted label, or null to suppress the label.
    /// </summary>
    public delegate string? TickFormatterDelegate(double value);

    /// <summary>
    /// Configuration for a chart axis.
    /// </summary>
    public class AxisConfig
    {
        public string? Id { get; set; }
        public string Position { get; set; } = "left"; // "left" | "right" | "bottom" | "top"
        public AxisType Type { get; set; } = AxisType.Value;
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double TickLength { get; set; } = 6.0;
        public string? Name { get; set; }
        public string AutoBounds { get; set; } = "visible"; // "global" | "visible"
        public TickFormatterDelegate? TickFormatter { get; set; }
    }

    /// <summary>
    /// Configuration for the inside or slider data zoom tools.
    /// </summary>
    public class DataZoomConfig
    {
        public string Type { get; set; } = "inside"; // "inside" | "slider"
        public int XAxisIndex { get; set; } = 0;
        public double Start { get; set; } = 0.0; // Start percent [0, 100]
        public double End { get; set; } = 100.0; // End percent [0, 100]
        public double? MinSpan { get; set; }
        public double? MaxSpan { get; set; }
    }

    /// <summary>
    /// Configuration for line rendering styles.
    /// </summary>
    public class LineStyleConfig
    {
        public double Width { get; set; } = 2.0;
        public double Opacity { get; set; } = 1.0;
        public string? Color { get; set; }
    }

    /// <summary>
    /// Configuration for area fill rendering styles.
    /// </summary>
    public class AreaStyleConfig
    {
        public double Opacity { get; set; } = 0.2;
        public string? Color { get; set; }
    }

    /// <summary>
    /// Helper struct to support double values or percentage strings (e.g. "50%") safely.
    /// </summary>
    public struct PercentOrReal
    {
        public double? RealValue { get; private set; }
        public string? PercentValue { get; private set; }
        public bool IsPercent => PercentValue != null;

        public PercentOrReal(double real)
        {
            RealValue = real;
            PercentValue = null;
        }

        public PercentOrReal(string percent)
        {
            RealValue = null;
            PercentValue = percent;
        }

        public static implicit operator PercentOrReal(double d) => new PercentOrReal(d);
        public static implicit operator PercentOrReal(string s) => new PercentOrReal(s);

        public override string ToString()
        {
            return IsPercent ? PercentValue! : RealValue.ToString()!;
        }
    }

    /// <summary>
    /// Configuration for bar item borders and corners.
    /// </summary>
    public class BarItemStyleConfig
    {
        public double BorderRadius { get; set; } = 0.0;
        public double BorderWidth { get; set; } = 0.0;
        public string? BorderColor { get; set; }
    }

    /// <summary>
    /// Union-supporting struct for Scatter colormaps.
    /// </summary>
    public struct DensityColormap
    {
        public string? NamedColormap { get; }
        public IReadOnlyList<string>? CustomColors { get; }
        public bool IsNamed => NamedColormap != null;

        public DensityColormap(string name)
        {
            NamedColormap = name;
            CustomColors = null;
        }

        public DensityColormap(IReadOnlyList<string> customColors)
        {
            NamedColormap = null;
            CustomColors = customColors;
        }

        public static implicit operator DensityColormap(string name) => new DensityColormap(name);
        public static implicit operator DensityColormap(string[] colors) => new DensityColormap(colors);
        public static implicit operator DensityColormap(List<string> colors) => new DensityColormap(colors);
    }

    /// <summary>
    /// Pie radius: supports a single value, percent, or a tuple [inner, outer].
    /// </summary>
    public struct PieRadius
    {
        public PercentOrReal Inner { get; }
        public PercentOrReal Outer { get; }
        public bool IsTuple { get; }

        public PieRadius(PercentOrReal outer)
        {
            Inner = 0.0;
            Outer = outer;
            IsTuple = false;
        }

        public PieRadius(PercentOrReal inner, PercentOrReal outer)
        {
            Inner = inner;
            Outer = outer;
            IsTuple = true;
        }

        public static implicit operator PieRadius(double d) => new PieRadius(d);
        public static implicit operator PieRadius(string s) => new PieRadius(s);
        public static implicit operator PieRadius((double inner, double outer) t) => new PieRadius(t.inner, t.outer);
        public static implicit operator PieRadius((string inner, string outer) t) => new PieRadius(t.inner, t.outer);
    }

    /// <summary>
    /// Pie center coordinates (x, y). Supports numbers or percentages.
    /// </summary>
    public struct PieCenter
    {
        public PercentOrReal X { get; }
        public PercentOrReal Y { get; }

        public PieCenter(PercentOrReal x, PercentOrReal y)
        {
            X = x;
            Y = y;
        }

        public static implicit operator PieCenter((double x, double y) t) => new PieCenter(t.x, t.y);
        public static implicit operator PieCenter((string x, string y) t) => new PieCenter(t.x, t.y);
    }

    public class PieItemStyleConfig
    {
        public double BorderRadius { get; set; } = 0.0;
        public double BorderWidth { get; set; } = 0.0;
    }

    public class CandlestickItemStyleConfig
    {
        public string? UpColor { get; set; }
        public string? DownColor { get; set; }
        public string? UpBorderColor { get; set; }
        public string? DownBorderColor { get; set; }
        public double BorderWidth { get; set; } = 1.0;
    }

    /// <summary>
    /// Base configuration class for all series configs.
    /// </summary>
    public abstract class SeriesConfig
    {
        public string? Name { get; set; }
        public string? YAxis { get; set; }
        public string? Color { get; set; }
        public bool Visible { get; set; } = true;
        public string Sampling { get; set; } = "none"; // "none" | "lttb" | "average" | "max" | "min" | "ohlc"
        public int SamplingThreshold { get; set; } = 5000;
        public GpuSeriesBuffer? GpuBuffer { get; set; }
    }

    public class LineSeriesConfig : SeriesConfig
    {
        public CartesianSeriesData? Data { get; set; }
        public LineStyleConfig? LineStyle { get; set; }
        public AreaStyleConfig? AreaStyle { get; set; }
        public bool ConnectNulls { get; set; } = false;
    }

    public class AreaSeriesConfig : SeriesConfig
    {
        public CartesianSeriesData? Data { get; set; }
        public double? Baseline { get; set; }
        public AreaStyleConfig? AreaStyle { get; set; }
        public bool ConnectNulls { get; set; } = false;
    }

    public class BarSeriesConfig : SeriesConfig
    {
        public CartesianSeriesData? Data { get; set; }
        public PercentOrReal? BarWidth { get; set; }
        public double BarGap { get; set; } = 0.01;
        public double BarCategoryGap { get; set; } = 0.2;
        public string? Stack { get; set; }
        public BarItemStyleConfig? ItemStyle { get; set; }
    }

    public class ScatterSeriesConfig : SeriesConfig
    {
        public CartesianSeriesData? Data { get; set; }
        public string Mode { get; set; } = "points"; // "points" | "density"
        public double BinSize { get; set; } = 4.0;
        public DensityColormap? DensityColormap { get; set; }
        public string DensityNormalization { get; set; } = "linear"; // "linear" | "sqrt" | "log"
        public double? SymbolSizeConstant { get; set; }
        public Func<DataPoint, double>? SymbolSizeFunction { get; set; }
        public string Symbol { get; set; } = "circle"; // "circle" | "rect" | "triangle"
    }

    public class PieSeriesConfig : SeriesConfig
    {
        public PieRadius? Radius { get; set; }
        public PieCenter? Center { get; set; }
        public double StartAngle { get; set; } = 90.0;
        public IReadOnlyList<PieDataItem> Data { get; set; } = Array.Empty<PieDataItem>();
        public PieItemStyleConfig? ItemStyle { get; set; }
    }

    public class CandlestickSeriesConfig : SeriesConfig
    {
        public IReadOnlyList<OHLCDataPoint> Data { get; set; } = Array.Empty<OHLCDataPoint>();
        public string Style { get; set; } = "classic"; // "classic" | "hollow"
        public CandlestickItemStyleConfig? ItemStyle { get; set; }
        public PercentOrReal? BarWidth { get; set; }
        public double BarMinWidth { get; set; } = 1.0;
        public double? BarMaxWidth { get; set; }
    }

    /// <summary>
    /// Parameters passed to the tooltip formatter triggers.
    /// </summary>
    public class TooltipParams
    {
        public string SeriesName { get; set; } = string.Empty;
        public int SeriesIndex { get; set; }
        public int DataIndex { get; set; }
        public double[] Value { get; set; } = Array.Empty<double>();
        public string Color { get; set; } = string.Empty;
    }

    public class TooltipConfig
    {
        public bool Show { get; set; } = true;
        public string Trigger { get; set; } = "item"; // "item" | "axis"
        public Func<TooltipParams, string>? ItemFormatter { get; set; }
        public Func<IReadOnlyList<TooltipParams>, string>? AxisFormatter { get; set; }
    }

    public class AnimationConfig
    {
        public double Duration { get; set; } = 300.0; // in milliseconds
        public string Easing { get; set; } = "cubicOut"; // "linear" | "cubicOut" | "cubicInOut" | "bounceOut"
        public double Delay { get; set; } = 0.0; // in milliseconds
    }

    public class LegendConfig
    {
        public bool Show { get; set; } = true;
        public string Position { get; set; } = "top"; // "top" | "bottom" | "left" | "right"
    }

    public class AnnotationStyle
    {
        public string? Color { get; set; }
        public double LineWidth { get; set; } = 1.0;
        public double[]? LineDash { get; set; }
        public double Opacity { get; set; } = 1.0;
    }

    public class AnnotationLabelBackground
    {
        public string? Color { get; set; }
        public double Opacity { get; set; } = 0.8;
        public double[]? Padding { get; set; } // [top, right, bottom, left]
        public double BorderRadius { get; set; } = 2.0;
    }

    public class AnnotationLabel
    {
        public string? Text { get; set; }
        public string? Template { get; set; }
        public int Decimals { get; set; } = 2;
        public double[] Offset { get; set; } = new double[2] { 0.0, 0.0 }; // [dx, dy]
        public string Anchor { get; set; } = "center"; // "start" | "center" | "end"
        public AnnotationLabelBackground? Background { get; set; }
    }

    public struct AnnotationPosition
    {
        public string Space { get; set; } // "data" | "plot"
        public double X { get; set; }
        public double Y { get; set; }

        public AnnotationPosition(string space, double x, double y)
        {
            Space = space;
            X = x;
            Y = y;
        }
    }

    public abstract class AnnotationConfig
    {
        public string? Id { get; set; }
        public string Layer { get; set; } = "aboveSeries"; // "belowSeries" | "aboveSeries"
        public AnnotationStyle? Style { get; set; }
        public AnnotationLabel? Label { get; set; }
    }

    public class AnnotationLineX : AnnotationConfig
    {
        public double X { get; set; }
        public double[]? YRange { get; set; } // [minY, maxY]
    }

    public class AnnotationLineY : AnnotationConfig
    {
        public double Y { get; set; }
        public double[]? XRange { get; set; } // [minX, maxX]
    }

    public class AnnotationPointMarker
    {
        public string Symbol { get; set; } = "circle"; // "circle" | "rect" | "triangle"
        public double Size { get; set; } = 6.0;
        public AnnotationStyle? Style { get; set; }
    }

    public class AnnotationPoint : AnnotationConfig
    {
        public double X { get; set; }
        public double Y { get; set; }
        public AnnotationPointMarker? Marker { get; set; }
    }

    public class AnnotationText : AnnotationConfig
    {
        public AnnotationPosition Position { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class GridLinesDirectionConfig
    {
        public bool Show { get; set; } = true;
        public int? Count { get; set; }
        public string? Color { get; set; }
    }

    public class GridLinesConfig
    {
        public bool Show { get; set; } = true;
        public string? Color { get; set; }
        public double Opacity { get; set; } = 1.0;
        public GridLinesDirectionConfig Horizontal { get; set; } = new GridLinesDirectionConfig();
        public GridLinesDirectionConfig Vertical { get; set; } = new GridLinesDirectionConfig();
    }

    /// <summary>
    /// Top-level option bag representing standard ChartGPU features.
    /// Provides premium C# definitions of configuration options for direct rendering integration.
    /// </summary>
    public class ChartGPUOptions
    {
        public List<AxisConfig>? YAxes { get; set; }
        public GridConfig? Grid { get; set; }
        public GridLinesConfig GridLines { get; set; } = new GridLinesConfig();
        public AxisConfig? XAxis { get; set; }
        public AxisConfig? YAxis { get; set; }
        public List<DataZoomConfig>? DataZoom { get; set; }
        public List<SeriesConfig>? Series { get; set; }
        public List<AnnotationConfig>? Annotations { get; set; }
        public bool AutoScroll { get; set; } = false;
        public string Theme { get; set; } = "dark"; // "dark" | "light"
        public IReadOnlyList<string>? Palette { get; set; }
        public TooltipConfig Tooltip { get; set; } = new TooltipConfig();
        public LegendConfig Legend { get; set; } = new LegendConfig();
        public AnimationConfig? Animation { get; set; }
        public string RenderMode { get; set; } = "auto"; // "auto" | "external"
    }
}
