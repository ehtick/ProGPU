using System;
using System.Runtime.ExceptionServices;

namespace Microsoft.UI.Xaml;

#pragma warning disable CS0618
public delegate void ApplicationInitializationCallback(ApplicationInitializationCallbackParams parameters);
#pragma warning restore CS0618

[Obsolete("ApplicationInitializationCallbackParams is retained for WinUI API compatibility.")]
public sealed class ApplicationInitializationCallbackParams
{
    internal ApplicationInitializationCallbackParams()
    {
    }
}

public class LaunchActivatedEventArgs : EventArgs
{
    public string Arguments { get; }

    public LaunchActivatedEventArgs(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Arguments = string.Join(' ', args);
    }
}

public class Application
{
    public static Application Current { get; internal set; } = null!;

    public ResourceDictionary Resources { get; } = new();

    public event UnhandledExceptionEventHandler? UnhandledException;

    /// <summary>
    /// Invokes the framework initialization callback on the current UI thread.
    /// Platform hosts remain responsible for installing their dispatcher before calling this API.
    /// </summary>
    public static void Start(ApplicationInitializationCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
#pragma warning disable CS0618
        callback(new ApplicationInitializationCallbackParams());
#pragma warning restore CS0618
    }

    protected virtual void OnLaunched(LaunchActivatedEventArgs args)
    {
    }

    internal void Launch(LaunchActivatedEventArgs args)
    {
        try
        {
            OnLaunched(args);
        }
        catch (Exception ex)
        {
            var eventArgs = new UnhandledExceptionEventArgs { Exception = ex };
            UnhandledException?.Invoke(this, eventArgs);
            if (!eventArgs.Handled)
            {
                Console.Error.WriteLine($"[Application] Unhandled exception during launch: {ex}");
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }
    }
}
