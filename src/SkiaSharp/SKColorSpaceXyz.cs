using System;

namespace SkiaSharp;

public struct SKColorSpaceXyz : IEquatable<SKColorSpaceXyz>
{
    private float _m00;
    private float _m01;
    private float _m02;
    private float _m10;
    private float _m11;
    private float _m12;
    private float _m20;
    private float _m21;
    private float _m22;

    public static readonly SKColorSpaceXyz Empty;

    public static readonly SKColorSpaceXyz Identity = new(
        1f, 0f, 0f,
        0f, 1f, 0f,
        0f, 0f, 1f);

    public static SKColorSpaceXyz Srgb => new(
        0.43606567f, 0.3851471f, 0.1430664f,
        0.2224884f, 0.71687317f, 0.06060791f,
        0.013916016f, 0.097076416f, 0.71409607f);

    public static SKColorSpaceXyz AdobeRgb => new(
        0.6097412f, 0.20527649f, 0.14918518f,
        0.31111145f, 0.6256714f, 0.06321716f,
        0.019470215f, 0.06086731f, 0.7445679f);

    public static SKColorSpaceXyz DisplayP3 => new(
        0.515102f, 0.291965f, 0.157153f,
        0.241182f, 0.692236f, 0.0665819f,
        -0.00104941f, 0.0418818f, 0.784378f);

    public static SKColorSpaceXyz Rec2020 => new(
        0.673459f, 0.165661f, 0.1251f,
        0.279033f, 0.675338f, 0.0456288f,
        -0.00193139f, 0.0299794f, 0.797162f);

    public static SKColorSpaceXyz Xyz => Identity;

    public float[] Values
    {
        readonly get =>
        [
            _m00, _m01, _m02,
            _m10, _m11, _m12,
            _m20, _m21, _m22,
        ];
        set
        {
            if (value.Length != 9)
            {
                throw new ArgumentException("The matrix array must have a length of 9.", nameof(value));
            }

            _m00 = value[0];
            _m01 = value[1];
            _m02 = value[2];
            _m10 = value[3];
            _m11 = value[4];
            _m12 = value[5];
            _m20 = value[6];
            _m21 = value[7];
            _m22 = value[8];
        }
    }

    public readonly float this[int x, int y]
    {
        get
        {
            if ((uint)x >= 3u)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if ((uint)y >= 3u)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return (x + y * 3) switch
            {
                0 => _m00,
                1 => _m01,
                2 => _m02,
                3 => _m10,
                4 => _m11,
                5 => _m12,
                6 => _m20,
                7 => _m21,
                8 => _m22,
                _ => throw new ArgumentOutOfRangeException("index"),
            };
        }
    }

    public SKColorSpaceXyz(float value)
    {
        _m00 = value;
        _m01 = value;
        _m02 = value;
        _m10 = value;
        _m11 = value;
        _m12 = value;
        _m20 = value;
        _m21 = value;
        _m22 = value;
    }

    public SKColorSpaceXyz(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != 9)
        {
            throw new ArgumentException("The matrix array must have a length of 9.", nameof(values));
        }

