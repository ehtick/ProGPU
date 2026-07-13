using System;
using System.Numerics;
using ProGPU.Scene;

namespace SkiaSharp;

public enum SKLatticeRectType
{
    Default,
    Transparent,
    FixedColor
}

public struct SKLattice : IEquatable<SKLattice>
{
    public int[]? XDivs { get; set; }
    public int[]? YDivs { get; set; }
    public SKLatticeRectType[]? RectTypes { get; set; }
    public SKRectI? Bounds { get; set; }
    public SKColor[]? Colors { get; set; }

    public readonly bool Equals(SKLattice obj) =>
        XDivs == obj.XDivs &&
        YDivs == obj.YDivs &&
        RectTypes == obj.RectTypes &&
        Bounds == obj.Bounds &&
        Colors == obj.Colors;

    public override readonly bool Equals(object? obj) => obj is SKLattice lattice && Equals(lattice);

    public static bool operator ==(SKLattice left, SKLattice right) => left.Equals(right);

    public static bool operator !=(SKLattice left, SKLattice right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(XDivs);
        hash.Add(YDivs);
        hash.Add(RectTypes);
        hash.Add(Bounds);
        hash.Add(Colors);
        return hash.ToHashCode();
    }
}

internal static class SKLatticeLayout
{
    internal static bool TryCreateLattice(
        int imageWidth,
        int imageHeight,
        in SKLattice lattice,
        SKRect destination,
        SKColorFilter? colorFilter,
        out TexturePatch[] patches)
    {
        patches = Array.Empty<TexturePatch>();
        var xDivs = lattice.XDivs;
        var yDivs = lattice.YDivs;
        if (xDivs == null || yDivs == null)
        {
            return false;
        }

        var bounds = lattice.Bounds ?? SKRectI.Create(imageWidth, imageHeight);
        if (!ContainsImageBounds(imageWidth, imageHeight, bounds) ||
            !ValidDivs(xDivs, bounds.Left, bounds.Right) ||
            !ValidDivs(yDivs, bounds.Top, bounds.Bottom))
        {
            return false;
        }

        var zeroXDivs = xDivs.Length == 0 ||
            (xDivs.Length == 1 && xDivs[0] == bounds.Left);
        var zeroYDivs = yDivs.Length == 0 ||
            (yDivs.Length == 1 && yDivs[0] == bounds.Top);
        if (zeroXDivs && zeroYDivs)
        {
            return false;
        }

        BuildDimension(
            xDivs,
            bounds.Left,
            bounds.Right,
            destination.Left,
            destination.Right,
            out var sourceX,
            out var destinationX,
            out var skippedFirstColumn);
        BuildDimension(
            yDivs,
            bounds.Top,
            bounds.Bottom,
            destination.Top,
            destination.Bottom,
            out var sourceY,
            out var destinationY,
            out var skippedFirstRow);

        patches = CreatePatches(
            sourceX,
            sourceY,
            destinationX,
            destinationY,
            lattice.RectTypes,
            lattice.Colors,
            xDivs.Length + 1,
            skippedFirstColumn,
            skippedFirstRow,
            colorFilter);
        return true;
    }

    internal static bool TryCreateNinePatch(
        int imageWidth,
        int imageHeight,
        SKRectI center,
        SKRect destination,
        out TexturePatch[] patches)
    {
        patches = Array.Empty<TexturePatch>();
        if (center.Left < 0 ||
            center.Top < 0 ||
            center.Right > imageWidth ||
            center.Bottom > imageHeight ||
            center.Left >= center.Right ||
            center.Top >= center.Bottom)
        {
            return false;
        }

        var sourceX = new[] { 0, center.Left, center.Right, imageWidth };
        var sourceY = new[] { 0, center.Top, center.Bottom, imageHeight };
        var destinationX = BuildNinePatchDestination(
            imageWidth,
            center.Left,
            center.Right,
            destination.Left,
            destination.Right);
        var destinationY = BuildNinePatchDestination(
            imageHeight,
            center.Top,
            center.Bottom,
            destination.Top,
            destination.Bottom);

        patches = CreatePatches(
            sourceX,
            sourceY,
            destinationX,
            destinationY,
            rectTypes: null,
            colors: null,
            originalColumnCount: 3,
            skippedFirstColumn: false,
            skippedFirstRow: false,
            colorFilter: null);
        return true;
    }

