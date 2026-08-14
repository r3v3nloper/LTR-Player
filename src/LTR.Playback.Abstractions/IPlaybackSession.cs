using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// The single point through which streams are opened, guaranteeing that at most one provider
/// connection is held at any time.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of the provider-side constraint, not for convenience: subscriptions
/// typically permit one or two concurrent connections, and a connection left open locks the
/// account out for minutes. Every playback request in the application goes through here so that
/// the ordering guarantee — stop fully, then start — cannot be bypassed.
/// </para>
/// <para>
/// It is also where the transport controls live — pausing, seeking, volume, track and aspect selection.
/// Those hold no connection and could have been read straight off the engine, but then the on-screen
/// controls would hold the engine, and an engine held in two places is how the ordering guarantee gets
/// bypassed by the next thing that needs "just one" call. Opening is still the privileged operation:
/// only <see cref="SwitchToAsync"/> starts a stream, and the shell has exactly one caller of it.
/// </para>
/// </remarks>
public interface IPlaybackSession : IAsyncDisposable
{
    PlaybackState State { get; }

    /// <summary>The stream currently held, or <see langword="null"/> when nothing is open.</summary>
    MediaRequest? Current { get; }

    /// <summary>
    /// How far into the current stream playback has reached, or <see langword="null"/> when there is no
    /// such thing — which is the normal answer for live television.
    /// </summary>
    /// <remarks>
    /// Surfaced here rather than leaving callers to reach for the engine, because the session is the one
    /// point through which playback is addressed and the engine is not meant to be held elsewhere.
    /// </remarks>
    TimeSpan? Position { get; }

    /// <summary>The current stream's total length, as the engine measures it.</summary>
    TimeSpan? Duration { get; }

    /// <summary>Whether the current stream can be positioned at all, which live streams cannot.</summary>
    bool IsSeekable { get; }

    /// <summary>Volume in percent, 0 to 100.</summary>
    int Volume { get; set; }

    bool IsMuted { get; set; }

    /// <summary>How the picture is fitted to the window. Survives a channel change.</summary>
    VideoAspectRatio AspectRatio { get; set; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Stops whatever is currently open, waits for its connection to be released, then opens
    /// <paramref name="request"/>. Concurrent calls are serialised; a call superseded by a newer
    /// one is abandoned rather than queued, so rapid channel changes do not pile up.
    /// </summary>
    Task<PlaybackState> SwitchToAsync(MediaRequest request, CancellationToken cancellationToken);

    /// <summary>Releases the current stream and returns once its connection is closed.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pauses or resumes what is open. Idempotent, and a no-op when nothing is.
    /// </summary>
    /// <remarks>
    /// Note that a paused stream still holds the provider connection — pausing live television is not a
    /// way to free one up.
    /// </remarks>
    void SetPaused(bool isPaused);

    /// <summary>
    /// Moves playback to <paramref name="position"/>, and does nothing when the stream cannot be
    /// positioned.
    /// </summary>
    void SeekTo(TimeSpan position);

    /// <summary>
    /// Tracks discovered in the stream currently open, which is empty until playback has started.
    /// </summary>
    IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind);

    /// <summary>
    /// Which track of <paramref name="kind"/> is playing, or <see cref="MediaTrack.DisabledId"/> when
    /// none is.
    /// </summary>
    int GetSelectedTrack(MediaTrackKind kind);

    /// <summary>
    /// Selects a track previously reported by <see cref="GetTracks"/>, or
    /// <see cref="MediaTrack.DisabledId"/> to switch the kind off.
    /// </summary>
    void SelectTrack(MediaTrackKind kind, int trackId);
}
