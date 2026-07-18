namespace ProGPU.Scene;

/// <summary>
/// Stable identity for an immutable recorded picture whose GPU storage may be patched in place.
/// The owner must invalidate its visual after replacing the picture. Updates are expected on the
/// render/UI thread; the compositor never observes a partially replaced picture.
/// </summary>
public sealed class SceneFragmentHandle : IDisposable
{
    private GpuPicture? _picture;
    private long _version = 1;

    public SceneFragmentHandle(GpuPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        _picture = picture;
    }

    public long Version => Volatile.Read(ref _version);

    internal GpuPicture Picture =>
        Volatile.Read(ref _picture) ?? throw new ObjectDisposedException(nameof(SceneFragmentHandle));

    public void ReplacePicture(GpuPicture picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var previous = Interlocked.Exchange(ref _picture, picture);
        if (previous == null)
        {
            Interlocked.Exchange(ref _picture, null);
            picture.Dispose();
            throw new ObjectDisposedException(nameof(SceneFragmentHandle));
        }

        unchecked
        {
            var version = Interlocked.Increment(ref _version);
            if (version <= 0)
            {
                Interlocked.Exchange(ref _version, 1);
            }
        }
        previous.Dispose();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _picture, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    ~SceneFragmentHandle()
    {
        Interlocked.Exchange(ref _picture, null)?.Dispose();
    }
}
