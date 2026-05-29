using System;
using System.Globalization;
using System.Numerics;

namespace ProGPU.WinUI.Charts
{
    public static class ChartUtils
    {
        public static double ParsePercent(PercentOrReal percentVal, double basis)
        {
            if (percentVal.IsPercent)
            {
                string s = percentVal.PercentValue!.Trim();
                if (s.EndsWith("%"))
                {
                    double pct = double.Parse(s.Substring(0, s.Length - 1), CultureInfo.InvariantCulture);
                    return (pct / 100.0) * basis;
                }
            }
            return percentVal.RealValue ?? 0.0;
        }

        public static Vector4 ParseCssColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return new Vector4(1f, 1f, 1f, 1f);

            color = color.Trim();
            if (color.StartsWith("#"))
            {
                string hex = color.Substring(1);
                if (hex.Length == 3)
                {
                    float r = Convert.ToInt32(new string(hex[0], 2), 16) / 255f;
                    float g = Convert.ToInt32(new string(hex[1], 2), 16) / 255f;
                    float b = Convert.ToInt32(new string(hex[2], 2), 16) / 255f;
                    return new Vector4(r, g, b, 1f);
                }
                if (hex.Length == 6)
                {
                    float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                    float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                    float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                    return new Vector4(r, g, b, 1f);
                }
                if (hex.Length == 8)
                {
                    float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
                    float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
                    float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
                    float a = Convert.ToInt32(hex.Substring(6, 2), 16) / 255f;
                    return new Vector4(r, g, b, a);
                }
            }

            // Quick named fallbacks
            if (color.Equals("red", StringComparison.OrdinalIgnoreCase)) return new Vector4(1f, 0f, 0f, 1f);
            if (color.Equals("green", StringComparison.OrdinalIgnoreCase)) return new Vector4(0f, 1f, 0f, 1f);
            if (color.Equals("blue", StringComparison.OrdinalIgnoreCase)) return new Vector4(0f, 0f, 1f, 1f);

            return new Vector4(1f, 1f, 1f, 1f); // Default White
        }
    }
}
