using System.Numerics;
using ProGPU.Scene;
using SkiaSharp;
using Xunit;

namespace ProGPU.Tests;

public sealed class SkLatticeCompatibilityTests
{
    [Fact]
    public void RectTypeValuesAndLatticeReferenceEqualityMatchNative()
    {
        Assert.Equal(0, (int)SKLatticeRectType.Default);
        Assert.Equal(1, (int)SKLatticeRectType.Transparent);
        Assert.Equal(2, (int)SKLatticeRectType.FixedColor);

        var xDivs = new[] { 2, 7 };
        var yDivs = new[] { 3, 9 };
        var first = new SKLattice { XDivs = xDivs, YDivs = yDivs };
        var sameReferences = new SKLattice { XDivs = xDivs, YDivs = yDivs };
        var copiedArrays = new SKLattice { XDivs = [2, 7], YDivs = [3, 9] };

        Assert.Equal(first, sameReferences);
        Assert.True(first == sameReferences);
        Assert.NotEqual(first, copiedArrays);
        Assert.True(first != copiedArrays);
    }

    [Fact]
    public void LatticePreservesFixedSegmentsAndScalesStretchSegments()
    {
        var lattice = new SKLattice
        {
            XDivs = [5, 15],
            YDivs = [4, 12]
        };

        Assert.True(SKLatticeLayout.TryCreateLattice(
            30,
            20,
            lattice,
            new SKRect(0f, 0f, 100f, 80f),
            colorFilter: null,
            out var patches));

        Assert.Equal(9, patches.Length);
        AssertPatch(
            patches[4],
            new Rect(5f, 4f, 10f, 8f),
            new Rect(5f, 4f, 80f, 68f));
        AssertPatch(
            patches[8],
            new Rect(15f, 12f, 15f, 8f),
            new Rect(85f, 72f, 15f, 8f));
    }

    [Fact]
    public void UndersizedLatticeCollapsesStretchSegmentsAndScalesFixedSegments()
    {
        var lattice = new SKLattice
        {
            XDivs = [5, 15],
            YDivs = [4, 12]
        };

        Assert.True(SKLatticeLayout.TryCreateLattice(
            30,
            20,
            lattice,
            new SKRect(0f, 0f, 10f, 6f),
            colorFilter: null,
            out var patches));

        Assert.Equal(4, patches.Length);
        Assert.Equal(new Rect(0f, 0f, 2.5f, 2f), patches[0].Destination);
        Assert.Equal(new Rect(2.5f, 0f, 7.5f, 2f), patches[1].Destination);
        Assert.Equal(new Rect(0f, 2f, 2.5f, 4f), patches[2].Destination);
        Assert.Equal(new Rect(2.5f, 2f, 7.5f, 4f), patches[3].Destination);
    }

    [Fact]
    public void LeadingBoundaryDivsSkipPaddedFlagsAndColors()
    {
        var rectTypes = new SKLatticeRectType[16];
        var colors = new SKColor[16];
        rectTypes[5] = SKLatticeRectType.FixedColor;
        colors[5] = SKColors.Red;
        rectTypes[6] = SKLatticeRectType.Transparent;
        using var colorFilter = SKColorFilter.CreateBlendMode(SKColors.Blue, SKBlendMode.Src);
        var lattice = new SKLattice
        {
            Bounds = new SKRectI(10, 0, 50, 20),
            XDivs = [10, 20, 30],
            YDivs = [0, 5, 15],
            RectTypes = rectTypes,
            Colors = colors
        };

        Assert.True(SKLatticeLayout.TryCreateLattice(
            60,
            20,
            lattice,
            new SKRect(0f, 0f, 40f, 20f),
            colorFilter,
            out var patches));

        Assert.Equal(8, patches.Length);
        Assert.Equal(TexturePatchKind.FixedColor, patches[0].Kind);
        Assert.Equal(new Rect(0f, 0f, 10f, 5f), patches[0].Destination);
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), patches[0].Color);
        Assert.DoesNotContain(
            patches,
            patch => patch.Destination == new Rect(10f, 0f, 10f, 5f));
    }

    [Fact]
    public void NinePatchAcceptsCentersTouchingImageEdges()
    {
        Assert.True(SKLatticeLayout.TryCreateNinePatch(
            20,
            10,
            new SKRectI(0, 2, 20, 8),
            new SKRect(0f, 0f, 100f, 30f),
            out var patches));

        Assert.Equal(3, patches.Length);
        AssertPatch(
            patches[0],
            new Rect(0f, 0f, 20f, 2f),
            new Rect(0f, 0f, 100f, 2f));
        AssertPatch(
            patches[1],
            new Rect(0f, 2f, 20f, 6f),
            new Rect(0f, 2f, 100f, 26f));
    }

    [Fact]
    public void InvalidLatticesAreRejectedWithoutPartialPatches()
    {
        var duplicateDivs = new SKLattice { XDivs = [5, 5], YDivs = [4, 8] };
        Assert.False(SKLatticeLayout.TryCreateLattice(
            20,
            20,
            duplicateDivs,
            new SKRect(0f, 0f, 20f, 20f),
            colorFilter: null,
            out var duplicatePatches));
        Assert.Empty(duplicatePatches);

        var noDivs = new SKLattice { XDivs = [], YDivs = [] };
        Assert.False(SKLatticeLayout.TryCreateLattice(
            20,
            20,
            noDivs,
            new SKRect(0f, 0f, 20f, 20f),
            colorFilter: null,
            out var emptyPatches));
        Assert.Empty(emptyPatches);
    }

    private static void AssertPatch(TexturePatch patch, Rect source, Rect destination)
    {
        Assert.Equal(TexturePatchKind.Texture, patch.Kind);
        Assert.Equal(source, patch.Source);
        Assert.Equal(destination, patch.Destination);
    }
}
