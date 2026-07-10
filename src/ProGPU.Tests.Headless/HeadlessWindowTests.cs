using System;
using System.IO;
using System.Numerics;
using Xunit;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;
using ProGPU.Vector;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace ProGPU.Tests.Headless;

[Collection("HeadlessTests")]
public class HeadlessWindowTests : IDisposable
{
    public void Dispose()
    {
    }

    [Fact]
    public void Test_HeadlessWindow_Initialization_And_Render()
    {
        uint width = 256;
        uint height = 256;

        var window = HeadlessWindow.Shared;
        window.Resize(width, height);
        Assert.NotNull(window.Context);
        Assert.NotNull(window.Compositor);
        Assert.Equal(width, window.Width);
        Assert.Equal(height, window.Height);

        // Create a solid red Border
        var border = new Border
        {
            Background = new SolidColorBrush(0xFF0000FF), // RGBA: Red = 255, Green = 0, Blue = 0, Alpha = 255
            Width = width,
            Height = height
        };

        window.Content = border;

        // Render the scene
        window.Render();

        // Retrieve pixels
        byte[] pixels = window.ReadPixels();
        Assert.NotNull(pixels);
        Assert.Equal((int)(width * height * 4), pixels.Length);

        // Check if there are colored pixels matching the red border.
        // The default clear color is dark (0.08f, 0.08f, 0.12f, 1.0f).
        // Since we draw a solid red border over the entire window, the pixels should be red (R=255, G=0, B=0, A=255).
        // Let's assert on a pixel in the center of the rendered image.
        int centerX = (int)width / 2;
        int centerY = (int)height / 2;
        int centerIndex = (centerY * (int)width + centerX) * 4;

        byte r = pixels[centerIndex + 0];
        byte g = pixels[centerIndex + 1];
        byte b = pixels[centerIndex + 2];
        byte a = pixels[centerIndex + 3];

        // Let's print out for diagnostic visibility
        Console.WriteLine($"Center pixel color: R={r}, G={g}, B={b}, A={a}");

        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
        Assert.Equal(255, a);

        // Cleanup
        window.Content = null;
    }

    [Fact]
    public void Test_HeadlessWindow_Screenshot_Export()
    {
        uint width = 512;
        uint height = 512;

        var window = HeadlessWindow.Shared;
        window.Resize(width, height);

        var border = new Border
        {
            Background = new SolidColorBrush(0x00FF00FF), // Green = 255, Alpha = 255
            Width = width,
            Height = height
        };

        window.Content = border;
        window.Render();

        string tempPath = Path.Combine(Path.GetTempPath(), "ProGPU_headless_test_screenshot.png");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        window.SaveScreenshot(tempPath);

        Assert.True(File.Exists(tempPath), "Screenshot PNG should be successfully created.");
        Assert.True(new FileInfo(tempPath).Length > 0, "PNG file should not be empty.");

        // Clean up the temporary screenshot file
        File.Delete(tempPath);

        // Cleanup
        window.Content = null;
    }

    [Fact]
    public void ResizingDefersMsaaResourcesUntilSubmittedWorkIsIdle()
    {
        using var window = new HeadlessWindow(32, 32);
        window.Content = new Border
        {
            Background = new SolidColorBrush(0x336699FF)
        };

        window.Render();
        for (uint size = 33; size <= 64; size++)
        {
            window.Resize(size, size + 1);
            window.Render();
        }

        lock (window.Context.DisposalLock)
        {
            Assert.NotEmpty(window.Context.PendingTextureViews);
            Assert.NotEmpty(window.Context.PendingTextures);
        }

        window.Context.CleanupPendingResources();
    }
}
