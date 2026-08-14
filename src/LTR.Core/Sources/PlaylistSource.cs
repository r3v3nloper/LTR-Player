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
    /// When this source's programme guide was last imported.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="LastRefreshedUtc"/> because the two happen on different schedules: a
    /// guide is a download of tens to hundreds of megabytes and is not worth repeating with every
    /// catalogue refresh, so this is what decides whether one is due.
    /// </remarks>
    public DateTimeOffset? LastGuideImportedUtc { get; set; }

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

    /// <summary>
    /// Whether the provider keeps an account behind this source whose state can be asked about.
    /// </summary>
    /// <remarks>
    /// A panel reports a status, an expiry date and how many connections it currently counts, which is what
    /// makes a stream that would not open explainable. A playlist reports nothing of the sort: the document
    /// either downloads or it does not, and asking again costs a multi-megabyte download that answers a
    /// different question. Abstract rather than defaulted, so a further protocol has to say which it is.
    /// </remarks>
    public abstract bool ReportsAccountState { get; }
}
