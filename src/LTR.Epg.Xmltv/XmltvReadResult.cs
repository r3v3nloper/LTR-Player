namespace LTR.Epg.Xmltv;

/// <summary>
/// What one pass over an XMLTV document produced.
/// </summary>
/// <param name="ChannelCount">Channel declarations read.</param>
/// <param name="ProgrammeCount">Programmes handed to the sink.</param>
/// <param name="SkippedProgrammeCount">
/// Programmes discarded for lacking a channel reference, a readable start time or a title. Counted
/// rather than thrown over, in keeping with how the playlist parser treats a bad line: one malformed
/// entry must not cost the user a guide.
/// </param>
/// <param name="WasTruncated">
/// Whether the document ended mid-element. A guide download interrupted at 90% is still 90% of a guide,
/// so the reader keeps what it read and reports the truncation instead of discarding everything.
/// </param>
public sealed record XmltvReadResult(
    int ChannelCount,
    int ProgrammeCount,
    int SkippedProgrammeCount,
    bool WasTruncated);
