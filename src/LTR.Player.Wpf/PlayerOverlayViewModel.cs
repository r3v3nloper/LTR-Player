using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// The controls drawn over the picture: where playback has reached, and everything that changes it.
/// </summary>
/// <remarks>
/// <para>
/// Takes <see cref="IPlaybackTransport"/> and not <see cref="IPlaybackSession"/>, which is what makes the
/// division structural rather than a matter of discipline: the coordinator decides *what* plays — it builds
/// addresses, opens streams and records positions — while everything here acts on a stream already open. This
/// class cannot start or release one, because the type it holds has no way to.
/// </para>
/// <para>
/// Nothing here subscribes to playback events, and that is deliberate. An engine raises them on its own
/// internal threads; WPF marshals a property change from another thread for a plain binding but not for a
/// collection, so a track list rebuilt from an engine callback would take the window down. Everything is
/// therefore read in <see cref="Sample"/>, which the window drives from a dispatcher timer.
/// </para>
/// </remarks>
public sealed partial class PlayerOverlayViewModel : ObservableObject
{
    /// <summary>
    /// How long the controls stay up after the last thing the viewer did.
    /// </summary>
    /// <remarks>
    /// Only while playing. A paused or opening stream keeps them up however long it takes, because a still
    /// picture with no controls on it reads as a frozen application.
    /// </remarks>
    public static readonly TimeSpan IdleBeforeHiding = TimeSpan.FromSeconds(4);

    /// <summary>How far a keyboard skip moves.</summary>
    public static readonly TimeSpan SkipStep = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How far the seek bar has to be dragged before letting go actually seeks.
    /// </summary>
    /// <remarks>
    /// A click on the bar without a drag leaves it where it already was, and honouring that as a seek would
    /// make an idle click re-buffer a film over HTTP for no reason.
    /// </remarks>
    public static readonly TimeSpan SeekTolerance = TimeSpan.FromSeconds(2);

    /// <summary>How much one press of the volume keys moves it.</summary>
    public const int VolumeStep = 5;

    private readonly IPlaybackTransport _playback;
    private readonly PlayerSettings.PlayerStateSettings _remembered;
    private readonly TimeProvider _timeProvider;

    private DateTimeOffset _lastActivity;

    /// <summary>The state at the previous sample, so a stream that has just started can be recognised.</summary>
    private PlaybackState _previousState = PlaybackState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isRevealed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _hasStream;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isPaused;

    /// <summary>
    /// Whether the stream can be positioned, which decides whether there is a seek bar at all.
    /// </summary>
    [ObservableProperty]
    private bool _isSeekable;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _positionLabel = string.Empty;

    [ObservableProperty]
    private string _durationLabel = string.Empty;

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private AspectRatioChoice _selectedAspectRatio = AspectRatioChoice.For(VideoAspectRatio.Source);

    /// <param name="settings">
    /// The player's own settings, whose remembered half this writes back as the viewer changes it.
    /// </param>
    /// <remarks>
    /// Taken as the live object rather than as values, so that whoever writes the settings file on the way out
    /// finds the current figures in it without this class knowing anything about files.
    /// </remarks>
    public PlayerOverlayViewModel(
        IPlaybackTransport playback,
        PlayerSettings settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var remembered = settings.Player;

        _playback = playback;
        _remembered = remembered;
        _timeProvider = timeProvider;
        _lastActivity = timeProvider.GetUtcNow();

        AudioTracks = new TrackSelectionViewModel(MediaTrackKind.Audio, canBeSwitchedOff: false, Select);
        SubtitleTracks = new TrackSelectionViewModel(MediaTrackKind.Subtitle, canBeSwitchedOff: true, Select);

        // Assigned through the properties, so each reaches the engine exactly as a viewer's change does.
        Volume = Math.Clamp(remembered.Volume, 0, 100);
        IsMuted = remembered.IsMuted;
        SelectedAspectRatio = AspectRatioChoice.For(remembered.AspectRatio);
    }

    public TrackSelectionViewModel AudioTracks { get; }

    public TrackSelectionViewModel SubtitleTracks { get; }

    /// <summary>
    /// Whether the controls are on screen.
    /// </summary>
    /// <remarks>
    /// Both halves are needed. Revealed alone would put a transport bar over the empty pane before anything
    /// is playing; a stream alone would leave the bar over the picture for the whole evening.
    /// </remarks>
    public bool IsVisible => IsRevealed && HasStream;

    /// <summary>
    /// Whether the seek bar is being dragged, which suspends the position being written from underneath it.
    /// </summary>
    public bool IsScrubbing { get; private set; }

    /// <summary>
    /// Brings the controls up, and keeps them up for as long as the viewer keeps doing things.
    /// </summary>
    /// <remarks>
    /// Called for any sign of life — a key, the mouse moving over the picture, a button pressed — and by the
    /// shell when a new stream is started, so that a channel change announces what it changed to.
    /// </remarks>
    public void Reveal()
    {
        _lastActivity = _timeProvider.GetUtcNow();
        IsRevealed = true;
    }

