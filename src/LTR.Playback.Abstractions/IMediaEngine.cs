using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// A media engine capable of opening one stream at a time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StopAsync"/> completes only once the engine has genuinely released the stream, not
/// merely once a stop has been requested. Callers depend on that: an IPTV account permits very few
/// concurrent connections, so starting the next stream before the previous one is released gets
/// the account locked out by the provider.
/// </para>
/// <para>
/// Implementations are not thread-safe. Serialising access is the job of the playback session.
/// </para>
/// </remarks>
public interface IMediaEngine : IAsyncDisposable
{
    PlaybackState State { get; }

    /// <summary>Volume in percent, 0 to 100.</summary>
    int Volume { get; set; }

    bool IsMuted { get; set; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Opens <paramref name="request"/> and returns once the engine has begun playing or failed.
    /// </summary>
    Task PlayAsync(MediaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the current stream and returns once the provider connection is actually closed.
    /// Safe to call when nothing is playing.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pauses or resumes the current stream. Idempotent, and a no-op when nothing is open.
    /// </summary>
    /// <remarks>
    /// A single setter rather than a Pause/Resume pair, so there is no question of what happens
    /// when something already paused is paused again.
    /// </remarks>
    void SetPaused(bool isPaused);

    /// <summary>
    /// Tracks discovered in the stream currently open. Empty until playback has started, since
    /// MPEG-TS announces its tracks only as they are encountered.
    /// </summary>
    IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind);

    /// <summary>Selects a track previously reported by <see cref="GetTracks"/>.</summary>
    void SelectTrack(MediaTrackKind kind, int trackId);
}
