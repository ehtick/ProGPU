using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Tests;

public sealed class EllipseGeometryHitTestingTests
{
    [Fact]
    public void ContainsFillAcceptsPointInsideEllipse()
    {
        Assert.True(EllipseGeometryHitTesting.ContainsFill(
            new Vector2(5f, 3f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f)));
    }

    [Fact]
    public void ContainsFillRejectsPointOutsideEllipse()
    {
        Assert.False(EllipseGeometryHitTesting.ContainsFill(
            new Vector2(9f, 5f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f)));
    }

    [Fact]
    public void ContainsFillRejectsDegenerateEllipse()
    {
        Assert.False(EllipseGeometryHitTesting.ContainsFill(
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 6f)));
    }

    [Fact]
    public void ContainsStrokeAcceptsPointOnEllipseStroke()
    {
        Assert.True(EllipseGeometryHitTesting.ContainsStroke(
            new Vector2(10f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f),
            strokeThickness: 2f,
            tolerance: 0f,
            relativeTolerance: false));
    }

    [Fact]
    public void ContainsStrokeRejectsPointInsideStrokeHole()
    {
        Assert.False(EllipseGeometryHitTesting.ContainsStroke(
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f),
            strokeThickness: 2f,
            tolerance: 0f,
            relativeTolerance: false));
    }

    [Fact]
    public void ContainsStrokeAppliesRelativeTolerance()
    {
        Assert.True(EllipseGeometryHitTesting.ContainsStroke(
            new Vector2(11.5f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f),
            strokeThickness: 1f,
            tolerance: 0.05f,
            relativeTolerance: true));
    }

    [Fact]
    public void ContainsStrokeRejectsInvalidTolerance()
    {
        Assert.False(EllipseGeometryHitTesting.ContainsStroke(
            new Vector2(10f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 6f),
            strokeThickness: 2f,
            tolerance: float.NaN,
            relativeTolerance: false));
    }
}
