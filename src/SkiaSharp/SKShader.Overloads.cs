using System.Numerics;
using ProGPU.Vector;

namespace SkiaSharp;

public partial class SKShader
{
    public static SKShader CreateEmpty() =>
        new(() => new SolidColorBrush(Vector4.Zero));

    public static SKShader CreateBitmap(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
    }

    public static SKShader CreateBitmap(
        SKBitmap source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY, localMatrix);
    }

    public static SKShader CreateImage(SKImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateRetainedImage(
            source.CreateOwnedCopy(),
            SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp,
            SKMatrix.Identity,
            SKSamplingOptions.Default);
    }

    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY) =>
        CreateImage(source, tileModeX, tileModeY, SKSamplingOptions.Default);

    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKSamplingOptions sampling) =>
        CreateImage(source, tileModeX, tileModeY, sampling, SKMatrix.Identity);

#pragma warning disable CS0619
    [Obsolete("Use CreateImage(SKImage src, SKShaderTileMode tmx, SKShaderTileMode tmy, SKSamplingOptions sampling) instead.", true)]
    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKFilterQuality quality) =>
        CreateImage(source, tileModeX, tileModeY, SamplingFromQuality((int)quality));
#pragma warning restore CS0619

    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKMatrix localMatrix) =>
        CreateImage(source, tileModeX, tileModeY, SKSamplingOptions.Default, localMatrix);

    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKSamplingOptions sampling,
        SKMatrix localMatrix)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateRetainedImage(source.CreateOwnedCopy(), tileModeX, tileModeY, localMatrix, sampling);
    }

#pragma warning disable CS0619
    [Obsolete("Use CreateImage(SKImage src, SKShaderTileMode tmx, SKShaderTileMode tmy, SKSamplingOptions sampling, SKMatrix localMatrix) instead.", true)]
    public static SKShader CreateImage(
        SKImage source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKFilterQuality quality,
        SKMatrix localMatrix) =>
        CreateImage(source, tileModeX, tileModeY, SamplingFromQuality((int)quality), localMatrix);