    private static bool ContainsImageBounds(int imageWidth, int imageHeight, SKRectI bounds) =>
        bounds.Left >= 0 &&
        bounds.Top >= 0 &&
        bounds.Right <= imageWidth &&
        bounds.Bottom <= imageHeight &&
        bounds.Left <= bounds.Right &&
        bounds.Top <= bounds.Bottom;

    private static bool ValidDivs(int[] divs, int start, int end)
    {
        var previous = start - 1;
        for (var i = 0; i < divs.Length; i++)
        {
            if (previous >= divs[i] || divs[i] >= end)
            {
                return false;
            }

            previous = divs[i];
        }

        return true;
    }

    private static void BuildDimension(
        int[] originalDivs,
        int sourceStart,
        int sourceEnd,
        float destinationStart,
        float destinationEnd,
        out int[] source,
        out float[] destination,
        out bool skippedFirstPatch)
    {
        skippedFirstPatch = originalDivs.Length > 0 && originalDivs[0] == sourceStart;
        var divOffset = skippedFirstPatch ? 1 : 0;
        var divCount = originalDivs.Length - divOffset;
        var firstIsScalable = skippedFirstPatch;

        var scalablePixels = CountScalablePixels(
            originalDivs,
            divOffset,
            divCount,
            firstIsScalable,
            sourceStart,
            sourceEnd);
        var fixedPixels = sourceEnd - sourceStart - scalablePixels;
        source = new int[divCount + 2];
        destination = new float[divCount + 2];

        var destinationLength = destinationEnd - destinationStart;
        var normalScale = fixedPixels <= destinationLength;
        var scale = normalScale
            ? scalablePixels == 0
                ? 0f
                : (destinationLength - fixedPixels) / scalablePixels
            : fixedPixels == 0
                ? 0f
                : destinationLength / fixedPixels;

        source[0] = sourceStart;
        destination[0] = destinationStart;
        var isScalable = firstIsScalable;
        for (var i = 0; i < divCount; i++)
        {
            source[i + 1] = originalDivs[divOffset + i];
            var sourceDelta = source[i + 1] - source[i];
            var destinationDelta = normalScale
                ? isScalable ? scale * sourceDelta : sourceDelta
                : isScalable ? 0f : scale * sourceDelta;
            destination[i + 1] = destination[i] + destinationDelta;
            isScalable = !isScalable;
        }

        source[divCount + 1] = sourceEnd;
        destination[divCount + 1] = destinationEnd;
    }

    private static int CountScalablePixels(
        int[] divs,
        int offset,
        int count,
        bool firstIsScalable,
        int start,
        int end)
    {
        if (count == 0)
        {
            return firstIsScalable ? end - start : 0;
        }

        var scalablePixels = firstIsScalable ? divs[offset] - start : 0;
        var index = firstIsScalable ? 1 : 0;
        for (; index < count; index += 2)
        {
            var left = divs[offset + index];
            var right = index + 1 < count ? divs[offset + index + 1] : end;
            scalablePixels += right - left;
        }

        return scalablePixels;
    }

    private static float[] BuildNinePatchDestination(
        int sourceLength,
        int centerStart,
        int centerEnd,
        float destinationStart,
        float destinationEnd)
    {
        var destination = new float[4];
        destination[0] = destinationStart;
        destination[1] = destinationStart + centerStart;
        destination[2] = destinationEnd - (sourceLength - centerEnd);
        destination[3] = destinationEnd;
        if (destination[1] > destination[2])
        {
            destination[1] = destinationStart +
                (destinationEnd - destinationStart) * centerStart /
                (sourceLength - (centerEnd - centerStart));
            destination[2] = destination[1];
        }

        return destination;
    }

