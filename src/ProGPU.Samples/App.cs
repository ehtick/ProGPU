using Microsoft.UI.Xaml;

namespace ProGPU.Samples;

public class App : Application
{
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window();
        window.Title = "ProGPU Substrate - High-Performance WinUI Gallery Dashboard";
        window.Width = 1280;
        window.Height = 800;
        window.GlyphAtlasSize = 2560;

        window.Activated += (s, e) =>
        {
            MainWindowController.Start(window);
        };

        window.Activate();
    }
}