#pragma warning restore CS0619

    public static SKShader CreatePicture(SKPicture source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader();
    }

    public static SKShader CreatePicture(
        SKPicture source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY);
    }

    public static SKShader CreatePicture(
        SKPicture source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKFilterMode filterMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY, filterMode);
    }

    public static SKShader CreatePicture(
        SKPicture source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKRect tileRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY, tileRect);
    }

    public static SKShader CreatePicture(
        SKPicture source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKFilterMode filterMode,
        SKRect tileRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY, filterMode, tileRect);
    }

    public static SKShader CreatePicture(
        SKPicture source,
        SKShaderTileMode tileModeX,
        SKShaderTileMode tileModeY,
        SKFilterMode filterMode,
        SKMatrix localMatrix,
        SKRect tileRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToShader(tileModeX, tileModeY, filterMode, localMatrix, tileRect);
    }

    public static SKShader CreateLinearGradient(
        SKPoint start,
        SKPoint end,
        SKColor[] colors,
        SKShaderTileMode mode) =>
        CreateLinearGradient(start, end, colors, null, mode);

    public static SKShader CreateLinearGradient(
        SKPoint start,
        SKPoint end,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        SKShaderTileMode mode) =>
        CreateLinearGradient(start, end, colors, colorSpace, null, mode);

    public static SKShader CreateRadialGradient(
        SKPoint center,
        float radius,
        SKColor[] colors,
        SKShaderTileMode mode) =>
        CreateRadialGradient(center, radius, colors, null, mode);

    public static SKShader CreateRadialGradient(
        SKPoint center,
        float radius,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        SKShaderTileMode mode) =>
        CreateRadialGradient(center, radius, colors, colorSpace, null, mode);

    public static SKShader CreateTwoPointConicalGradient(
        SKPoint start,
        float startRadius,
        SKPoint end,
        float endRadius,
        SKColor[] colors,
        SKShaderTileMode mode) =>
        CreateTwoPointConicalGradient(start, startRadius, end, endRadius, colors, null, mode);

    public static SKShader CreateTwoPointConicalGradient(
        SKPoint start,
        float startRadius,
        SKPoint end,
        float endRadius,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        SKShaderTileMode mode) =>
        CreateTwoPointConicalGradient(
            start,
            startRadius,
            end,
            endRadius,
            colors,
            colorSpace,
            null,
            mode);

    public static SKShader CreateSweepGradient(SKPoint center, SKColor[] colors) =>
        CreateSweepGradient(center, colors, null, SKShaderTileMode.Clamp, 0f, 360f);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColor[] colors,
        float[]? colorPos) =>
        CreateSweepGradient(center, colors, colorPos, SKShaderTileMode.Clamp, 0f, 360f);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColor[] colors,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle) =>
        CreateSweepGradient(center, colors, null, tileMode, startAngle, endAngle);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle) =>
        CreateSweepGradientCore(
            center,
            CreateGradientStops(colors, colorPos),
            tileMode,
            startAngle,
            endAngle,
            SKMatrix.Identity,
            GradientColorInterpolationMode.SRgbLinearInterpolation);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColor[] colors,
        float[]? colorPos,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle,
        SKMatrix localMatrix) =>
        CreateSweepGradientCore(
            center,
            CreateGradientStops(colors, colorPos),
            tileMode,
            startAngle,
            endAngle,
            localMatrix,
            GradientColorInterpolationMode.SRgbLinearInterpolation);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace) =>
        CreateSweepGradient(center, colors, colorSpace, null, SKShaderTileMode.Clamp, 0f, 360f);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        float[]? colorPos) =>
        CreateSweepGradient(center, colors, colorSpace, colorPos, SKShaderTileMode.Clamp, 0f, 360f);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        float[]? colorPos,
        SKMatrix localMatrix) =>
        CreateSweepGradient(
            center,
            colors,
            colorSpace,
            colorPos,
            SKShaderTileMode.Clamp,
            0f,
            360f,
            localMatrix);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle) =>
        CreateSweepGradient(center, colors, colorSpace, null, tileMode, startAngle, endAngle);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        float[]? colorPos,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle) =>
        CreateSweepGradient(
            center,
            colors,
            colorSpace,
            colorPos,
            tileMode,
            startAngle,
            endAngle,
            SKMatrix.Identity);

    public static SKShader CreateSweepGradient(
        SKPoint center,
        SKColorF[] colors,
        SKColorSpace colorSpace,
        float[]? colorPos,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle,
        SKMatrix localMatrix) =>
        CreateSweepGradientCore(
            center,
            CreateGradientStops(colors, colorPos),
            tileMode,
            startAngle,
            endAngle,
            localMatrix,
            colorSpace?.IsLinear == true
                ? GradientColorInterpolationMode.ScRgbLinearInterpolation
                : GradientColorInterpolationMode.SRgbLinearInterpolation);

    public static SKShader CreatePerlinNoiseFractalNoise(
        float baseFrequencyX,
        float baseFrequencyY,
        int numOctaves,
        float seed) =>
        CreatePerlinNoiseFractalNoise(
            baseFrequencyX,
            baseFrequencyY,
            numOctaves,
            seed,
            SKPointI.Empty);

    public static SKShader CreatePerlinNoiseFractalNoise(
        float baseFrequencyX,
        float baseFrequencyY,
        int numOctaves,
        float seed,
        SKSizeI tileSize) =>
        CreatePerlinNoiseFractalNoise(
            baseFrequencyX,
            baseFrequencyY,
            numOctaves,
            seed,
            new SKPointI(tileSize.Width, tileSize.Height));

    public static SKShader CreatePerlinNoiseTurbulence(
        float baseFrequencyX,
        float baseFrequencyY,
        int numOctaves,
        float seed) =>
        CreatePerlinNoiseTurbulence(
            baseFrequencyX,
            baseFrequencyY,
            numOctaves,
            seed,
            SKPointI.Empty);

    public static SKShader CreatePerlinNoiseTurbulence(
        float baseFrequencyX,
        float baseFrequencyY,
        int numOctaves,
        float seed,
        SKSizeI tileSize) =>
        CreatePerlinNoiseTurbulence(
            baseFrequencyX,
            baseFrequencyY,
            numOctaves,
            seed,
            new SKPointI(tileSize.Width, tileSize.Height));

    public static SKShader CreateCompose(
        SKShader shaderA,
        SKShader shaderB,
        SKBlendMode mode) =>
        CreateBlend(mode, shaderA, shaderB);

    public static SKShader CreateBlend(
        SKBlendMode mode,
        SKShader shaderA,
        SKShader shaderB)
    {
        ArgumentNullException.ThrowIfNull(shaderA);
        ArgumentNullException.ThrowIfNull(shaderB);
        return new SKShader(new ComposedShaderData(shaderA, shaderB, mode, null));
    }

    public static SKShader CreateBlend(
        SKBlender blender,
        SKShader shaderA,
        SKShader shaderB)
    {
        ArgumentNullException.ThrowIfNull(shaderA);
        ArgumentNullException.ThrowIfNull(shaderB);
        ArgumentNullException.ThrowIfNull(blender);

        if (blender.TryGetBlendMode(out var mode))
        {
            return new SKShader(new ComposedShaderData(shaderA, shaderB, mode, null));
        }

        if (blender.Arithmetic is { } arithmetic)
        {
            return new SKShader(new ComposedShaderData(shaderA, shaderB, null, arithmetic));
        }

        throw new NotSupportedException("The SKBlender implementation is not supported by shader composition.");
    }

    public static SKShader CreateColorFilter(SKShader shader, SKColorFilter filter)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(filter);
        return shader.WithColorFilter(filter);
    }

    public static SKShader CreateLocalMatrix(SKShader shader, SKMatrix localMatrix)
    {
        ArgumentNullException.ThrowIfNull(shader);
        return shader.WithLocalMatrix(localMatrix);
    }

    private static SKShader CreateSweepGradientCore(
        SKPoint center,
        GradientStop[] stops,
        SKShaderTileMode tileMode,
        float startAngle,
        float endAngle,
        SKMatrix localMatrix,
        GradientColorInterpolationMode interpolationMode)
    {
        if (!float.IsFinite(startAngle) || !float.IsFinite(endAngle) || startAngle > endAngle)
        {
            return CreateEmpty();
        }

        if (startAngle == endAngle && tileMode == SKShaderTileMode.Decal)
        {
            return CreateEmpty();
        }

        if (startAngle == endAngle && tileMode is SKShaderTileMode.Repeat or SKShaderTileMode.Mirror)
        {
            var average = AverageGradientColor(stops);
            return new SKShader(() => new SolidColorBrush(average));
        }

        var spreadMethod = MapTileMode(tileMode);
        if (!TryGetShaderCoordinateTransform(localMatrix, out var coordinateTransform))
        {
            return CreateEmpty();
        }
        return new SKShader(() => new SweepGradientBrush(
            new Vector2(center.X, center.Y),
            CloneGradientStops(stops))
        {
            StartAngle = startAngle,
            EndAngle = endAngle,
            SpreadMethod = spreadMethod,
            CoordinateTransform = coordinateTransform,
            ColorInterpolationMode = interpolationMode,
        });
    }

    private static Vector4 AverageGradientColor(IReadOnlyList<GradientStop> stops)
    {
        if (stops.Count == 0)
        {
            return Vector4.Zero;
        }

        var average = stops[0].Color * Math.Clamp(stops[0].Offset, 0f, 1f);
        var previousOffset = Math.Clamp(stops[0].Offset, 0f, 1f);
        for (var index = 1; index < stops.Count; index++)
        {
            var offset = Math.Clamp(stops[index].Offset, previousOffset, 1f);
            average += (stops[index - 1].Color + stops[index].Color) *
                (0.5f * (offset - previousOffset));
            previousOffset = offset;
        }

        average += stops[^1].Color * (1f - previousOffset);
        return Vector4.Clamp(average, Vector4.Zero, Vector4.One);
    }

    private static SKSamplingOptions SamplingFromQuality(int quality) => quality switch
    {
        0 => SKSamplingOptions.Default,
        1 => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
        2 => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        3 => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => SKSamplingOptions.Default,
    };
}
