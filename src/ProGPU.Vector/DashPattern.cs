using System;
using System.Numerics;

#nullable enable
#pragma warning disable IDE0057, IDE0059, IDE0078, IDE0300, IDE0301, IDE0305

namespace ProGPU.Vector;

#if PROGPU_VECTOR_INTERNAL
internal
#else
public
#endif
readonly struct LineDashSegment
{
    public LineDashSegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }

    public Vector2 Start { get; }
    public Vector2 End { get; }
}

#if PROGPU_VECTOR_INTERNAL
internal
#else
public
#endif
readonly struct DashPattern
{
    private const float Epsilon = 0.0001f;

    private readonly float[] _intervals;

    private DashPattern(float[] intervals, int initialIndex, float initialDistance)
    {
        _intervals = intervals;
        InitialIndex = initialIndex;
        InitialDistance = initialDistance;
    }

    public ReadOnlySpan<float> Intervals => _intervals;

    public int InitialIndex { get; }

    public float InitialDistance { get; }

    public static bool TryCreate(
        ReadOnlySpan<double> dashArray,
        double dashOffset,
        double strokeThickness,
        out DashPattern pattern)
    {
        pattern = default;
        if (dashArray.IsEmpty || !double.IsFinite(strokeThickness) || strokeThickness <= 0.0)
        {
            return false;
        }

        var intervalCount = dashArray.Length % 2 == 0 ? dashArray.Length : dashArray.Length * 2;
        var intervals = new float[intervalCount];
        var patternLength = 0.0f;
        for (var i = 0; i < intervals.Length; i++)
        {
            var interval = dashArray[i % dashArray.Length] * strokeThickness;
            if (!double.IsFinite(interval) || interval < 0.0)
            {
                return false;
            }

            if (interval <= Epsilon)
            {
                if ((i % 2) != 0)
                {
                    return false;
                }

                interval = strokeThickness;
            }

            intervals[i] = (float)interval;
            patternLength += intervals[i];
        }

        if (patternLength <= Epsilon)
        {
            return false;
        }

        var initialIndex = 0;
        var initialDistance = PositiveModulo((float)(dashOffset * strokeThickness), patternLength);
        while (initialDistance >= intervals[initialIndex])
        {
            initialDistance -= intervals[initialIndex];
            initialIndex = (initialIndex + 1) % intervals.Length;
        }

        pattern = new DashPattern(intervals, initialIndex, initialDistance);
        return true;
    }

    public bool TryCreateLineSegments(
        Vector2 start,
        Vector2 end,
        int patternIndex,
        float distanceInPattern,
        out LineDashSegment[] dashSegments,
        out int finalPatternIndex,
        out float finalDistanceInPattern)
    {
        dashSegments = Array.Empty<LineDashSegment>();
        finalPatternIndex = patternIndex;
        finalDistanceInPattern = distanceInPattern;

        if (!TryValidateState(Intervals, patternIndex, distanceInPattern))
        {
            return false;
        }

        NormalizeState(Intervals, ref patternIndex, ref distanceInPattern);

        var delta = end - start;
        var length = delta.Length();
        if (length <= Epsilon)
        {
            return false;
        }

        var normalizedPatternIndex = patternIndex;
        var normalizedDistanceInPattern = distanceInPattern;
        var segmentCount = CountLineSegments(
            Intervals,
            ref patternIndex,
            ref distanceInPattern,
            length);
        if (segmentCount == 0)
        {
            finalPatternIndex = patternIndex;
            finalDistanceInPattern = distanceInPattern;
            return true;
        }

        dashSegments = new LineDashSegment[segmentCount];
        FillLineSegments(
            Intervals,
            start,
            end,
            length,
            normalizedPatternIndex,
            normalizedDistanceInPattern,
            dashSegments);
        finalPatternIndex = patternIndex;
        finalDistanceInPattern = distanceInPattern;
        return true;
    }

    private static int CountLineSegments(
        ReadOnlySpan<float> intervals,
        ref int patternIndex,
        ref float distanceInPattern,
        float length)
    {
        var count = 0;
        var distance = 0.0f;
        while (distance < length - Epsilon)
        {
            var remainingInElement = intervals[patternIndex] - distanceInPattern;
            var step = MathF.Min(remainingInElement, length - distance);
            if ((patternIndex % 2) == 0 && step > Epsilon)
            {
                count++;
            }

            Advance(intervals, ref patternIndex, ref distanceInPattern, remainingInElement, step);
            distance += step;
        }

        return count;
    }

    private static void FillLineSegments(
        ReadOnlySpan<float> intervals,
        Vector2 start,
        Vector2 end,
        float length,
        int patternIndex,
        float distanceInPattern,
        LineDashSegment[] dashSegments)
    {
        var direction = (end - start) / length;
        var distance = 0.0f;
        var segmentIndex = 0;
        while (distance < length - Epsilon)
        {
            var remainingInElement = intervals[patternIndex] - distanceInPattern;
            var step = MathF.Min(remainingInElement, length - distance);
            if ((patternIndex % 2) == 0 && step > Epsilon)
            {
                dashSegments[segmentIndex++] = new LineDashSegment(
                    start + direction * distance,
                    start + direction * (distance + step));
            }

            Advance(intervals, ref patternIndex, ref distanceInPattern, remainingInElement, step);
            distance += step;
        }
    }

    public static bool TryValidateState(
        ReadOnlySpan<float> intervals,
        int patternIndex,
        float distanceInPattern)
    {
        if (intervals.IsEmpty ||
            patternIndex < 0 ||
            patternIndex >= intervals.Length ||
            !float.IsFinite(distanceInPattern) ||
            distanceInPattern < 0.0f)
        {
            return false;
        }

        for (var i = 0; i < intervals.Length; i++)
        {
            if (!float.IsFinite(intervals[i]) || intervals[i] <= Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    public static void NormalizeState(
        ReadOnlySpan<float> intervals,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        while (distanceInPattern >= intervals[patternIndex])
        {
            distanceInPattern -= intervals[patternIndex];
            patternIndex = (patternIndex + 1) % intervals.Length;
        }
    }

    public static void Advance(
        ReadOnlySpan<float> intervals,
        ref int patternIndex,
        ref float distanceInPattern,
        float remainingInElement,
        float step)
    {
        if (step >= remainingInElement - Epsilon)
        {
            distanceInPattern = 0.0f;
            patternIndex = (patternIndex + 1) % intervals.Length;
        }
        else
        {
            distanceInPattern += step;
        }
    }

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0.0f ? result + modulus : result;
    }
}
