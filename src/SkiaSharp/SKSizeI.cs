using System;

namespace SkiaSharp;

public partial struct SKSizeI
{
    public readonly bool IsEmpty => Equals(Empty);

    public SKSizeI(SKPointI point)
    {
        _width = point.X;
        _height = point.Y;
    }

    public readonly SKPointI ToPointI() => new(_width, _height);

    public override readonly string ToString() => $"{{Width={_width}, Height={_height}}}";

    public static SKSizeI Add(SKSizeI first, SKSizeI second) => first + second;

    public static SKSizeI Subtract(SKSizeI first, SKSizeI second) => first - second;

    public static SKSizeI operator +(SKSizeI first, SKSizeI second) =>
        new(first._width + second._width, first._height + second._height);

    public static SKSizeI operator -(SKSizeI first, SKSizeI second) =>
        new(first._width - second._width, first._height - second._height);

    public static explicit operator SKPointI(SKSizeI size) =>
        new(size._width, size._height);

    public readonly bool Equals(SKSizeI other) =>
        _width == other._width && _height == other._height;

    public override readonly bool Equals(object? obj) => obj is SKSizeI other && Equals(other);

    public static bool operator ==(SKSizeI left, SKSizeI right) => left.Equals(right);

    public static bool operator !=(SKSizeI left, SKSizeI right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_width);
        hash.Add(_height);
        return hash.ToHashCode();
    }
}
