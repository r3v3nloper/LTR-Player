namespace LTR.Epg.Xmltv;

/// <summary>
/// Gives every programme an end time, deriving it from the one that follows where the guide omitted it.
/// </summary>
/// <remarks>
/// <para>
/// XMLTV treats <c>stop</c> as optional and a good number of guides leave it out entirely, on the
/// understanding that a programme runs until the next one starts. Something has to apply that rule, and
/// the alternative — a nullable end time — would put "or until the next programme" into every query
/// that asks what is on now.
/// </para>
/// <para>
/// A decorator over the sink rather than a step in the reader, so the reader stays a reader: it reports
/// what the document said, and this states what to do about what it did not say. One programme per
/// channel is held back, which is what lets the following one supply the answer.
/// </para>
/// </remarks>
public sealed class XmltvStopTimeFiller : IXmltvSink
{
    /// <summary>
    /// Used for a programme with no successor to close it, and for one whose successor is implausibly
    /// far off.
    /// </summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// The longest run-time that will be inferred from a gap.
    /// </summary>
    /// <remarks>
    /// Guides have holes — a channel with an entry at 06:00 and nothing until 23:00 is not broadcasting
    /// a seventeen-hour programme. Beyond this the gap is taken as absence of information, and the
    /// entry gets the default duration so the channel reads as "nothing listed" instead of stating
    /// something false for most of the day.
    /// </remarks>
    public static readonly TimeSpan MaximumInferredDuration = TimeSpan.FromHours(6);

    private readonly IXmltvSink _inner;
    private readonly Dictionary<string, XmltvProgramme> _openPerChannel = new(StringComparer.Ordinal);

    public XmltvStopTimeFiller(IXmltvSink inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public ValueTask ChannelAsync(XmltvChannel channel, CancellationToken cancellationToken)
    {
        return _inner.ChannelAsync(channel, cancellationToken);
    }

    public async ValueTask ProgrammeAsync(XmltvProgramme programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        if (_openPerChannel.Remove(programme.ChannelId, out var open))
        {
            await _inner.ProgrammeAsync(Close(open, programme.StartUtc), cancellationToken).ConfigureAwait(false);
        }

        if (programme.StopUtc is null)
        {
            _openPerChannel[programme.ChannelId] = programme;
            return;
        }

        await _inner.ProgrammeAsync(programme, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the programmes still awaiting a successor. Must be called once the document has been
    /// read, or the last entry of every channel is lost.
    /// </summary>
    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        foreach (var open in _openPerChannel.Values)
        {
            await _inner.ProgrammeAsync(Close(open, nextStartUtc: null), cancellationToken).ConfigureAwait(false);
        }

        _openPerChannel.Clear();
    }

    private static XmltvProgramme Close(XmltvProgramme programme, DateTimeOffset? nextStartUtc)
    {
        var implied = nextStartUtc - programme.StartUtc;

        var duration = implied is { } gap && gap > TimeSpan.Zero && gap <= MaximumInferredDuration
            ? gap
            : DefaultDuration;

        return programme with { StopUtc = programme.StartUtc + duration };
    }
}
