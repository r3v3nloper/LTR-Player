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

    /// <summary>
    /// How far into the current stream playback has reached, or <see langword="null"/> when the engine
    /// cannot say.
    /// </summary>
    /// <remarks>
    /// Null is the normal answer for a live stream, and also the answer for the first moments of a film
    /// before the engine has read enough of it. Callers therefore have to treat "no position" as an
    /// ordinary state rather than as a fault.
    /// </remarks>
    TimeSpan? Position { get; }

    /// <summary>
    /// The current stream's total length, or <see langword="null"/> when it has none or none is known.
    /// </summary>
    /// <remarks>
    /// This is the figure resume decisions are made against, and it is the engine's rather than the
    /// provider's on purpose: a panel's stated running time is frequently absent or simply wrong, while
    /// this one comes from the file being open.
    /// </remarks>
    TimeSpan? Duration { get; }

    /// <summary>Whether the current stream can be positioned at all, which live streams cannot.</summary>
    bool IsSeekable { get; }

    /// <summary>
    /// How the picture is fitted to the window.
    /// </summary>
    /// <remarks>
    /// Kept by the engine rather than reapplied by the caller on every stream, because engines reset it
    /// when media is opened and a viewer who corrected a stretched channel expects the correction to
    /// survive a channel change.
    /// </remarks>
    VideoAspectRatio AspectRatio { get; set; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Opens <paramref name="request"/> and returns once the engine has begun playing or failed.
    /// </summary>
    /// <remarks>
    /// An implementation honours <see cref="MediaRequest.StartAt"/> while opening rather than by seeking
    /// afterwards. The reason is stated on the property: a seek before the media is open is discarded,
    /// and one after it has opened shows a second of the beginning first.
    /// </remarks>
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
    /// Moves playback to <paramref name="position"/>, and does nothing when the stream cannot be
    /// positioned.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MediaRequest.StartAt"/>, which resumes a part-watched film while opening
    /// it. This is the one a seek bar uses, on a stream that is already playing; the distinction matters
    /// because the two are honoured at different moments and only the opening one can be relied upon to
    /// land before the first frame.
    /// </remarks>
    void SeekTo(TimeSpan position);

    /// <summary>
    /// Tracks discovered in the stream currently open. Empty until playback has started, since
    /// MPEG-TS announces its tracks only as they are encountered.
    /// </summary>
    IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind);

    /// <summary>
    /// Which track of <paramref name="kind"/> is playing, or <see cref="MediaTrack.DisabledId"/> when
    /// none is.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed, because the engine chooses the initial track and its choice is the one a
    /// menu has to show. A menu that presented the first listed track as selected would say "German" over a
    /// stream playing English, which is worse than offering no menu.
    /// </remarks>
    int GetSelectedTrack(MediaTrackKind kind);

    /// <summary>
    /// Selects a track previously reported by <see cref="GetTracks"/>, or
    /// <see cref="MediaTrack.DisabledId"/> to switch the kind off.
    /// </summary>
    void SelectTrack(MediaTrackKind kind, int trackId);
}