    /// <summary>
    /// Reads where playback has reached, and takes the controls away once nothing is happening.
    /// </summary>
    /// <remarks>
    /// Driven by the window's timer, which runs faster while these controls are visible than while they are
    /// not — the same timer either way, because the slow rate exists to keep a resume position current and
    /// that job does not stop when the overlay hides.
    /// </remarks>
    public void Sample()
    {
        var state = _playback.State;

        HasStream = state.HoldsProviderConnection();
        IsPaused = state == PlaybackState.Paused;
        IsSeekable = _playback.IsSeekable;

        // A stream that has just started needs the viewer's settings pushed at it: an engine is entitled to
        // forget them when it opens media, and the volume the viewer chose is not a per-channel decision.
        if (state == PlaybackState.Playing && _previousState != PlaybackState.Playing)
        {
            ApplyToStream();
        }

        _previousState = state;

        SampleTimes();
        SampleTracks();
        HideWhenIdle();
    }

    /// <summary>Pauses or resumes, and reflects it without waiting for the next sample.</summary>
    [RelayCommand]
    private void TogglePause()
    {
        Reveal();

        var wantsPause = !IsPaused;
        _playback.SetPaused(wantsPause);
        IsPaused = wantsPause;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        Reveal();
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void SkipForward()
    {
        Skip(SkipStep);
    }

    [RelayCommand]
    private void SkipBack()
    {
        Skip(-SkipStep);
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        Reveal();
        IsFullscreen = !IsFullscreen;
    }

    /// <summary>Leaves fullscreen, and does nothing when it is not on. Bound to Escape.</summary>
    public void LeaveFullscreen()
    {
        Reveal();
        IsFullscreen = false;
    }

    /// <summary>Moves the volume by <paramref name="delta"/> percent, clamped to the usable range.</summary>
    public void ChangeVolume(int delta)
    {
        Reveal();

        // Unmuted on the way up, because pressing volume-up on a muted player and hearing nothing reads as
        // the key not working.
        if (delta > 0)
        {
            IsMuted = false;
        }

        Volume = Math.Clamp(Volume + delta, 0, 100);
    }

    /// <summary>Moves the picture on to the next aspect ratio. Bound to a key, as the menu is a click.</summary>
    public void CycleAspectRatio()
    {
        Reveal();
        SelectedAspectRatio = AspectRatioChoice.After(SelectedAspectRatio.Value);
    }

    /// <summary>
    /// Moves playback by <paramref name="offset"/> from where it is now.
    /// </summary>
    /// <remarks>
    /// Measured from the engine's position rather than from the bar's, so a skip during a drag does not
    /// compound with where the thumb happens to be.
    /// </remarks>
    public void Skip(TimeSpan offset)
    {
        Reveal();

        if (_playback.Position is not { } current)
        {
            return;
        }

        _playback.SeekTo(current + offset);
        SampleTimes();
    }

    /// <summary>The viewer has taken hold of the seek bar.</summary>
    public void BeginScrub()
    {
        Reveal();
        IsScrubbing = true;
    }

    /// <summary>
    /// The viewer has let the seek bar go: move playback to where they left it.
    /// </summary>
    public void EndScrub()
    {
        IsScrubbing = false;
        Reveal();

        var target = TimeSpan.FromSeconds(PositionSeconds);
        var current = _playback.Position ?? TimeSpan.Zero;

        if ((target - current).Duration() < SeekTolerance)
        {
            return;
        }

        _playback.SeekTo(target);
        SampleTimes();
    }

    private void ApplyToStream()
    {
        _playback.Volume = Volume;
        _playback.IsMuted = IsMuted;
    }

    /// <remarks>
    /// The position is left alone while the bar is being dragged. Writing it would drag the thumb out from
    /// under the pointer twice a second, which makes the bar impossible to aim.
    /// </remarks>
    private void SampleTimes()
    {
        var position = _playback.Position ?? TimeSpan.Zero;
        var duration = _playback.Duration ?? TimeSpan.Zero;

        DurationSeconds = duration.TotalSeconds;
        DurationLabel = duration > TimeSpan.Zero ? DurationText.Format(duration) : string.Empty;

        if (IsScrubbing)
        {
            return;
        }

        PositionSeconds = position.TotalSeconds;
        PositionLabel = position > TimeSpan.Zero ? DurationText.Format(position) : string.Empty;
    }

    private void SampleTracks()
    {
        AudioTracks.Sync(
            _playback.GetTracks(MediaTrackKind.Audio),
            _playback.GetSelectedTrack(MediaTrackKind.Audio));

        SubtitleTracks.Sync(
            _playback.GetTracks(MediaTrackKind.Subtitle),
            _playback.GetSelectedTrack(MediaTrackKind.Subtitle));
    }

    private void HideWhenIdle()
    {
        if (!IsRevealed || _playback.State != PlaybackState.Playing)
        {
            return;
        }

        if (_timeProvider.GetUtcNow() - _lastActivity >= IdleBeforeHiding)
        {
            IsRevealed = false;
        }
    }

    private void Select(MediaTrackKind kind, int trackId)
    {
        Reveal();
        _playback.SelectTrack(kind, trackId);
    }

    /// <remarks>
    /// Each of these writes the engine and the remembered state together. Written on change rather than saved
    /// on change: the file is put out once, on the way out of the window, because a volume slider being
    /// dragged raises this a hundred times.
    /// </remarks>
    partial void OnVolumeChanged(int value)
    {
        _playback.Volume = value;
        _remembered.Volume = value;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _playback.IsMuted = value;
        _remembered.IsMuted = value;
    }

    partial void OnSelectedAspectRatioChanged(AspectRatioChoice value)
    {
        _playback.AspectRatio = value.Value;
        _remembered.AspectRatio = value.Value;
    }
}
