using System;
using System.Numerics;
using Microsoft.UI.Xaml.Media;
using ProGPU.Layout;
using ProGPU.Scene;
using Windows.Media.Playback;

namespace Microsoft.UI.Xaml.Controls;

/// <summary>
/// User-visible playback command surface for a MediaPlayerElement.
/// </summary>
public class MediaTransportControls : Control
{
    public static readonly DependencyProperty IsZoomButtonVisibleProperty = Register(nameof(IsZoomButtonVisible), true);
    public static readonly DependencyProperty IsZoomEnabledProperty = Register(nameof(IsZoomEnabled), true);
    public static readonly DependencyProperty IsFastForwardButtonVisibleProperty = Register(nameof(IsFastForwardButtonVisible), true);
    public static readonly DependencyProperty IsFastForwardEnabledProperty = Register(nameof(IsFastForwardEnabled), true);
    public static readonly DependencyProperty IsFastRewindButtonVisibleProperty = Register(nameof(IsFastRewindButtonVisible), true);
    public static readonly DependencyProperty IsFastRewindEnabledProperty = Register(nameof(IsFastRewindEnabled), true);
    public static readonly DependencyProperty IsStopButtonVisibleProperty = Register(nameof(IsStopButtonVisible), true);
    public static readonly DependencyProperty IsStopEnabledProperty = Register(nameof(IsStopEnabled), true);
    public static readonly DependencyProperty IsVolumeButtonVisibleProperty = Register(nameof(IsVolumeButtonVisible), true);
    public static readonly DependencyProperty IsVolumeEnabledProperty = Register(nameof(IsVolumeEnabled), true);
    public static readonly DependencyProperty IsPlaybackRateButtonVisibleProperty = Register(nameof(IsPlaybackRateButtonVisible), true);
    public static readonly DependencyProperty IsPlaybackRateEnabledProperty = Register(nameof(IsPlaybackRateEnabled), true);
    public static readonly DependencyProperty IsSeekBarVisibleProperty = Register(nameof(IsSeekBarVisible), true);
    public static readonly DependencyProperty IsSeekEnabledProperty = Register(nameof(IsSeekEnabled), true);
    public static readonly DependencyProperty IsCompactProperty = Register(nameof(IsCompact), false);
    public static readonly DependencyProperty IsSkipForwardButtonVisibleProperty = Register(nameof(IsSkipForwardButtonVisible), false);
    public static readonly DependencyProperty IsSkipForwardEnabledProperty = Register(nameof(IsSkipForwardEnabled), true);
    public static readonly DependencyProperty IsSkipBackwardButtonVisibleProperty = Register(nameof(IsSkipBackwardButtonVisible), false);
    public static readonly DependencyProperty IsSkipBackwardEnabledProperty = Register(nameof(IsSkipBackwardEnabled), true);
    public static readonly DependencyProperty IsNextTrackButtonVisibleProperty = Register(nameof(IsNextTrackButtonVisible), false);
    public static readonly DependencyProperty IsPreviousTrackButtonVisibleProperty = Register(nameof(IsPreviousTrackButtonVisible), false);
    public static readonly DependencyProperty FastPlayFallbackBehaviourProperty = Register(nameof(FastPlayFallbackBehaviour), FastPlayFallbackBehaviour.Skip);
    public static readonly DependencyProperty ShowAndHideAutomaticallyProperty = Register(nameof(ShowAndHideAutomatically), true);
    public static readonly DependencyProperty IsRepeatEnabledProperty = Register(nameof(IsRepeatEnabled), true);
    public static readonly DependencyProperty IsRepeatButtonVisibleProperty = Register(nameof(IsRepeatButtonVisible), false);

