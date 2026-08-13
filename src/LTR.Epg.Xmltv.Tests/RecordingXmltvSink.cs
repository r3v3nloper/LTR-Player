namespace LTR.Epg.Xmltv;

/// <summary>
/// Collects what the reader pushes, so a test can assert on it.
/// </summary>
internal sealed class RecordingXmltvSink : IXmltvSink
{
    public List<XmltvChannel> Channels { get; } = [];

    public List<XmltvProgramme> Programmes { get; } = [];

    public ValueTask ChannelAsync(XmltvChannel channel, CancellationToken cancellationToken)
    {
        Channels.Add(channel);
        return ValueTask.CompletedTask;
    }

    public ValueTask ProgrammeAsync(XmltvProgramme programme, CancellationToken cancellationToken)
    {
        Programmes.Add(programme);
        return ValueTask.CompletedTask;
    }
}
