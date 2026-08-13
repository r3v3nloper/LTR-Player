using System.IO;
using System.IO.Compression;
using System.Text;

namespace LTR.Epg.Xmltv;

/// <summary>
/// Exercises the reader against the shapes real guides arrive in.
/// </summary>
/// <remarks>
/// Every case here is something a published guide actually does: a DTD declaration, several languages per
/// title, a missing end time, gzip with no indication of it, and a download that stopped halfway.
/// </remarks>
public sealed class XmltvStreamReaderTests
{
    private const string TwoChannelGuide = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE tv SYSTEM "xmltv.dtd">
        <tv generator-info-name="test">
          <channel id="tf1.fr">
            <display-name lang="fr">TF1</display-name>
            <display-name lang="en">TF One</display-name>
            <icon src="http://logos.example/tf1.png" />
          </channel>
          <channel id="ard.de">
            <display-name>ARD</display-name>
          </channel>
          <programme start="20260812183000 +0200" stop="20260812200000 +0200" channel="tf1.fr">
            <title lang="fr">Journal</title>
            <desc lang="fr">Les informations.</desc>
            <category lang="fr">Actualités</category>
            <episode-num system="onscreen">S2E14</episode-num>
            <icon src="http://images.example/journal.jpg" />
          </programme>
          <programme start="20260812200000 +0200" stop="20260812220000 +0200" channel="ard.de">
            <title>Tagesschau</title>
          </programme>
        </tv>
        """;

    [Fact]
    public async Task ReadAsync_ReadsChannelsAndProgrammes()
    {
        // Arrange
        var sink = new RecordingXmltvSink();

        // Act
        var result = await ReadAsync(TwoChannelGuide, sink);

        // Assert
        result.ChannelCount.ShouldBe(2);
        result.ProgrammeCount.ShouldBe(2);
        result.SkippedProgrammeCount.ShouldBe(0);
        result.WasTruncated.ShouldBeFalse();

        var channel = sink.Channels[0];
        channel.Id.ShouldBe("tf1.fr");
        channel.IconUrl.ShouldBe("http://logos.example/tf1.png");

        var programme = sink.Programmes[0];
        programme.ChannelId.ShouldBe("tf1.fr");
        programme.StartUtc.ShouldBe(DateTimeOffset.Parse("2026-08-12T16:30:00Z", CultureInfo.InvariantCulture));
        programme.StopUtc.ShouldBe(DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture));
        programme.Title.ShouldBe("Journal");
        programme.Description.ShouldBe("Les informations.");
        programme.Category.ShouldBe("Actualités");
        programme.EpisodeReference.ShouldBe("S2E14");
        programme.IconUrl.ShouldBe("http://images.example/journal.jpg");
    }

    /// <summary>
    /// A guide may name a channel once per language. The first is taken, because matching needs one name
    /// and the guide lists its own preference first.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TakesTheFirstOfSeveralDisplayNames()
    {
        // Arrange
        var sink = new RecordingXmltvSink();

        // Act
        await ReadAsync(TwoChannelGuide, sink);

        // Assert
        sink.Channels[0].DisplayName.ShouldBe("TF1");
    }

    [Fact]
    public async Task ReadAsync_ReportsAnAbsentStopTimeRatherThanGuessingOne()
    {
        // Arrange
        const string guide = """
            <tv>
              <programme start="20260812183000 +0200" channel="tf1.fr">
                <title>Journal</title>
              </programme>
            </tv>
            """;

        var sink = new RecordingXmltvSink();

        // Act
        var result = await ReadAsync(guide, sink);

        // Assert: the reader states what the document said; closing the gap is a separate decision.
        result.ProgrammeCount.ShouldBe(1);
        sink.Programmes[0].StopUtc.ShouldBeNull();
    }

    /// <summary>
    /// Panels serve <c>xmltv.php</c> gzipped without saying so, and an <c>.xml.gz</c> address is
    /// sometimes decompressed on the way. The first two bytes are the only reliable indication.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ReadsAGzippedGuide()
    {
        // Arrange
        using var compressed = new MemoryStream();

        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(TwoChannelGuide), TestContext.Current.CancellationToken);
        }

        compressed.Position = 0;
        var sink = new RecordingXmltvSink();

        // Act
        var result = await XmltvStreamReader.ReadAsync(compressed, sink, TestContext.Current.CancellationToken);

        // Assert
        result.ChannelCount.ShouldBe(2);
        result.ProgrammeCount.ShouldBe(2);
    }

    /// <summary>
    /// A download interrupted at 90% is 90% of a guide. Discarding it would turn a slow connection into no
    /// guide at all.
    /// </summary>
    [Fact]
    public async Task ReadAsync_KeepsWhatItReadFromATruncatedDocument()
    {
        // Arrange
        var truncated = TwoChannelGuide[..TwoChannelGuide.IndexOf("<programme start=\"20260812200000", StringComparison.Ordinal)];
        var sink = new RecordingXmltvSink();

        // Act
        var result = await ReadAsync(truncated, sink);

        // Assert
        result.WasTruncated.ShouldBeTrue();
        result.ProgrammeCount.ShouldBe(1);
        sink.Programmes.ShouldHaveSingleItem().Title.ShouldBe("Journal");
    }

    [Fact]
    public async Task ReadAsync_SkipsProgrammesItCannotUse()
    {
        // Arrange: no channel, an unreadable start, and no title — one of each.
        const string guide = """
            <tv>
              <programme start="20260812183000 +0200"><title>Orphan</title></programme>
              <programme start="rubbish" channel="tf1.fr"><title>Undated</title></programme>
              <programme start="20260812190000 +0200" channel="tf1.fr"></programme>
              <programme start="20260812200000 +0200" stop="20260812210000 +0200" channel="tf1.fr">
                <title>Usable</title>
              </programme>
            </tv>
            """;

        var sink = new RecordingXmltvSink();

        // Act
        var result = await ReadAsync(guide, sink);

        // Assert: counted and carried on, the way the playlist parser treats a bad line.
        result.SkippedProgrammeCount.ShouldBe(3);
        sink.Programmes.ShouldHaveSingleItem().Title.ShouldBe("Usable");
    }

    /// <summary>
    /// Descriptions arrive with markup in them and with CDATA around them, both of which
    /// <c>ReadElementContentAsString</c> would either throw over or mis-position the reader after.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ReadsTextThatContainsMarkupOrCdata()
    {
        // Arrange
        const string guide = """
            <tv>
              <programme start="20260812183000 +0200" stop="20260812200000 +0200" channel="tf1.fr">
                <title>Journal</title>
                <desc>Erste Zeile<br />zweite Zeile</desc>
                <category><![CDATA[Nachrichten]]></category>
              </programme>
            </tv>
            """;

        var sink = new RecordingXmltvSink();

        // Act
        await ReadAsync(guide, sink);

        // Assert
        var programme = sink.Programmes.ShouldHaveSingleItem();
        programme.Description.ShouldBe("Erste Zeilezweite Zeile");

        // The element after the one containing markup still has to be seen.
        programme.Category.ShouldBe("Nachrichten");
    }

    [Fact]
    public async Task ReadAsync_IgnoresChannelDeclarationsWithNoIdentifier()
    {
        // Arrange
        const string guide = """
            <tv>
              <channel><display-name>Nameless</display-name></channel>
              <channel id="ard.de"><display-name>ARD</display-name></channel>
            </tv>
            """;

        var sink = new RecordingXmltvSink();

        // Act
        var result = await ReadAsync(guide, sink);

        // Assert: nothing can reference it, so it is unusable rather than merely incomplete.
        result.ChannelCount.ShouldBe(1);
        sink.Channels.ShouldHaveSingleItem().Id.ShouldBe("ard.de");
    }

    private static async Task<XmltvReadResult> ReadAsync(string document, IXmltvSink sink)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));
        return await XmltvStreamReader.ReadAsync(stream, sink, TestContext.Current.CancellationToken);
    }
}
