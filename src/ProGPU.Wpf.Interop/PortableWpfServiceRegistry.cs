using System.Reflection;

namespace ProGPU.Wpf.Interop;

public interface IPortableClipboardServiceRegistrar
{
    Assembly SourceAssembly { get; }

    IDisposable Register(Func<string?> getText, Action<string?> setText);

    void Clear();
}

public interface IPortableLauncherServiceRegistrar
{
    Assembly SourceAssembly { get; }

    IDisposable Register(Func<object, bool> launch);

    void Clear();
}

public interface IPortableMessageBoxServiceRegistrar
{
    Assembly SourceAssembly { get; }

    IDisposable Register(Func<object, object?> show);

    void Clear();
}

public interface IPortableFileDialogServiceRegistrar
{
    Assembly SourceAssembly { get; }

    IDisposable Register(Func<object, string?> showDialog);

    void Clear();
}

public interface IPortableMediaContextRenderServiceRegistrar
{
    Assembly SourceAssembly { get; }

    IDisposable Register(Action<object?, TimeSpan> requestRender);

    void Clear();
}

public static class PortableWpfServiceRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Assembly, IPortableClipboardServiceRegistrar> ClipboardServices = new();
    private static readonly Dictionary<Assembly, IPortableLauncherServiceRegistrar> LauncherServices = new();
    private static readonly Dictionary<Assembly, IPortableMessageBoxServiceRegistrar> MessageBoxServices = new();
    private static readonly Dictionary<Assembly, IPortableFileDialogServiceRegistrar> FileDialogServices = new();
    private static readonly Dictionary<Assembly, IPortableMediaContextRenderServiceRegistrar> MediaContextRenderServices = new();

    public static IDisposable RegisterClipboardService(IPortableClipboardServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (SyncRoot)
        {
            ClipboardServices[service.SourceAssembly] = service;
        }

        return new Registration<IPortableClipboardServiceRegistrar>(service, ClipboardServices);
    }

    public static bool TryGetClipboardService(
        Assembly sourceAssembly,
        out IPortableClipboardServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        lock (SyncRoot)
        {
            return ClipboardServices.TryGetValue(sourceAssembly, out service!);
        }
    }

    public static IDisposable RegisterLauncherService(IPortableLauncherServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (SyncRoot)
        {
            LauncherServices[service.SourceAssembly] = service;
        }

        return new Registration<IPortableLauncherServiceRegistrar>(service, LauncherServices);
    }

    public static bool TryGetLauncherService(
        Assembly sourceAssembly,
        out IPortableLauncherServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        lock (SyncRoot)
        {
            return LauncherServices.TryGetValue(sourceAssembly, out service!);
        }
    }

    public static IDisposable RegisterMessageBoxService(IPortableMessageBoxServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (SyncRoot)
        {
            MessageBoxServices[service.SourceAssembly] = service;
        }

        return new Registration<IPortableMessageBoxServiceRegistrar>(service, MessageBoxServices);
    }

    public static bool TryGetMessageBoxService(
        Assembly sourceAssembly,
        out IPortableMessageBoxServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        lock (SyncRoot)
        {
            return MessageBoxServices.TryGetValue(sourceAssembly, out service!);
        }
    }

    public static IDisposable RegisterFileDialogService(IPortableFileDialogServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (SyncRoot)
        {
            FileDialogServices[service.SourceAssembly] = service;
        }

        return new Registration<IPortableFileDialogServiceRegistrar>(service, FileDialogServices);
    }

    public static bool TryGetFileDialogService(
        Assembly sourceAssembly,
        out IPortableFileDialogServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        lock (SyncRoot)
        {
            return FileDialogServices.TryGetValue(sourceAssembly, out service!);
        }
    }

    public static IDisposable RegisterMediaContextRenderService(IPortableMediaContextRenderServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (SyncRoot)
        {
            MediaContextRenderServices[service.SourceAssembly] = service;
        }

        return new Registration<IPortableMediaContextRenderServiceRegistrar>(service, MediaContextRenderServices);
    }

    public static bool TryGetMediaContextRenderService(
        Assembly sourceAssembly,
        out IPortableMediaContextRenderServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);

        lock (SyncRoot)
        {
            return MediaContextRenderServices.TryGetValue(sourceAssembly, out service!);
        }
    }

    private sealed class Registration<TService> : IDisposable
        where TService : class
    {
        private readonly Dictionary<Assembly, TService> _services;
        private TService? _service;

        public Registration(TService service, Dictionary<Assembly, TService> services)
        {
            _service = service;
            _services = services;
        }

        public void Dispose()
        {
            var service = _service;
            if (service == null)
            {
                return;
            }

            _service = null;

            lock (SyncRoot)
            {
                var sourceAssembly = GetSourceAssembly(service);
                if (_services.TryGetValue(sourceAssembly, out var current) &&
                    ReferenceEquals(current, service))
                {
                    _services.Remove(sourceAssembly);
                }
            }
        }

        private static Assembly GetSourceAssembly(TService service)
        {
            return service switch
            {
                IPortableClipboardServiceRegistrar clipboardService => clipboardService.SourceAssembly,
                IPortableLauncherServiceRegistrar launcherService => launcherService.SourceAssembly,
                IPortableMessageBoxServiceRegistrar messageBoxService => messageBoxService.SourceAssembly,
                IPortableFileDialogServiceRegistrar fileDialogService => fileDialogService.SourceAssembly,
                IPortableMediaContextRenderServiceRegistrar renderService => renderService.SourceAssembly,
                _ => throw new InvalidOperationException("Unsupported portable WPF service registrar.")
            };
        }
    }
}
