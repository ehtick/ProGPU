using System;

namespace Windows.Media.Playback;

public interface IMediaPlaybackSource
{
}

public enum MediaPlaybackState
{
    None = 0,
    Opening = 1,
    Buffering = 2,
    Playing = 3,
    Paused = 4
}

/// <summary>
/// Typed playback state model used by ProGPU's platform media adapters.
/// </summary>
public class MediaPlayer
{
    private IMediaPlaybackSource? _source;
    private MediaPlaybackState _playbackState;

    public IMediaPlaybackSource? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
                return;
            _source = value;
            PlaybackState = value == null ? MediaPlaybackState.None : MediaPlaybackState.Opening;
        }
    }

    public MediaPlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            if (_playbackState == value)
                return;
            _playbackState = value;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? PlaybackStateChanged;

    public void Play()
    {
        if (Source != null)
            PlaybackState = MediaPlaybackState.Playing;
    }

    public void Pause()
    {
        if (PlaybackState == MediaPlaybackState.Playing)
            PlaybackState = MediaPlaybackState.Paused;
    }
}
