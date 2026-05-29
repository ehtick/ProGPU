using System;
using System.Collections.Generic;

namespace ProGPU.WinUI.Charts
{
    /// <summary>
    /// Interface representing general interpolation scales.
    /// </summary>
    public interface IChartScale
    {
        double Scale(double value);
        double Invert(double pixel);
    }

    /// <summary>
    /// Maps a numeric domain (data space) to a numeric range (pixel/screen space).
    /// Defaults to identity mapping [0, 1] -> [0, 1].
    /// </summary>
    public class LinearScale : IChartScale
    {
        public double DomainMin { get; set; } = 0.0;
        public double DomainMax { get; set; } = 1.0;
        public double RangeMin { get; set; } = 0.0;
        public double RangeMax { get; set; } = 1.0;

        /// <summary>
        /// Fluent setter for the domain bounds.
        /// </summary>
        public LinearScale SetDomain(double min, double max)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max))
            {
                throw new ArgumentException($"Domain bounds must be finite. Received min={min}, max={max}");
            }
            DomainMin = min;
            DomainMax = max;
            return this;
        }

        /// <summary>
        /// Fluent setter for the range/pixel bounds.
        /// </summary>
        public LinearScale SetRange(double min, double max)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max))
            {
                throw new ArgumentException($"Range bounds must be finite. Received min={min}, max={max}");
            }
            RangeMin = min;
            RangeMax = max;
            return this;
        }

        /// <summary>
        /// Maps a domain (data) value to a range (pixel) value. No clamping.
        /// If domain min equals max, maps to the range midpoint.
        /// </summary>
        public double Scale(double value)
        {
            if (!double.IsFinite(value)) return double.NaN;

            if (DomainMin == DomainMax)
            {
                return (RangeMin + RangeMax) / 2.0;
            }

            double t = (value - DomainMin) / (DomainMax - DomainMin);
            return RangeMin + t * (RangeMax - RangeMin);
        }

        /// <summary>
        /// Inverts a range (pixel) value back to a domain (data) value. No clamping.
        /// If domain min equals max, returns domain min.
        /// </summary>
        public double Invert(double pixel)
        {
            if (!double.IsFinite(pixel)) return double.NaN;

            if (DomainMin == DomainMax)
            {
                return DomainMin;
            }

            if (RangeMin == RangeMax)
            {
                return (DomainMin + DomainMax) / 2.0;
            }

            double t = (pixel - RangeMin) / (RangeMax - RangeMin);
            return DomainMin + t * (DomainMax - DomainMin);
        }
    }

    /// <summary>
    /// Maps category names (strings) to evenly spaced centers across a screen range.
    /// </summary>
    public class CategoryScale
    {
        private readonly List<string> _categories = new List<string>();
        private readonly Dictionary<string, int> _indexByCategory = new Dictionary<string, int>(StringComparer.Ordinal);

        public double RangeMin { get; set; } = 0.0;
        public double RangeMax { get; set; } = 1.0;

        public IReadOnlyList<string> Categories => _categories;

        /// <summary>
        /// Fluent setter for unique categorical domain items.
        /// </summary>
        public CategoryScale SetDomain(IEnumerable<string> nextCategories)
        {
            _categories.Clear();
            _indexByCategory.Clear();
            int index = 0;
            foreach (var category in nextCategories)
            {
                if (_indexByCategory.ContainsKey(category))
                {
                    throw new ArgumentException($"Category domain must not contain duplicates. Duplicate: \"{category}\"");
                }
                _categories.Add(category);
                _indexByCategory[category] = index++;
            }
            return this;
        }

        /// <summary>
        /// Fluent setter for range boundaries.
        /// </summary>
        public CategoryScale SetRange(double min, double max)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max))
            {
                throw new ArgumentException($"Range bounds must be finite. Received min={min}, max={max}");
            }
            RangeMin = min;
            RangeMax = max;
            return this;
        }

        public int GetCategoryIndex(string category)
        {
            if (_indexByCategory.TryGetValue(category, out int idx))
            {
                return idx;
            }
            return -1;
        }

        public double Bandwidth()
        {
            int n = _categories.Count;
            if (n == 0) return 0.0;
            return Math.Abs((RangeMax - RangeMin) / n);
        }

        /// <summary>
        /// Returns the center x-position for a category.
        /// </summary>
        public double Scale(string category)
        {
            int n = _categories.Count;
            if (n == 0)
            {
                return (RangeMin + RangeMax) / 2.0;
            }

            int i = GetCategoryIndex(category);
            if (i < 0) return double.NaN;

            double step = (RangeMax - RangeMin) / n;
            return RangeMin + (i + 0.5) * step;
        }
    }

    /// <summary>
    /// Implements Retina-grade Subpixel Snapping as required by our rendering engine rules.
    /// Prevents linear stretch blurring on high-DPI screens.
    /// </summary>
    public static class DpiSnapper
    {
        /// <summary>
        /// Snaps a position coordinate to 1/4th of a physical pixel, then returns it to logical coordinate space.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static double Snap(double value, double dpiScale)
        {
            if (dpiScale <= 0.0) return value;
            double physical = value * dpiScale;
            double snapped = Math.Round(physical * 4.0) / 4.0;
            return snapped / dpiScale;
        }

        /// <summary>
        /// Snaps a float coordinate to 1/4th of a physical pixel, then returns it to logical coordinate space.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static float Snap(float value, float dpiScale)
        {
            if (dpiScale <= 0.0f) return value;
            float physical = value * dpiScale;
            float snapped = MathF.Round(physical * 4.0f) / 4.0f;
            return snapped / dpiScale;
        }
    }
}