    public bool IsZoomButtonVisible { get => GetBool(IsZoomButtonVisibleProperty); set => SetValue(IsZoomButtonVisibleProperty, value); }
    public bool IsZoomEnabled { get => GetBool(IsZoomEnabledProperty); set => SetValue(IsZoomEnabledProperty, value); }
    public bool IsFastForwardButtonVisible { get => GetBool(IsFastForwardButtonVisibleProperty); set => SetValue(IsFastForwardButtonVisibleProperty, value); }
    public bool IsFastForwardEnabled { get => GetBool(IsFastForwardEnabledProperty); set => SetValue(IsFastForwardEnabledProperty, value); }
    public bool IsFastRewindButtonVisible { get => GetBool(IsFastRewindButtonVisibleProperty); set => SetValue(IsFastRewindButtonVisibleProperty, value); }
    public bool IsFastRewindEnabled { get => GetBool(IsFastRewindEnabledProperty); set => SetValue(IsFastRewindEnabledProperty, value); }
    public bool IsStopButtonVisible { get => GetBool(IsStopButtonVisibleProperty); set => SetValue(IsStopButtonVisibleProperty, value); }
    public bool IsStopEnabled { get => GetBool(IsStopEnabledProperty); set => SetValue(IsStopEnabledProperty, value); }
    public bool IsVolumeButtonVisible { get => GetBool(IsVolumeButtonVisibleProperty); set => SetValue(IsVolumeButtonVisibleProperty, value); }
    public bool IsVolumeEnabled { get => GetBool(IsVolumeEnabledProperty); set => SetValue(IsVolumeEnabledProperty, value); }
    public bool IsPlaybackRateButtonVisible { get => GetBool(IsPlaybackRateButtonVisibleProperty); set => SetValue(IsPlaybackRateButtonVisibleProperty, value); }
    public bool IsPlaybackRateEnabled { get => GetBool(IsPlaybackRateEnabledProperty); set => SetValue(IsPlaybackRateEnabledProperty, value); }
    public bool IsSeekBarVisible { get => GetBool(IsSeekBarVisibleProperty); set => SetValue(IsSeekBarVisibleProperty, value); }
    public bool IsSeekEnabled { get => GetBool(IsSeekEnabledProperty); set => SetValue(IsSeekEnabledProperty, value); }
    public bool IsCompact { get => GetBool(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public bool IsSkipForwardButtonVisible { get => GetBool(IsSkipForwardButtonVisibleProperty); set => SetValue(IsSkipForwardButtonVisibleProperty, value); }
    public bool IsSkipForwardEnabled { get => GetBool(IsSkipForwardEnabledProperty); set => SetValue(IsSkipForwardEnabledProperty, value); }
    public bool IsSkipBackwardButtonVisible { get => GetBool(IsSkipBackwardButtonVisibleProperty); set => SetValue(IsSkipBackwardButtonVisibleProperty, value); }
    public bool IsSkipBackwardEnabled { get => GetBool(IsSkipBackwardEnabledProperty); set => SetValue(IsSkipBackwardEnabledProperty, value); }
    public bool IsNextTrackButtonVisible { get => GetBool(IsNextTrackButtonVisibleProperty); set => SetValue(IsNextTrackButtonVisibleProperty, value); }
    public bool IsPreviousTrackButtonVisible { get => GetBool(IsPreviousTrackButtonVisibleProperty); set => SetValue(IsPreviousTrackButtonVisibleProperty, value); }
    public FastPlayFallbackBehaviour FastPlayFallbackBehaviour { get => (FastPlayFallbackBehaviour)(GetValue(FastPlayFallbackBehaviourProperty) ?? FastPlayFallbackBehaviour.Skip); set => SetValue(FastPlayFallbackBehaviourProperty, value); }
    public bool ShowAndHideAutomatically { get => GetBool(ShowAndHideAutomaticallyProperty); set => SetValue(ShowAndHideAutomaticallyProperty, value); }
    public bool IsRepeatEnabled { get => GetBool(IsRepeatEnabledProperty); set => SetValue(IsRepeatEnabledProperty, value); }
    public bool IsRepeatButtonVisible { get => GetBool(IsRepeatButtonVisibleProperty); set => SetValue(IsRepeatButtonVisibleProperty, value); }

    public void Show() => Visibility = Visibility.Visible;
    public void Hide() => Visibility = Visibility.Collapsed;

    private bool GetBool(DependencyProperty property) => (bool)(GetValue(property) ?? false);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(MediaTransportControls),
            new PropertyMetadata(defaultValue) { AffectsMeasure = true, AffectsRender = true });
}

/// <summary>
/// Visual host for frames produced by a typed MediaPlayer adapter.
/// </summary>
public class MediaPlayerPresenter : FrameworkElement
{
    public static readonly DependencyProperty MediaPlayerProperty = Register<MediaPlayer?>(nameof(MediaPlayer), null, OnMediaPlayerChanged);
    public static readonly DependencyProperty StretchProperty = Register(nameof(Stretch), Stretch.Uniform);
    public static readonly DependencyProperty IsFullWindowProperty = Register(nameof(IsFullWindow), false);

