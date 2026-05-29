using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProGPU.WinUI.Charts
{
    /// <summary>
    /// Represents a single 2D cartesian data point, with an optional size component for bubble/scatter plots.
    /// </summary>
    public struct DataPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double? Size { get; set; }

        public DataPoint(double x, double y, double? size = null)
        {
            X = x;
            Y = y;
            Size = size;
        }

        public override string ToString()
        {
            return Size.HasValue ? $"({X}, {Y}, Size={Size.Value})" : $"({X}, {Y})";
        }
    }

    /// <summary>
    /// Represents an Open-High-Low-Close (OHLC) data point for candlestick charts.
    /// Matches the standard ECharts convention: [timestamp, open, close, low, high].
    /// </summary>
    public struct OHLCDataPoint
    {
        public double Timestamp { get; set; }
        public double Open { get; set; }
        public double Close { get; set; }
        public double Low { get; set; }
        public double High { get; set; }

        public OHLCDataPoint(double timestamp, double open, double close, double low, double high)
        {
            Timestamp = timestamp;
            Open = open;
            Close = close;
            Low = low;
            High = high;
        }

        public override string ToString()
        {
            return $"T={Timestamp}: [O={Open}, C={Close}, L={Low}, H={High}]";
        }
    }

    /// <summary>
    /// Represents a single slice in a pie or donut chart.
    /// </summary>
    public class PieDataItem
    {
        public double Value { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public bool Visible { get; set; } = true;

        public override string ToString()
        {
            return $"{Name}: {Value} (Visible={Visible})";
        }
    }

    /// <summary>
    /// Parallel arrays/lists representation for CartesianSeriesData.
    /// Extremely helpful for avoiding object allocation overheads.
    /// </summary>
    public class XYArraysData
    {
        public IReadOnlyList<double> X { get; set; } = Array.Empty<double>();
        public IReadOnlyList<double> Y { get; set; } = Array.Empty<double>();
        public IReadOnlyList<double>? Size { get; set; }
    }

    /// <summary>
    /// Represents Cartesian min/max boundaries for both axes.
    /// </summary>
    public struct ChartBounds
    {
        public double XMin { get; set; }
        public double XMax { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }

        public ChartBounds(double xMin, double xMax, double yMin, double yMax)
        {
            XMin = xMin;
            XMax = xMax;
            YMin = yMin;
            YMax = yMax;
        }
    }

    /// <summary>
    /// Data format for CartesianSeriesData.
    /// </summary>
    public enum CartesianDataFormat
    {
        PointsList,
        XYArrays,
        Interleaved
    }

    /// <summary>
    /// Unified, allocation-minimizing wrapper for Cartesian series data supporting:
    /// - Array or IReadOnlyList of nullable/non-nullable DataPoint objects/structs
    /// - Parallel X, Y and optional Size arrays (XYArraysData)
    /// - Pre-interleaved float or double arrays [x0, y0, x1, y1, ...]
    /// </summary>
    public class CartesianSeriesData
    {
        public CartesianDataFormat Format { get; private set; }
        public IReadOnlyList<DataPoint?>? Points { get; private set; }
        public XYArraysData? XYArrays { get; private set; }
        public float[]? Interleaved { get; private set; }

        public int Version { get; private set; }

        public void IncrementVersion()
        {
            Version++;
        }

        public CartesianSeriesData(IReadOnlyList<DataPoint?> points)
        {
            Format = CartesianDataFormat.PointsList;
            Points = points;
        }

        public CartesianSeriesData(XYArraysData xyArrays)
        {
            Format = CartesianDataFormat.XYArrays;
            XYArrays = xyArrays;
        }

        public CartesianSeriesData(float[] interleaved)
        {
            Format = CartesianDataFormat.Interleaved;
            Interleaved = interleaved;
        }

        public void AppendRange(IEnumerable<DataPoint?> range)
        {
            if (Format == CartesianDataFormat.PointsList)
            {
                if (Points is List<DataPoint?> list)
                {
                    list.AddRange(range);
                }
                else if (Points != null)
                {
                    var newList = new List<DataPoint?>(Points);
                    newList.AddRange(range);
                    Points = newList;
                }
                else
                {
                    Points = new List<DataPoint?>(range);
                }
            }
            else if (Format == CartesianDataFormat.Interleaved)
            {
                var list = new List<DataPoint?>(range);
                int prevLen = Interleaved?.Length ?? 0;
                var newArray = new float[prevLen + list.Count * 2];
                if (Interleaved != null)
                {
                    Array.Copy(Interleaved, newArray, prevLen);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    newArray[prevLen + i * 2] = p == null ? float.NaN : (float)p.Value.X;
                    newArray[prevLen + i * 2 + 1] = p == null ? float.NaN : (float)p.Value.Y;
                }
                Interleaved = newArray;
            }
            IncrementVersion();
        }

        public int PointCount
        {
            get
            {
                return Format switch
                {
                    CartesianDataFormat.PointsList => Points?.Count ?? 0,
                    CartesianDataFormat.XYArrays => XYArrays == null ? 0 : Math.Min(XYArrays.X.Count, XYArrays.Y.Count),
                    CartesianDataFormat.Interleaved => Interleaved == null ? 0 : Interleaved.Length / 2,
                    _ => 0
                };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetX(int index)
        {
            return Format switch
            {
                CartesianDataFormat.PointsList => Points?[index]?.X ?? double.NaN,
                CartesianDataFormat.XYArrays => XYArrays!.X[index],
                CartesianDataFormat.Interleaved => Interleaved![index * 2],
                _ => double.NaN
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetY(int index)
        {
            return Format switch
            {
                CartesianDataFormat.PointsList => Points?[index]?.Y ?? double.NaN,
                CartesianDataFormat.XYArrays => XYArrays!.Y[index],
                CartesianDataFormat.Interleaved => Interleaved![index * 2 + 1],
                _ => double.NaN
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double? GetSize(int index)
        {
            return Format switch
            {
                CartesianDataFormat.PointsList => Points?[index]?.Size,
                CartesianDataFormat.XYArrays => XYArrays!.Size?[index],
                CartesianDataFormat.Interleaved => null,
                _ => null
            };
        }

        /// <summary>
        /// Packs XY coordinates from CartesianSeriesData into a float array in interleaved layout.
        /// Subtracts xOffset to preserve Float32 precision when charting huge dimensions (e.g. timestamps).
        /// </summary>
        public void PackXYInto(float[] dest, int destOffset, int srcOffset, int count, double xOffset)
        {
            int available = PointCount - srcOffset;
            int actualCount = Math.Min(count, available);

            if (actualCount <= 0) return;

            int requiredLength = destOffset + actualCount * 2;
            if (requiredLength > dest.Length)
            {
                throw new ArgumentException($"packXYInto: output buffer too small (need {requiredLength} floats, have {dest.Length})");
            }

            if (Format == CartesianDataFormat.XYArrays && XYArrays != null)
            {
                for (int i = 0; i < actualCount; i++)
                {
                    int srcIdx = srcOffset + i;
                    int outIdx = destOffset + i * 2;
                    dest[outIdx] = (float)(XYArrays.X[srcIdx] - xOffset);
                    dest[outIdx + 1] = (float)XYArrays.Y[srcIdx];
                }
            }
            else if (Format == CartesianDataFormat.Interleaved && Interleaved != null)
            {
                for (int i = 0; i < actualCount; i++)
                {
                    int srcIdx = (srcOffset + i) * 2;
                    int outIdx = destOffset + i * 2;
                    dest[outIdx] = (float)(Interleaved[srcIdx] - xOffset);
                    dest[outIdx + 1] = Interleaved[srcIdx + 1];
                }
            }
            else if (Format == CartesianDataFormat.PointsList && Points != null)
            {
                for (int i = 0; i < actualCount; i++)
                {
                    int srcIdx = srcOffset + i;
                    int outIdx = destOffset + i * 2;
                    var p = Points[srcIdx];
                    if (p == null)
                    {
                        dest[outIdx] = float.NaN;
                        dest[outIdx + 1] = float.NaN;
                    }
                    else
                    {
                        dest[outIdx] = (float)(p.Value.X - xOffset);
                        dest[outIdx + 1] = (float)p.Value.Y;
                    }
                }
            }
        }

        /// <summary>
        /// Computes the xMin, xMax, yMin, yMax boundaries.
        /// Ensures min != max by expanding max by +1 to guarantee clean scale derivatives.
        /// </summary>
        public ChartBounds? ComputeRawBounds()
        {
            double xMin = double.PositiveInfinity;
            double xMax = double.NegativeInfinity;
            double yMin = double.PositiveInfinity;
            double yMax = double.NegativeInfinity;

            int count = PointCount;
            if (count == 0) return null;

            for (int i = 0; i < count; i++)
            {
                double x = GetX(i);
                double y = GetY(i);

                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;

                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
            }

            if (!double.IsFinite(xMin) || !double.IsFinite(xMax) ||
                !double.IsFinite(yMin) || !double.IsFinite(yMax))
            {
                return null;
            }

            // Expand when empty span to avoid divide-by-zero scales
            if (xMin == xMax) xMax = xMin + 1.0;
            if (yMin == yMax) yMax = yMin + 1.0;

            return new ChartBounds(xMin, xMax, yMin, yMax);
        }

        public bool HasNullGaps()
        {
            if (Format != CartesianDataFormat.PointsList || Points == null) return false;
            foreach (var p in Points)
            {
                if (p == null) return true;
            }
            return false;
        }

        /// <summary>
        /// Removes null or NaN entries to create a contiguous stream.
        /// </summary>
        public List<DataPoint> FilterGaps()
        {
            var result = new List<DataPoint>(PointCount);
            int count = PointCount;
            for (int i = 0; i < count; i++)
            {
                double x = GetX(i);
                double y = GetY(i);
                if (double.IsFinite(x) && double.IsFinite(y))
                {
                    result.Add(new DataPoint(x, y, GetSize(i)));
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Geometric-growing columnar datastore backing one or more series.
    /// Reuses buffers during append streams, completely eliminating GC allocations in updates.
    /// </summary>
    public class ChartSeriesStore
    {
        public float[] StagingBuffer { get; private set; } = Array.Empty<float>();
        public int PointCount { get; private set; }
        public double XOffset { get; set; }

        public void SetSeries(CartesianSeriesData data, double xOffset = 0.0)
        {
            XOffset = xOffset;
            int count = data.PointCount;
            EnsureCapacity(count);
            PointCount = count;
            if (count > 0)
            {
                data.PackXYInto(StagingBuffer, 0, 0, count, XOffset);
            }
        }

        public void AppendSeries(CartesianSeriesData appendData)
        {
            int appendCount = appendData.PointCount;
            if (appendCount == 0) return;

            int prevCount = PointCount;
            int nextCount = prevCount + appendCount;
            EnsureCapacity(nextCount);

            appendData.PackXYInto(StagingBuffer, prevCount * 2, 0, appendCount, XOffset);
            PointCount = nextCount;
        }

        public void Clear()
        {
            PointCount = 0;
        }

        private void EnsureCapacity(int requiredPoints)
        {
            int requiredFloats = requiredPoints * 2;
            if (StagingBuffer.Length < requiredFloats)
            {
                int newCapacity = Math.Max(128, StagingBuffer.Length);
                while (newCapacity < requiredFloats)
                {
                    newCapacity *= 2; // Power of two growth policy
                }
                var newBuffer = new float[newCapacity];
                if (PointCount > 0)
                {
                    Array.Copy(StagingBuffer, newBuffer, PointCount * 2);
                }
                StagingBuffer = newBuffer;
            }
        }
    }
}
