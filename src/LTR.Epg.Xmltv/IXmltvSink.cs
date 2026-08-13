namespace LTR.Epg.Xmltv;

/// <summary>
/// Receives the contents of an XMLTV document as they are read.
/// </summary>
/// <remarks>
/// The reader pushes rather than returning a collection, and that is the whole point of it: a guide is
/// tens to hundreds of megabytes and holding its programmes as objects would cost more memory than the
/// document itself. A sink can store each batch and forget it, which is what keeps the import flat in
/// memory whatever the size of the guide.
/// </remarks>
public interface IXmltvSink
{
    ValueTask ChannelAsync(XmltvChannel channel, CancellationToken cancellationToken);

    ValueTask ProgrammeAsync(XmltvProgramme programme, CancellationToken cancellationToken);
}
