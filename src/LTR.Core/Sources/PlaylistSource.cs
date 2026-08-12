using LTR.Core.Content;

namespace LTR.Core.Sources;

/// <summary>
/// A configured subscription the user has added. Concrete subclasses carry the connection
/// details that differ per provider protocol.
/// </summary>
/// <remarks>
/// Modelled as a hierarchy rather than one class with nullable fields for every protocol, so
/// adding a further protocol (Stalker portals, for example) is a new subclass instead of an edit
/// to shared state.
/// </remarks>
public abstract class PlaylistSource
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// User agent sent to the provider. Many panels reject unknown agents or serve degraded
    /// responses to them, so this stays configurable per source.
    /// </summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    public StreamFormat PreferredStreamFormat { get; set; } = StreamFormat.MpegTs;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastRefreshedUtc { get; set; }

    /// <summary>
    /// What this particular panel turned out to support. Populated by a capability probe rather
    /// than assumed, because Xtream-compatible panels differ substantially.
    /// </summary>
    public ProviderCapabilities Capabilities { get; set; } = new();

    public ICollection<Category> Categories { get; set; } = [];

    public ICollection<Channel> Channels { get; set; } = [];

    /// <summary>
    /// A VLC-like agent, because that is what panels are most reliably configured to accept.
    /// </summary>
    public static string DefaultUserAgent => "VLC/3.0.21 LibVLC/3.0.21";

    /// <summary>Absolute address the provider is reached at, used for logging and de-duplication.</summary>
    public abstract Uri Endpoint { get; }
}
