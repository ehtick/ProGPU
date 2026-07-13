using System;

namespace SkiaSharp;

public struct SKColorSpaceTransferFn : IEquatable<SKColorSpaceTransferFn>
{
    private enum TransferFunctionKind
    {
        Invalid,
        Srgb,
        Pq,
        Hlg,
        HlgInverse,
        PqNamed,
        HlgNamed,
    }

    private float _g;
    private float _a;
    private float _b;
    private float _c;
    private float _d;
    private float _e;
    private float _f;

    public static readonly SKColorSpaceTransferFn Empty;

    public static SKColorSpaceTransferFn Srgb => new(
        2.4f,
        0.9478673f,
        0.0521327f,
        0.07739938f,
        0.04045f,
        0f,
        0f);

    public static SKColorSpaceTransferFn TwoDotTwo => new(2.2f, 1f, 0f, 0f, 0f, 0f, 0f);

    public static SKColorSpaceTransferFn Linear => new(1f, 1f, 0f, 0f, 0f, 0f, 0f);

    public static SKColorSpaceTransferFn Rec2020 => new(
        2.22222f,
        0.909672f,
        0.0903276f,
        0.222222f,
        0.0812429f,
        0f,
        0f);

    public static SKColorSpaceTransferFn Pq => new(-5f, 203f, 0f, 0f, 0f, 0f, 0f);

    public static SKColorSpaceTransferFn Hlg => new(-6f, 203f, 1000f, 1.2f, 0f, 0f, 0f);

    public readonly float[] Values => [_g, _a, _b, _c, _d, _e, _f];

    public float G
    {
        readonly get => _g;
        set => _g = value;
    }

    public float A
    {
        readonly get => _a;
        set => _a = value;
    }

    public float B
    {
        readonly get => _b;
        set => _b = value;
    }

    public float C
    {
        readonly get => _c;
        set => _c = value;
    }

    public float D
    {
        readonly get => _d;
        set => _d = value;
    }

    public float E
    {
        readonly get => _e;
        set => _e = value;
    }

    public float F
    {
        readonly get => _f;
        set => _f = value;
    }

    public SKColorSpaceTransferFn(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != 7)
        {
            throw new ArgumentException(
                "The values must have exactly 7 items, one for each of [G, A, B, C, D, E, F].",
                nameof(values));
        }

