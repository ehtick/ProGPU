using System;
using System.Numerics;

namespace SkiaSharp;

public partial struct SKPoint
{
    public readonly bool IsEmpty => Equals(Empty);
    public readonly float Length => (float)Math.Sqrt(_x * _x + _y * _y);
    public readonly float LengthSquared => _x * _x + _y * _y;

    public void Offset(SKPoint point)
    {
        _x += point._x;
        _y += point._y;
    }

    public void Offset(float dx, float dy)
    {
        _x += dx;
        _y += dy;
    }

    public override readonly string ToString() => $"{{X={_x}, Y={_y}}}";

    public static SKPoint Normalize(SKPoint point)
    {
        var lengthSquared = point._x * point._x + point._y * point._y;
        var inverseLength = 1d / Math.Sqrt(lengthSquared);
        return new SKPoint(
            (float)(point._x * inverseLength),
            (float)(point._y * inverseLength));
    }

    public static float Distance(SKPoint point, SKPoint other)
    {
        var dx = point._x - other._x;
        var dy = point._y - other._y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceSquared(SKPoint point, SKPoint other)
    {
        var dx = point._x - other._x;
        var dy = point._y - other._y;
        return dx * dx + dy * dy;
    }

    public static SKPoint Reflect(SKPoint point, SKPoint normal)
    {
        var lengthSquared = point._x * point._x + point._y * point._y;
        return new SKPoint(
            point._x - 2f * lengthSquared * normal._x,
            point._y - 2f * lengthSquared * normal._y);
    }

    public static SKPoint Add(SKPoint point, SKSizeI size) => point + size;

    public static SKPoint Add(SKPoint point, SKSize size) => point + size;

    public static SKPoint Add(SKPoint point, SKPointI size) => point + size;

    public static SKPoint Add(SKPoint point, SKPoint size) => point + size;

    public static SKPoint Subtract(SKPoint point, SKSizeI size) => point - size;

    public static SKPoint Subtract(SKPoint point, SKSize size) => point - size;

    public static SKPoint Subtract(SKPoint point, SKPointI size) => point - size;

    public static SKPoint Subtract(SKPoint point, SKPoint size) => point - size;

    public static SKPoint operator +(SKPoint point, SKSizeI size) =>
        new(point._x + size.Width, point._y + size.Height);

    public static SKPoint operator +(SKPoint point, SKSize size) =>
        new(point._x + size.Width, point._y + size.Height);

    public static SKPoint operator +(SKPoint point, SKPointI size) =>
        new(point._x + size.X, point._y + size.Y);

    public static SKPoint operator +(SKPoint point, SKPoint size) =>
        new(point._x + size._x, point._y + size._y);

    public static SKPoint operator -(SKPoint point, SKSizeI size) =>
        new(point._x - size.Width, point._y - size.Height);

    public static SKPoint operator -(SKPoint point, SKSize size) =>
        new(point._x - size.Width, point._y - size.Height);

    public static SKPoint operator -(SKPoint point, SKPointI size) =>
        new(point._x - size.X, point._y - size.Y);

    public static SKPoint operator -(SKPoint point, SKPoint size) =>
        new(point._x - size._x, point._y - size._y);

    public static implicit operator Vector2(SKPoint point) => new(point._x, point._y);

    public static implicit operator SKPoint(Vector2 vector) => new(vector.X, vector.Y);

    public readonly bool Equals(SKPoint other) => _x == other._x && _y == other._y;

    public override readonly bool Equals(object? obj) => obj is SKPoint other && Equals(other);

    public static bool operator ==(SKPoint left, SKPoint right) => left.Equals(right);

    public static bool operator !=(SKPoint left, SKPoint right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_x);
        hash.Add(_y);
        return hash.ToHashCode();
    }
}
