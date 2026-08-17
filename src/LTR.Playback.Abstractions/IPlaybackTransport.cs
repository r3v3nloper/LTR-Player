using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// Everything that can be done to a stream that is already open.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPlaybackSession"/>, and deliberately not inherited by it, because the division is
/// the one the whole playback design rests on. Nothing here opens or releases a provider connection: a
/// subscription permits very few concurrent connections, and the ordering rule that keeps within them holds
/// only while one thing does the starting.
/// </para>
/// <para>
/// So the on-screen controls take this and nothing else. M5 argued that in prose — "the overlay acts on a
/// stream already open" — and prose is not enforcement; a type is. What the overlay cannot see, it cannot
/// come to depend on the next time something needs "just one" call.
/// </para>
/// <para>
/// One object implements this and the session both. Splitting the implementation would buy nothing: every
/// member of either is the same delegation to the same engine.
/// </para>
/// </remarks>
public interface IPlaybackTransport
{
    PlaybackState State { get; }

    /// <summary>
    /// How far into the current stream playback has reached, or <see langword="null"/> when there is no
    /// such thing — which is the normal answer for live television.
    /// </summary>
    /// <remarks>
    /// Surfaced here rather than leaving callers to reach for the engine, because this is the one point
    /// through which playback is addressed and the engine is not meant to be held elsewhere.
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
