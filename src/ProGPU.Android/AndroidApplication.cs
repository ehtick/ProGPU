using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.UI.Xaml;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace ProGPU.Android;

/// <summary>
/// Android activity base for a ProGPU application. Applications provide only the shared
/// WinUI launch callback; this base owns the native Android surface and lifecycle.
/// </summary>
public abstract class ProGpuActivity : Activity
{
    private AndroidRenderView? _renderView;
    private AndroidTextInputBridge? _textInput;
    private AndroidWindowHost? _host;
    private bool _isResumed;
    private bool _launchStarted;
    private bool _hasPaused;
    private bool _hasStopped;

    /// <summary>Launches the shared application after the Android window host is installed.</summary>
    protected abstract Task LaunchProGpuApplicationAsync();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is { } nativeWindow)
        {
            nativeWindow.SetSoftInputMode(SoftInput.AdjustNothing);
            nativeWindow.SetStatusBarColor(Color.Transparent);
            nativeWindow.SetNavigationBarColor(Color.Transparent);
            nativeWindow.StatusBarContrastEnforced = false;
            nativeWindow.NavigationBarContrastEnforced = false;
            nativeWindow.SetDecorFitsSystemWindows(false);
            if (nativeWindow.Attributes is { } attributes)
            {
                attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always;
                nativeWindow.Attributes = attributes;
            }
        }

        var root = new FrameLayout(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        _renderView = new AndroidRenderView(this);
        root.AddView(_renderView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        _textInput = new AndroidTextInputBridge(this);
        root.AddView(_textInput.NativeView, new FrameLayout.LayoutParams(1, 1));
        SetContentView(root);

        _host = new AndroidWindowHost(this, _renderView, _textInput);
        WindowHostServices.Current = _host;
        _renderView.SurfaceAvailable += OnInitialSurfaceAvailable;
    }

    protected override void OnStart()
    {
        base.OnStart();
        if (_hasStopped && XamlApplication.Current is { } application)
        {
            _hasStopped = false;
            ObserveLifecycleTask(application.NotifyHostLeavingBackgroundAsync(), "leaving background");
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isResumed = true;
        if (_hasPaused && XamlApplication.Current is { } application)
        {
            _hasPaused = false;
            application.NotifyHostResuming();
        }
        _host?.Resume();
        TryLaunchApplication();
    }

    protected override void OnPause()
    {
        _isResumed = false;
        _host?.Pause();
        _hasPaused = true;
        if (XamlApplication.Current is { } application)
            ObserveLifecycleTask(application.NotifyHostSuspendingAsync(), "suspending");
        base.OnPause();
    }

    protected override void OnStop()
    {
        _hasStopped = true;
        if (XamlApplication.Current is { } application)
            ObserveLifecycleTask(application.NotifyHostEnteredBackgroundAsync(), "entering background");
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        if (_renderView != null)
            _renderView.SurfaceAvailable -= OnInitialSurfaceAvailable;
        AndroidWindowHost? host = _host;
        _host = null;
        if (ReferenceEquals(WindowHostServices.Current, host)) WindowHostServices.Current = null;
        host?.Dispose();
        _textInput = null;
        _renderView = null;
        base.OnDestroy();
    }

    private void OnInitialSurfaceAvailable(Surface surface, int width, int height) =>
        TryLaunchApplication();

    private void TryLaunchApplication()
    {
        if (_launchStarted || !_isResumed || _renderView?.HasValidSurface != true) return;
        _launchStarted = true;
        _renderView.SurfaceAvailable -= OnInitialSurfaceAvailable;
        _ = ObserveLaunchAsync();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (_host?.HandleActivityResult(requestCode, resultCode, data) == true) return;
        base.OnActivityResult(requestCode, resultCode, data);
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e != null && _host?.HandleKeyEvent(e) == true) return true;
        return base.DispatchKeyEvent(e);
    }

    public override void OnBackPressed()
    {
        if (_host?.HandleBackRequested() == true) return;
#pragma warning disable CS0618
        base.OnBackPressed();
#pragma warning restore CS0618
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _renderView?.RefreshMetrics();
    }

    private async Task ObserveLaunchAsync()
    {
        try
        {
            await LaunchProGpuApplicationAsync();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("ProGPU.Android", exception.ToString());
            throw;
        }
    }

    private static async void ObserveLifecycleTask(Task task, string transition)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("ProGPU.Android", $"Application failed while {transition}: {exception}");
        }
    }
}
