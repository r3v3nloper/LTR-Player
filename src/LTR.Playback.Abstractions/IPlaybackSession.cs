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
/// Holds only what opens and releases a stream. What can be done to one already open is
/// <see cref="IPlaybackTransport"/>, which this deliberately does not inherit: a caller that should not be
/// able to open a connection takes that one, and then cannot. One object implements both.
/// </para>
/// </remarks>
public interface IPlaybackSession : IAsyncDisposable
{
    /// <summary>
    /// The stream currently held, or <see langword="null"/> when nothing is open.
    /// </summary>
    /// <remarks>
    /// Nothing in either application reads this; it is here because it states the guarantee the interface
    /// exists for, and it is what the session's own tests assert to prove a stream was let go.
    /// </remarks>
    MediaRequest? Current { get; }

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
