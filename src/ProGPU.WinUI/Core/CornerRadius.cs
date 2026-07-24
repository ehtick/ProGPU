using System;

namespace Microsoft.UI.Xaml;

public struct CornerRadius : IEquatable<CornerRadius>
{
    public CornerRadius(double uniformRadius)
        : this(uniformRadius, uniformRadius, uniformRadius, uniformRadius)
    {
    }

    public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        if (topLeft < 0d || topRight < 0d || bottomRight < 0d || bottomLeft < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(topLeft), "Corner radii cannot be negative.");
        }

        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    public double TopLeft { readonly get; set; }
    public double TopRight { readonly get; set; }
    public double BottomRight { readonly get; set; }
    public double BottomLeft { readonly get; set; }

    // The retained drawing command currently carries one circular radius.
    // Keep that renderer approximation internal so the public value contract
    // still preserves every WinUI corner independently.
    internal readonly float RenderingRadius => (float)TopLeft;

    public readonly bool IsUniform => TopLeft == TopRight && TopLeft == BottomRight && TopLeft == BottomLeft;

    public readonly bool Equals(CornerRadius other) =>
        TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) &&
        BottomRight.Equals(other.BottomRight) && BottomLeft.Equals(other.BottomLeft);

    public override readonly bool Equals(object? obj) => obj is CornerRadius other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

    public static implicit operator CornerRadius(float value) => new(value);
    public static implicit operator CornerRadius(double value) => new(value);
    public static bool operator ==(CornerRadius left, CornerRadius right) => left.Equals(right);
    public static bool operator !=(CornerRadius left, CornerRadius right) => !left.Equals(right);
}
