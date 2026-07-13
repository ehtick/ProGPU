using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkColorsCompatibilityTests
{
    [Fact]
    public void PaletteUsesNativeOneByteStructShape()
    {
        var type = typeof(SKColors);

        Assert.True(type.IsValueType);
        Assert.Equal(1, Marshal.SizeOf<SKColors>());
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void NamedColorsAreMutableFieldsAndEmptyIsAProperty()
    {
        var type = typeof(SKColors);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.True(fields.Length > 100);
        Assert.All(fields, static field => Assert.False(field.IsInitOnly));
        Assert.DoesNotContain(fields, static field => field.Name == nameof(SKColors.Empty));
        Assert.NotNull(type.GetProperty(nameof(SKColors.Empty), BindingFlags.Public | BindingFlags.Static));
        Assert.Equal(new SKColor(0x00ffffffu), SKColors.Transparent);
        Assert.Equal(new SKColor(0u), SKColors.Empty);
    }
}
