namespace LTR.Core.Content;

/// <summary>
/// One programme in the guide.
/// </summary>
public sealed class EpgEntry
{
    public int Id { get; set; }

    public int GuideChannelId { get; set; }
    public GuideChannel? GuideChannel { get; set; }

    public DateTimeOffset StartUtc { get; set; }

    /// <summary>
    /// When the programme ends.
    /// </summary>
    /// <remarks>
    /// Never null, although XMLTV allows the <c>stop</c> attribute to be omitted. A nullable end would
    /// push "until the next programme starts" into every query that asks what is on now, so the gap is
    /// closed once during import instead.
    /// </remarks>
    public DateTimeOffset StopUtc { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Genre as the guide states it, such as "Sport" or "Nachrichten".</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Episode reference as published, in whatever notation the guide used.
    /// </summary>
    /// <remarks>
    /// Kept as text rather than parsed into season and episode numbers: XMLTV permits several
    /// numbering systems side by side, and a wrong parse of <c>0.4.0/2</c> is worse than showing what
    /// the guide actually said.
    /// </remarks>
    public string? EpisodeReference { get; set; }

    public string? IconUrl { get; set; }

    public TimeSpan Duration => StopUtc - StartUtc;

    /// <summary>Whether <paramref name="instant"/> falls inside this programme.</summary>
    public bool Covers(DateTimeOffset instant)
    {
        return StartUtc <= instant && instant < StopUtc;
    }
}