    public MediaPlayer? MediaPlayer { get => GetValue(MediaPlayerProperty) as MediaPlayer; set => SetValue(MediaPlayerProperty, value); }
    public Stretch Stretch { get => (Stretch)(GetValue(StretchProperty) ?? Stretch.Uniform); set => SetValue(StretchProperty, value); }
    public bool IsFullWindow { get => (bool)(GetValue(IsFullWindowProperty) ?? false); set => SetValue(IsFullWindowProperty, value); }

    protected override Vector2 MeasureOverride(Vector2 availableSize) =>
        new(
            float.IsFinite(availableSize.X) ? availableSize.X : 320f,
            float.IsFinite(availableSize.Y) ? availableSize.Y : 180f);

    private static void OnMediaPlayerChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (MediaPlayerPresenter)dependencyObject;
        if (args.OldValue is MediaPlayer oldPlayer)
            oldPlayer.PlaybackStateChanged -= presenter.OnPlaybackStateChanged;
        if (args.NewValue is MediaPlayer newPlayer)
            newPlayer.PlaybackStateChanged += presenter.OnPlaybackStateChanged;
        presenter.Invalidate();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs args) => Invalidate();

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(MediaPlayerPresenter),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsRender = true });
}

/// <summary>
/// Owns media playback state, presentation, and transport controls.
/// </summary>
public class MediaPlayerElement : Control
{
    private readonly MediaPlayerPresenter _presenter;
    private MediaPlayer _mediaPlayer;

    public static readonly DependencyProperty SourceProperty = Register<IMediaPlaybackSource?>(nameof(Source), null, OnSourceChanged);
    public static readonly DependencyProperty TransportControlsProperty = Register<MediaTransportControls?>(nameof(TransportControls), null, OnTransportControlsChanged);
    public static readonly DependencyProperty AreTransportControlsEnabledProperty = Register(nameof(AreTransportControlsEnabled), true, OnTransportControlsEnabledChanged);
    public static readonly DependencyProperty PosterSourceProperty = Register<ImageSource?>(nameof(PosterSource), null);
    public static readonly DependencyProperty StretchProperty = Register(nameof(Stretch), Stretch.Uniform, OnStretchChanged);
    public static readonly DependencyProperty AutoPlayProperty = Register(nameof(AutoPlay), false, OnAutoPlayChanged);
    public static readonly DependencyProperty IsFullWindowProperty = Register(nameof(IsFullWindow), false, OnFullWindowChanged);
    public static readonly DependencyProperty MediaPlayerProperty = Register<MediaPlayer?>(nameof(MediaPlayer), null);

    public MediaPlayerElement()
    {
        _mediaPlayer = new MediaPlayer();
        _presenter = new MediaPlayerPresenter { MediaPlayer = _mediaPlayer };
        AddChild(_presenter);
        TransportControls = new MediaTransportControls();
        SetValue(MediaPlayerProperty, _mediaPlayer);
    }