    private static TexturePatch[] CreatePatches(
        int[] sourceX,
        int[] sourceY,
        float[] destinationX,
        float[] destinationY,
        SKLatticeRectType[]? rectTypes,
        SKColor[]? colors,
        int originalColumnCount,
        bool skippedFirstColumn,
        bool skippedFirstRow,
        SKColorFilter? colorFilter)
    {
        var columnCount = sourceX.Length - 1;
        var rowCount = sourceY.Length - 1;
        var patchCount = 0;
        for (var y = 0; y < rowCount; y++)
        {
            for (var x = 0; x < columnCount; x++)
            {
                if (destinationX[x] == destinationX[x + 1] ||
                    destinationY[y] == destinationY[y + 1] ||
                    GetRectType(rectTypes, originalColumnCount, x, y, skippedFirstColumn, skippedFirstRow) ==
                    SKLatticeRectType.Transparent)
                {
                    continue;
                }

                patchCount++;
            }
        }

        if (patchCount == 0)
        {
            return Array.Empty<TexturePatch>();
        }

        var patches = new TexturePatch[patchCount];
        var patchIndex = 0;
        for (var y = 0; y < rowCount; y++)
        {
            for (var x = 0; x < columnCount; x++)
            {
                if (destinationX[x] == destinationX[x + 1] ||
                    destinationY[y] == destinationY[y + 1])
                {
                    continue;
                }

                var rectType = GetRectType(
                    rectTypes,
                    originalColumnCount,
                    x,
                    y,
                    skippedFirstColumn,
                    skippedFirstRow);
                if (rectType == SKLatticeRectType.Transparent)
                {
                    continue;
                }

                var destination = new Rect(
                    destinationX[x],
                    destinationY[y],
                    destinationX[x + 1] - destinationX[x],
                    destinationY[y + 1] - destinationY[y]);
                if (rectType == SKLatticeRectType.FixedColor)
                {
                    var color = GetFixedColor(
                        colors,
                        originalColumnCount,
                        x,
                        y,
                        skippedFirstColumn,
                        skippedFirstRow);
                    if (colorFilter != null)
                    {
                        color = colorFilter.Apply(color);
                    }

                    patches[patchIndex++] = new TexturePatch(destination, ToVector4(color));
                }
                else
                {
                    patches[patchIndex++] = new TexturePatch(
                        new Rect(
                            sourceX[x],
                            sourceY[y],
                            sourceX[x + 1] - sourceX[x],
                            sourceY[y + 1] - sourceY[y]),
                        destination);
                }
            }
        }

        return patches;
    }

    private static SKLatticeRectType GetRectType(
        SKLatticeRectType[]? rectTypes,
        int originalColumnCount,
        int x,
        int y,
        bool skippedFirstColumn,
        bool skippedFirstRow)
    {
        if (rectTypes == null)
        {
            return SKLatticeRectType.Default;
        }

        var originalX = x + (skippedFirstColumn ? 1 : 0);
        var originalY = y + (skippedFirstRow ? 1 : 0);
        var index = originalY * originalColumnCount + originalX;
        return (uint)index < (uint)rectTypes.Length
            ? rectTypes[index]
            : SKLatticeRectType.Default;
    }

    private static SKColor GetFixedColor(
        SKColor[]? colors,
        int originalColumnCount,
        int x,
        int y,
        bool skippedFirstColumn,
        bool skippedFirstRow)
    {
        var originalX = x + (skippedFirstColumn ? 1 : 0);
        var originalY = y + (skippedFirstRow ? 1 : 0);
        var index = originalY * originalColumnCount + originalX;
        return colors != null && (uint)index < (uint)colors.Length
            ? colors[index]
            : SKColor.Empty;
    }

    private static Vector4 ToVector4(SKColor color) => new(
        color.Red / 255f,
        color.Green / 255f,
        color.Blue / 255f,
        color.Alpha / 255f);
}
