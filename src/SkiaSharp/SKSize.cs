using System;

namespace SkiaSharp;

public partial struct SKSize
{
    public readonly bool IsEmpty => Equals(Empty);

    public SKSize(SKPoint point)
    {
        _width = point.X;
        _height = point.Y;
    }

    public readonly SKPoint ToPoint() => new(_width, _height);

    public readonly SKSizeI ToSizeI()
    {
        checked
        {
            return new SKSizeI((int)_width, (int)_height);
        }
    }

    public override readonly string ToString() =>
        FormattableString.Invariant($"{{Width={_width}, Height={_height}}}");

    public static SKSize Add(SKSize first, SKSize second) => first + second;

    public static SKSize Subtract(SKSize first, SKSize second) => first - second;

    public static SKSize operator +(SKSize first, SKSize second) =>
        new(first._width + second._width, first._height + second._height);

    public static SKSize operator -(SKSize first, SKSize second) =>
        new(first._width - second._width, first._height - second._height);

    public static explicit operator SKPoint(SKSize size) => new(size._width, size._height);

    public static implicit operator SKSize(SKSizeI size) => new(size.Width, size.Height);

    public readonly bool Equals(SKSize other) =>
        _width == other._width && _height == other._height;

    public override readonly bool Equals(object? obj) => obj is SKSize other && Equals(other);

    public static bool operator ==(SKSize left, SKSize right) => left.Equals(right);

    public static bool operator !=(SKSize left, SKSize right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_width);
        hash.Add(_height);
        return hash.ToHashCode();
    }
}
