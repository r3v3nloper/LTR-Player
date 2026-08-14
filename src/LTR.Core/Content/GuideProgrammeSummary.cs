namespace LTR.Core.Content;

/// <summary>
/// The three things a channel row shows about a programme: what it is called and when it runs.
/// </summary>
/// <remarks>
/// <para>
/// Narrow on purpose. The channel list asks what is on now for every matched channel once a minute, and
/// answering with whole <see cref="EpgEntry"/> rows meant materialising a description of up to four thousand
/// characters — plus category, episode reference and icon — for each of roughly nine thousand rows, to
/// display two titles. None of it was read.
/// </para>
/// <para>
/// What that buys is measured, and it is allocation rather than time: against a real 42,000-programme guide
/// the query itself takes the same 25 milliseconds either way, because the database is in-process and
/// "transferring" a column is a memory copy. The nine thousand objects a minute are the saving, and they
/// matter to a window that stays open all evening rather than to any single refresh.
/// </para>
/// <para>
/// The full entry is still what a timeline and the programme detail use; those ask for far fewer rows and
/// show far more of each.
/// </para>
/// </remarks>
public sealed record GuideProgrammeSummary(string Title, DateTimeOffset StartUtc, DateTimeOffset StopUtc)
{
    public TimeSpan Duration => StopUtc - StartUtc;

    /// <summary>
    /// How far through the programme a given instant is, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Here rather than in the row that draws the bar, so the arithmetic is testable without a window. A
    /// zero-length programme cannot occur — the import rejects one — but dividing by it would take the
    /// window down rather than show an odd bar, so it is guarded anyway.
    /// </remarks>
    public double ProgressAt(DateTimeOffset atUtc)
    {
        var seconds = Duration.TotalSeconds;

        return seconds <= 0
            ? 0
            : Math.Clamp((atUtc - StartUtc).TotalSeconds / seconds, 0, 1);
    }
}