        _g = values[0];
        _a = values[1];
        _b = values[2];
        _c = values[3];
        _d = values[4];
        _e = values[5];
        _f = values[6];
    }

    public SKColorSpaceTransferFn(float g, float a, float b, float c, float d, float e, float f)
    {
        _g = g;
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _e = e;
        _f = f;
    }

    public readonly SKColorSpaceTransferFn Invert()
    {
        switch (Classify())
        {
            case TransferFunctionKind.Invalid:
            case TransferFunctionKind.PqNamed:
            case TransferFunctionKind.HlgNamed:
                return Empty;
            case TransferFunctionKind.Pq:
                return new SKColorSpaceTransferFn(-2f, -_a, _d, 1f / _f, _b, -_e, 1f / _c);
            case TransferFunctionKind.Hlg:
                return new SKColorSpaceTransferFn(-4f, 1f / _a, 1f / _b, 1f / _c, _d, _e, _f);
            case TransferFunctionKind.HlgInverse:
                return new SKColorSpaceTransferFn(-3f, 1f / _a, 1f / _b, 1f / _c, _d, _e, _f);
            case TransferFunctionKind.Srgb:
                break;
        }

        var inverse = Empty;
        var leftThreshold = _c * _d + _f;
        var rightThreshold = FastPow(_a * _d + _b, _g) + _e;
        if (MathF.Abs(leftThreshold - rightThreshold) > 1f / 512f)
        {
            return Empty;
        }

        inverse._d = leftThreshold;
        if (inverse._d > 0f)
        {
            inverse._c = 1f / _c;
            inverse._f = -_f / _c;
        }

        var scale = FastPow(_a, -_g);
        inverse._g = 1f / _g;
        inverse._a = scale;
        inverse._b = -scale * _e;
        inverse._e = -_b / _a;
        if (inverse._a < 0f)
        {
            return Empty;
        }

        if (inverse._a * inverse._d + inverse._b < 0f)
        {
            inverse._b = -inverse._a * inverse._d;
        }

        if (inverse.Classify() != TransferFunctionKind.Srgb)
        {
            return Empty;
        }

        var transformedOne = Transform(1f);
        if (!float.IsFinite(transformedOne))
        {
            return Empty;
        }

        var sign = transformedOne < 0f ? -1f : 1f;
        transformedOne *= sign;
        if (transformedOne < inverse._d)
        {
            inverse._f = 1f - sign * inverse._c * transformedOne;
        }
        else
        {
            inverse._e = 1f - sign * FastPow(inverse._a * transformedOne + inverse._b, inverse._g);
        }

        return inverse.Classify() == TransferFunctionKind.Srgb ? inverse : Empty;
    }

    public readonly float Transform(float value)
    {
        var sign = value < 0f ? -1f : 1f;
        value *= sign;
        switch (Classify())
        {
            case TransferFunctionKind.HlgNamed:
            {
                const float a = 0.17883277f;
                const float b = 0.28466892f;
                const float c = 0.55991073f;
                return sign * (value <= 0.5f
                    ? value * value / 3f
                    : (FastExp((value - c) / a) + b) / 12f);
            }
            case TransferFunctionKind.Hlg:
            {
                var scale = _f + 1f;
                return scale * sign * (value * _a <= 1f
                    ? FastPow(value * _a, _b)
                    : FastExp((value - _e) * _c) + _d);
            }
            case TransferFunctionKind.HlgInverse:
            {
                var scale = _f + 1f;
                value /= scale;
                return sign * (value <= 1f
                    ? _a * FastPow(value, _b)
                    : _c * FastLog(value - _d) + _e);
            }
            case TransferFunctionKind.Srgb:
                return sign * (value < _d
                    ? _c * value + _f
                    : FastPow(_a * value + _b, _g) + _e);
            case TransferFunctionKind.PqNamed:
            {
                const float c1 = 107f / 128f;
                const float c2 = 2413f / 128f;
                const float c3 = 2392f / 128f;
                const float m1 = 1305f / 8192f;
                const float m2 = 2523f / 32f;
                var power = FastPow(value, 1f / m2);
                return FastPow((power - c1) / (c2 - c3 * power), 1f / m1);
            }
            case TransferFunctionKind.Pq:
            {
                var power = FastPow(value, _c);
                return sign * FastPow((_a + _b * power) / (_d + _e * power), _f);
            }
            default:
                return 0f;
        }
    }

    public readonly bool Equals(SKColorSpaceTransferFn other) =>
        _g == other._g &&
        _a == other._a &&
        _b == other._b &&
        _c == other._c &&
        _d == other._d &&
        _e == other._e &&
        _f == other._f;

    public override readonly bool Equals(object? obj) => obj is SKColorSpaceTransferFn other && Equals(other);

    public static bool operator ==(SKColorSpaceTransferFn left, SKColorSpaceTransferFn right) => left.Equals(right);

    public static bool operator !=(SKColorSpaceTransferFn left, SKColorSpaceTransferFn right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_g);
        hash.Add(_a);
        hash.Add(_b);
        hash.Add(_c);
        hash.Add(_d);
        hash.Add(_e);
        hash.Add(_f);
        return hash.ToHashCode();
    }

    internal readonly bool IsValid => Classify() != TransferFunctionKind.Invalid;

    internal readonly bool IsNumerical => Classify() == TransferFunctionKind.Srgb;

    private readonly TransferFunctionKind Classify()
    {
        if (_g < 0f)
        {
            if (_g < -128f)
            {
                return TransferFunctionKind.Invalid;
            }

            var kind = -(int)_g;
            if (-kind != _g)
            {
                return TransferFunctionKind.Invalid;
            }

            return kind switch
            {
                2 => TransferFunctionKind.Pq,
                3 => TransferFunctionKind.Hlg,
                4 => TransferFunctionKind.HlgInverse,
                5 when _b == 0f && _c == 0f && _d == 0f && _e == 0f && _f == 0f =>
                    TransferFunctionKind.PqNamed,
                6 when _d == 0f && _e == 0f && _f == 0f => TransferFunctionKind.HlgNamed,
                _ => TransferFunctionKind.Invalid,
            };
        }

        var sum = _a + _b + _c + _d + _e + _f + _g;
        return float.IsFinite(sum) &&
               _a >= 0f &&
               _c >= 0f &&
               _d >= 0f &&
               _g >= 0f &&
               _a * _d + _b >= 0f
            ? TransferFunctionKind.Srgb
            : TransferFunctionKind.Invalid;
    }

    private static float FastLog2(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        var exponent = (float)bits * (1f / (1 << 23));
        var mantissaBits = (bits & 0x007fffff) | 0x3f000000;
        var mantissa = BitConverter.Int32BitsToSingle(mantissaBits);
        return exponent - 124.225514990f -
               1.498030302f * mantissa -
               1.725879990f / (0.3520887068f + mantissa);
    }

    private static float FastLog(float value) => 0.69314718f * FastLog2(value);

    private static float FastExp2(float value)
    {
        if (value > 128f)
        {
            return float.PositiveInfinity;
        }

        if (value < -127f)
        {
            return 0f;
        }

        var fraction = value - MathF.Floor(value);
        var floatBits = (1f * (1 << 23)) *
                        (value + 121.274057500f -
                         1.490129070f * fraction +
                         27.728023300f / (4.84252568f - fraction));
        if (floatBits >= (float)int.MaxValue)
        {
            return float.PositiveInfinity;
        }

        if (floatBits < 0f)
        {
            return 0f;
        }

        return BitConverter.Int32BitsToSingle((int)floatBits);
    }

    private static float FastPow(float value, float power)
    {
        if (value <= 0f)
        {
            return 0f;
        }

        if (value == 1f)
        {
            return 1f;
        }

        return FastExp2(FastLog2(value) * power);
    }

    private static float FastExp(float value) => FastExp2(1.4426950408889634074f * value);
}
