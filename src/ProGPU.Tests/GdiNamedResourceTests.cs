using System.Drawing;
using Xunit;

namespace ProGPU.Tests;

public sealed class GdiNamedResourceTests
{
    [Fact]
    public void ClassDiagramPensExposeCachedNamedColors()
    {
        Assert.Same(Pens.SteelBlue, Pens.SteelBlue);
        Assert.Equal(Color.SteelBlue, Pens.SteelBlue.Color);
        Assert.Equal(Color.Olive, Pens.Olive.Color);
        Assert.Equal(Color.DarkBlue, Pens.DarkBlue.Color);
    }

    [Fact]
    public void ClassDiagramBrushesExposeCachedNamedColors()
    {
        Assert.Same(Brushes.DarkBlue, Brushes.DarkBlue);
        Assert.Equal(Color.DarkBlue, Assert.IsType<SolidBrush>(Brushes.DarkBlue).Color);
        Assert.Equal(Color.LightYellow, Assert.IsType<SolidBrush>(Brushes.LightYellow).Color);
        Assert.Equal(Color.DarkGoldenrod, Assert.IsType<SolidBrush>(Brushes.DarkGoldenrod).Color);
        Assert.Equal(Color.Gold, Assert.IsType<SolidBrush>(Brushes.Gold).Color);
    }
}
