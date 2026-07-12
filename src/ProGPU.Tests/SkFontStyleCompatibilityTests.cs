using System.Reflection;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkFontStyleCompatibilityTests
{
    [Fact]
    public void DefaultConstructorCreatesNativeNormalStyle()
    {
        using var style = new SKFontStyle();

        Assert.Equal((int)SKFontStyleWeight.Normal, style.Weight);
        Assert.Equal((int)SKFontStyleWidth.Normal, style.Width);
        Assert.Equal(SKFontStyleSlant.Upright, style.Slant);
    }

    [Fact]
    public void IntegerConstructorPreservesArbitraryNativeValues()
    {
        using var style = new SKFontStyle(-17, 23, (SKFontStyleSlant)19);

        Assert.Equal(-17, style.Weight);
        Assert.Equal(23, style.Width);
        Assert.Equal((SKFontStyleSlant)19, style.Slant);
    }

    [Fact]
    public void NamedStylesHaveNativeValuesAndStableIdentity()
    {
        Assert.Same(SKFontStyle.Normal, SKFontStyle.Normal);
        Assert.Same(SKFontStyle.Bold, SKFontStyle.Bold);
        Assert.Same(SKFontStyle.Italic, SKFontStyle.Italic);
        Assert.Same(SKFontStyle.BoldItalic, SKFontStyle.BoldItalic);

        AssertStyle(SKFontStyle.Normal, SKFontStyleWeight.Normal, SKFontStyleSlant.Upright);
        AssertStyle(SKFontStyle.Bold, SKFontStyleWeight.Bold, SKFontStyleSlant.Upright);
        AssertStyle(SKFontStyle.Italic, SKFontStyleWeight.Normal, SKFontStyleSlant.Italic);
        AssertStyle(SKFontStyle.BoldItalic, SKFontStyleWeight.Bold, SKFontStyleSlant.Italic);
    }

    [Fact]
    public void NamedStylesIgnorePublicDisposalWhileOwnedInstancesReleaseHandles()
    {
        var singleton = SKFontStyle.Bold;
        var singletonHandle = singleton.Handle;
        singleton.Dispose();

        Assert.NotEqual(IntPtr.Zero, singletonHandle);
        Assert.Equal(singletonHandle, singleton.Handle);

        var owned = new SKFontStyle();
        Assert.NotEqual(IntPtr.Zero, owned.Handle);
        owned.Dispose();
        Assert.Equal(IntPtr.Zero, owned.Handle);
    }

    [Fact]
    public void NamedStylesArePropertiesAndNoShimMembersAreDeclared()
    {
        var type = typeof(SKFontStyle);

        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(
            new[] { "Bold", "BoldItalic", "Italic", "Normal" },
            type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Null(type.GetMethod(nameof(IDisposable.Dispose), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Null(type.GetProperty(nameof(SKObject.Handle), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    private static void AssertStyle(
        SKFontStyle style,
        SKFontStyleWeight weight,
        SKFontStyleSlant slant)
    {
        Assert.Equal((int)weight, style.Weight);
        Assert.Equal((int)SKFontStyleWidth.Normal, style.Width);
        Assert.Equal(slant, style.Slant);
    }
}
