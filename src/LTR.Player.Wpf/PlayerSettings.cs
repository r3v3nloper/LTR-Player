using LTR.Playback;
using LTR.Playback.LibVlc;

namespace LTR.Player.Wpf;

/// <summary>
/// What the player remembers between sessions.
/// </summary>
/// <remarks>
/// <para>
/// A file rather than a table in the catalogue, and that is a hardening decision rather than a convenience.
/// The catalogue is treated as a cache that can be thrown away — an unreadable one is quarantined and the
/// player starts with an empty one — and settings that went with it would take the viewer's tuning along.
/// The file is also editable by hand, which matters when a bad value is what stops the window opening.
/// </para>
/// <para>
/// Mutable properties with an empty constructor, because this is deserialised. Not a record for that reason.
/// </para>
/// </remarks>
public sealed class PlayerSettings
{
    /// <summary>
    /// A fresh set of engine options, read for its defaults.
    /// </summary>
    /// <remarks>
    /// The values are taken from there rather than repeated here. Stating a caching figure in two places is
    /// exactly the duplication that had a startup argument reading as the effective setting while nothing
    /// consumed it, found in the review after M5.
    /// </remarks>
    private static readonly LibVlcOptions EngineDefaults = new();

    public PlaybackSettings Playback { get; set; } = new();

    public PlayerStateSettings Player { get; set; } = new();

    /// <summary>Tuning that only takes effect when the engine is next started.</summary>
    public sealed class PlaybackSettings
    {
        public int NetworkCachingMilliseconds { get; set; } = EngineDefaults.NetworkCachingMilliseconds;

        public int LiveNetworkCachingMilliseconds { get; set; } =
            EngineDefaults.LiveNetworkCachingMilliseconds;

        public HardwareDecoding HardwareDecoding { get; set; } = EngineDefaults.HardwareDecoding;
    }

    /// <summary>
    /// What the viewer last left the controls at.
    /// </summary>
    /// <remarks>
    /// Volume is the one anybody notices: a player that starts at full volume every evening gets turned down
    /// every evening.
    /// </remarks>
    public sealed class PlayerStateSettings
    {
        public int Volume { get; set; } = 100;

        public bool IsMuted { get; set; }

        public VideoAspectRatio AspectRatio { get; set; } = VideoAspectRatio.Source;
    }
}
