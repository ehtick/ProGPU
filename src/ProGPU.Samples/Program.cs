using System;
using Microsoft.UI.Xaml;

namespace ProGPU.Samples;

public static class Program
{
    public static void Main(string[] args)
    {
        AppBuilder<App>.Configure()
            .WithTitle("ProGPU Substrate - High-Performance WinUI Gallery Dashboard")
            .WithSize(1280, 800)
            .Build()
            .Run(args);
    }
}

public class App : Application
{
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window();
        window.Title = "ProGPU Substrate - High-Performance WinUI Gallery Dashboard";
        window.Width = 1280;
        window.Height = 800;
        window.GlyphAtlasSize = 3072;

        window.Activated += (s, e) =>
        {
            MainWindowController.Start(window);
        };

        window.Activate();
    }
}
