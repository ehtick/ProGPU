using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;
using Xunit;

namespace ProGPU.Tests;

public sealed class MobileWindowApiTests
{
    [Fact]
    public void ExternalHostEventsUseWinUiWindowArgumentShapes()
    {
        var window = new Window();
        var activationStates = new List<WindowActivationState>();
        var visibilityStates = new List<bool>();
        var closedCount = 0;

        window.Activated += (_, args) =>
        {
            args.Handled = true;
            activationStates.Add(args.WindowActivationState);
        };
        window.VisibilityChanged += (_, args) =>
        {
            args.Handled = true;
            visibilityStates.Add(args.Visible);
        };
        window.Closed += (_, args) =>
        {
            args.Handled = true;
            closedCount++;
        };

        window.NotifyHostVisibilityChanged(true);
        window.NotifyHostActivationChanged(WindowActivationState.PointerActivated);
        window.NotifyHostVisibilityChanged(false);
        window.ShutdownExternalRenderer();
        window.ShutdownExternalRenderer();

        Assert.IsAssignableFrom<DependencyObject>(window);
        Assert.Equal([WindowActivationState.PointerActivated, WindowActivationState.Deactivated], activationStates);
        Assert.Equal([true, false], visibilityStates);
        Assert.Equal(1, closedCount);
    }

    [Fact]
    public void SetTitleBarAcceptsNullToRestoreTheSystemTitleBar()
    {
        var window = new Window();

        window.SetTitleBar(null);
        window.ShutdownExternalRenderer();
    }

    [Fact]
    public void LaunchArgumentsMatchTheWinUiStringContract()
    {
        var args = new LaunchActivatedEventArgs(["--page", "Text"]);

        Assert.Equal("--page Text", args.Arguments);
    }

    [Fact]
    public void ApplicationStartInvokesInitializationOnTheCallingThread()
    {
        int callingThread = Environment.CurrentManagedThreadId;
        int callbackThread = -1;

        Application.Start(_ => callbackThread = Environment.CurrentManagedThreadId);

        Assert.Equal(callingThread, callbackThread);
    }

    [Fact]
    public void SafeAreaAndDockedInputPaneProduceLogicalVisibleBounds()
    {
        var window = new Window { Width = 390, Height = 844 };
        var changes = new List<WindowInsets>();
        window.InsetsChanged += (_, args) => changes.Add(args.Insets);
        InputPane pane = InputPane.GetForWindow(window);
        int showing = 0;
        int hiding = 0;
        pane.Showing += (_, args) =>
        {
            args.EnsuredFocusedElementInView = true;
            showing++;
        };
        pane.Hiding += (_, _) => hiding++;

        window.NotifyHostInsetsChanged(
            new Thickness(0f, 59f, 0f, 34f),
            new Windows.Foundation.Rect(0d, 544d, 390d, 300d));

        Assert.Equal(new Thickness(0f, 59f, 0f, 34f), window.Insets.SafeArea);
        Assert.Equal(new Windows.Foundation.Rect(0d, 59d, 390d, 485d), window.Insets.VisibleBounds);
        Assert.True(pane.Visible);
        Assert.Equal(1, showing);

        window.NotifyHostInsetsChanged(new Thickness(0f, 59f, 0f, 34f), default);

        Assert.Equal(new Windows.Foundation.Rect(0d, 59d, 390d, 751d), window.Insets.VisibleBounds);
        Assert.False(pane.Visible);
        Assert.Equal(1, hiding);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void FloatingInputPaneReportsOcclusionWithoutShrinkingWholeWindow()
    {
        var window = new Window { Width = 390, Height = 844 };
        var floating = new Windows.Foundation.Rect(90d, 360d, 210d, 240d);

        window.NotifyHostInsetsChanged(new Thickness(0f, 59f, 0f, 34f), floating);

        Assert.Equal(floating, window.InputPane.OccludedRect);
        Assert.Equal(new Windows.Foundation.Rect(0d, 59d, 390d, 751d), window.Insets.VisibleBounds);
    }
}