    public IMediaPlaybackSource? Source { get => GetValue(SourceProperty) as IMediaPlaybackSource; set => SetValue(SourceProperty, value); }
    public MediaTransportControls? TransportControls { get => GetValue(TransportControlsProperty) as MediaTransportControls; set => SetValue(TransportControlsProperty, value); }
    public bool AreTransportControlsEnabled { get => (bool)(GetValue(AreTransportControlsEnabledProperty) ?? true); set => SetValue(AreTransportControlsEnabledProperty, value); }
    public ImageSource? PosterSource { get => GetValue(PosterSourceProperty) as ImageSource; set => SetValue(PosterSourceProperty, value); }
    public Stretch Stretch { get => (Stretch)(GetValue(StretchProperty) ?? Stretch.Uniform); set => SetValue(StretchProperty, value); }
    public bool AutoPlay { get => (bool)(GetValue(AutoPlayProperty) ?? false); set => SetValue(AutoPlayProperty, value); }
    public bool IsFullWindow { get => (bool)(GetValue(IsFullWindowProperty) ?? false); set => SetValue(IsFullWindowProperty, value); }
    public MediaPlayer MediaPlayer => _mediaPlayer;

    public void SetMediaPlayer(MediaPlayer mediaPlayer)
    {
        ArgumentNullException.ThrowIfNull(mediaPlayer);
        _mediaPlayer = mediaPlayer;
        SetValue(MediaPlayerProperty, mediaPlayer);
        _presenter.MediaPlayer = mediaPlayer;
        mediaPlayer.Source = Source;
        if (AutoPlay)
            mediaPlayer.Play();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _presenter.Measure(availableSize);
        TransportControls?.Measure(availableSize);
        return _presenter.DesiredSize;
    }

    protected override void ArrangeOverride(Rect arrangeRect)
    {
        _presenter.Arrange(arrangeRect);
        TransportControls?.Arrange(arrangeRect);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var element = (MediaPlayerElement)dependencyObject;
        element._mediaPlayer.Source = args.NewValue as IMediaPlaybackSource;
        if (element.AutoPlay)
            element._mediaPlayer.Play();
    }

    private static void OnTransportControlsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var element = (MediaPlayerElement)dependencyObject;
        if (args.OldValue is MediaTransportControls oldControls && ReferenceEquals(oldControls.Parent, element))
            element.RemoveChild(oldControls);
        if (args.NewValue is MediaTransportControls newControls && element.AreTransportControlsEnabled)
            element.AddChild(newControls);
        element.InvalidateMeasure();
        element.Invalidate();
    }

    private static void OnTransportControlsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var element = (MediaPlayerElement)dependencyObject;
        var controls = element.TransportControls;
        if (controls == null)
            return;

        if ((bool)(args.NewValue ?? true))
        {
            if (!ReferenceEquals(controls.Parent, element))
                element.AddChild(controls);
        }
        else if (ReferenceEquals(controls.Parent, element))
        {
            element.RemoveChild(controls);
        }

        element.InvalidateMeasure();
        element.Invalidate();
    }

    private static void OnStretchChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((MediaPlayerElement)dependencyObject)._presenter.Stretch = (Stretch)(args.NewValue ?? Stretch.Uniform);

    private static void OnAutoPlayChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var element = (MediaPlayerElement)dependencyObject;
        if ((bool)(args.NewValue ?? false))
            element._mediaPlayer.Play();
    }

    private static void OnFullWindowChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args) =>
        ((MediaPlayerElement)dependencyObject)._presenter.IsFullWindow = (bool)(args.NewValue ?? false);

    private static DependencyProperty Register<T>(
        string name,
        T defaultValue,
        PropertyChangedCallback? callback = null) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(MediaPlayerElement),
            new PropertyMetadata(defaultValue, callback) { AffectsMeasure = true, AffectsArrange = true, AffectsRender = true });
}