        _m00 = values[0];
        _m01 = values[1];
        _m02 = values[2];
        _m10 = values[3];
        _m11 = values[4];
        _m12 = values[5];
        _m20 = values[6];
        _m21 = values[7];
        _m22 = values[8];
    }

    public SKColorSpaceXyz(
        float m00,
        float m01,
        float m02,
        float m10,
        float m11,
        float m12,
        float m20,
        float m21,
        float m22)
    {
        _m00 = m00;
        _m01 = m01;
        _m02 = m02;
        _m10 = m10;
        _m11 = m11;
        _m12 = m12;
        _m20 = m20;
        _m21 = m21;
        _m22 = m22;
    }

    public readonly SKColorSpaceXyz Invert()
    {
        double a00 = _m00;
        double a01 = _m10;
        double a02 = _m20;
        double a10 = _m01;
        double a11 = _m11;
        double a12 = _m21;
        double a20 = _m02;
        double a21 = _m12;
        double a22 = _m22;
        var b0 = a00 * a11 - a01 * a10;
        var b1 = a00 * a12 - a02 * a10;
        var b2 = a01 * a12 - a02 * a11;
        var b3 = a20;
        var b4 = a21;
        var b5 = a22;
        var determinant = b0 * b5 - b1 * b4 + b2 * b3;
        if (determinant == 0d)
        {
            return Empty;
        }

        var inverseDeterminant = 1d / determinant;
        if (inverseDeterminant > float.MaxValue ||
            inverseDeterminant < -float.MaxValue ||
            !double.IsFinite(inverseDeterminant))
        {
            return Empty;
        }

        b0 *= inverseDeterminant;
        b1 *= inverseDeterminant;
        b2 *= inverseDeterminant;
        b3 *= inverseDeterminant;
        b4 *= inverseDeterminant;
        b5 *= inverseDeterminant;
        var result = new SKColorSpaceXyz(
            (float)(a11 * b5 - a12 * b4),
            (float)(a12 * b3 - a10 * b5),
            (float)(a10 * b4 - a11 * b3),
            (float)(a02 * b4 - a01 * b5),
            (float)(a00 * b5 - a02 * b3),
            (float)(a01 * b3 - a00 * b4),
            (float)b2,
            (float)-b1,
            (float)b0);
        return result.IsFinite() ? result : Empty;
    }

    public static SKColorSpaceXyz Concat(SKColorSpaceXyz a, SKColorSpaceXyz b) => new(
        a._m00 * b._m00 + a._m01 * b._m10 + a._m02 * b._m20,
        a._m00 * b._m01 + a._m01 * b._m11 + a._m02 * b._m21,
        a._m00 * b._m02 + a._m01 * b._m12 + a._m02 * b._m22,
        a._m10 * b._m00 + a._m11 * b._m10 + a._m12 * b._m20,
        a._m10 * b._m01 + a._m11 * b._m11 + a._m12 * b._m21,
        a._m10 * b._m02 + a._m11 * b._m12 + a._m12 * b._m22,
        a._m20 * b._m00 + a._m21 * b._m10 + a._m22 * b._m20,
        a._m20 * b._m01 + a._m21 * b._m11 + a._m22 * b._m21,
        a._m20 * b._m02 + a._m21 * b._m12 + a._m22 * b._m22);

    public readonly bool Equals(SKColorSpaceXyz other) =>
        _m00 == other._m00 &&
        _m01 == other._m01 &&
        _m02 == other._m02 &&
        _m10 == other._m10 &&
        _m11 == other._m11 &&
        _m12 == other._m12 &&
        _m20 == other._m20 &&
        _m21 == other._m21 &&
        _m22 == other._m22;

    public override readonly bool Equals(object? obj) => obj is SKColorSpaceXyz other && Equals(other);

    public static bool operator ==(SKColorSpaceXyz left, SKColorSpaceXyz right) => left.Equals(right);

    public static bool operator !=(SKColorSpaceXyz left, SKColorSpaceXyz right) => !left.Equals(right);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_m00);
        hash.Add(_m01);
        hash.Add(_m02);
        hash.Add(_m10);
        hash.Add(_m11);
        hash.Add(_m12);
        hash.Add(_m20);
        hash.Add(_m21);
        hash.Add(_m22);
        return hash.ToHashCode();
    }

    private readonly bool IsFinite() =>
        float.IsFinite(_m00) &&
        float.IsFinite(_m01) &&
        float.IsFinite(_m02) &&
        float.IsFinite(_m10) &&
        float.IsFinite(_m11) &&
        float.IsFinite(_m12) &&
        float.IsFinite(_m20) &&
        float.IsFinite(_m21) &&
        float.IsFinite(_m22);
}
