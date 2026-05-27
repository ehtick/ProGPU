using System;
using System.Numerics;
using Xunit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ProGPU.Designer;
using DataPackage = ProGPU.Designer.DataPackage;
using DragEventArgs = ProGPU.Designer.DragEventArgs;
using StandardDataFormats = ProGPU.Designer.StandardDataFormats;
using ProGPU.Vector;
using ProGPU.Layout;

namespace ProGPU.Tests.Headless;

[Collection("HeadlessTests")]
public class DesignerCanvasTests
{
    [Fact]
    public void Test_DesignerCanvas_Drop_Reflection_Instantiation_And_GridSnap()
    {
        var canvas = new DesignerCanvas
        {
            Width = 800,
            Height = 600,
            AllowDrop = true
        };

        // Prepare data package holding the "Button" tool
        var data = new DataPackage();
        data.SetData(StandardDataFormats.Tool, "Button");

        // Prepare drop coordinates: e.g. at (53, 104) which should snap to (50, 100) on 10px grid
        var dropPos = new Vector2(53f, 104f);
        var args = new DragEventArgs(data, dropPos);

        // Act
        canvas.OnDrop(args);

        // Assert
        Assert.Single(canvas.DesignSurface.Children);
        var instantiatedControl = canvas.DesignSurface.Children[0] as Button;
        Assert.NotNull(instantiatedControl);
        
        // Assert grid snapping worked
        float left = Canvas.GetLeft(instantiatedControl);
        float top = Canvas.GetTop(instantiatedControl);
        Assert.Equal(50f, left);
        Assert.Equal(100f, top);

        // Assert that the instantiated control is selected
        Assert.Same(instantiatedControl, canvas.SelectedElement);
    }

    [Fact]
    public void Test_DesignerCanvas_Magnetic_Alignment_Snapping()
    {
        var canvas = new DesignerCanvas
        {
            Width = 800,
            Height = 600
        };

        // Add a static control A at (100, 100) with size (100, 50)
        var controlA = new Border();
        Canvas.SetLeft(controlA, 100f);
        Canvas.SetTop(controlA, 100f);
        controlA.Width = 100f;
        controlA.Height = 50f;
        canvas.DesignSurface.Children.Add(controlA);

        // Create control B which we will drag
        var controlB = new Border();
        Canvas.SetLeft(controlB, 300f);
        Canvas.SetTop(controlB, 300f);
        controlB.Width = 100f;
        controlB.Height = 50f;
        canvas.DesignSurface.Children.Add(controlB);

        // Set selected element
        canvas.SelectElement(controlB);

        // Drag B close to A vertical left alignment (e.g. drag B to x = 105f which is within 8px of A left=100f)
        var targetPos = new Vector2(105f, 300f);
        var snappedPos = canvas.SnapPosition(controlB, targetPos);

        // Assert B left snapped to exactamente A left (100f)
        Assert.Equal(100f, snappedPos.X);
        Assert.Equal(100f, canvas.ActiveVerticalSnapX);

        // Drag B close to A center alignment (e.g. A center is 150f, drag B center near it)
        // Control B width is 100, so my center is x + 50.
        // If my left is 102f, my center is 152f, which is within 8px of 150f.
        targetPos = new Vector2(102f, 300f);
        snappedPos = canvas.SnapPosition(controlB, targetPos);
        Assert.Equal(100f, snappedPos.X); // snaps my center to A center, so my left becomes 150 - 50 = 100f
    }

    [Fact]
    public void Test_SelectionAdorner_Handles_Layout()
    {
        var canvas = new DesignerCanvas
        {
            Width = 800,
            Height = 600
        };

        var button = new Button();
        Canvas.SetLeft(button, 150f);
        Canvas.SetTop(button, 120f);
        button.Width = 120f;
        button.Height = 40f;
        canvas.DesignSurface.Children.Add(button);

        // Act - Select button
        canvas.SelectElement(button);

        // Assert selection adorner was added to AdornerSurface
        Assert.Single(canvas.AdornerSurface.Children);
        var adorner = canvas.AdornerSurface.Children[0] as SelectionAdorner;
        Assert.NotNull(adorner);
        Assert.Same(button, adorner.AssociatedElement);

        // Assert 8 thumbs exist as children in SelectionAdorner
        Assert.Equal(8, adorner.Children.Count);
        foreach (var child in adorner.Children)
        {
            Assert.IsType<Thumb>(child);
        }
    }
}
