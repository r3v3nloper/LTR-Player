namespace LTR.Epg.Xmltv;

/// <summary>
/// A <c>&lt;programme&gt;</c> element from an XMLTV document.
/// </summary>
/// <param name="ChannelId">The <c>channel</c> attribute, referring to a channel declaration.</param>
/// <param name="StartUtc">Start time, converted from whatever offset the document stated.</param>
/// <param name="StopUtc">
/// End time, or <see langword="null"/> when the document omitted it. Omission is common enough that
/// callers have to handle it; <see cref="XmltvStopTimeFiller"/> is what closes the gap.
/// </param>
/// <param name="Title">Programme title.</param>
/// <param name="Description">Synopsis, when present.</param>
/// <param name="Category">First genre given.</param>
/// <param name="EpisodeReference">Episode numbering exactly as published.</param>
/// <param name="IconUrl">Programme image, when present.</param>
public sealed record XmltvProgramme(
    string ChannelId,
    DateTimeOffset StartUtc,
    DateTimeOffset? StopUtc,
    string Title,
    string? Description,
    string? Category,
    string? EpisodeReference,
    string? IconUrl);
