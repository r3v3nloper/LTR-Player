using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// The single point through which streams are opened, guaranteeing that at most one provider
/// connection is held at any time.
/// </summary>
/// <remarks>
/// This exists because of the provider-side constraint, not for convenience: subscriptions
/// typically permit one or two concurrent connections, and a connection left open locks the
/// account out for minutes. Every playback request in the application goes through here so that
/// the ordering guarantee — stop fully, then start — cannot be bypassed.
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

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Stops whatever is currently open, waits for its connection to be released, then opens
    /// <paramref name="request"/>. Concurrent calls are serialised; a call superseded by a newer
    /// one is abandoned rather than queued, so rapid channel changes do not pile up.
    /// </summary>
    Task<PlaybackState> SwitchToAsync(MediaRequest request, CancellationToken cancellationToken);

    /// <summary>Releases the current stream and returns once its connection is closed.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
