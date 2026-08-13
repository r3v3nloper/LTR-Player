using LTR.Core.Sources;

namespace LTR.Core.Content;

/// <summary>
/// One channel as the electronic programme guide names it, which is not the same thing as a
/// <see cref="Channel"/>.
/// </summary>
/// <remarks>
/// <para>
/// A guide is published independently of the channel list — a separate XMLTV document, often from a
/// different party — and it identifies its channels by its own identifiers and names. Modelling it as
/// its own entity is what allows a channel and its programmes to be joined by something other than a
/// shared identifier: most channels of a real subscription carry no guide id at all, so the join has to
/// survive being made by name.
/// </para>
/// <para>
/// Scoped to a source, because two subscriptions bring two guides whose identifiers mean different
/// things.
/// </para>
/// </remarks>
public sealed class GuideChannel
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public PlaylistSource? Source { get; set; }

    /// <summary>
    /// The identifier the guide uses — the <c>id</c> attribute of an XMLTV <c>&lt;channel&gt;</c>.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The name the guide displays. Empty for guides that declare no channels at all and are known only
    /// through the identifiers their programmes reference.
    /// </summary>
    public string? DisplayName { get; set; }

    public string? IconUrl { get; set; }

    public ICollection<EpgEntry> Entries { get; set; } = [];
}
