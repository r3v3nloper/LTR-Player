namespace LTR.Epg.Xmltv;

public sealed class XmltvStopTimeFillerTests
{
    private static readonly DateTimeOffset SixPm =
        DateTimeOffset.Parse("2026-08-12T18:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task ProgrammeAsync_ClosesAnOpenEntryWithTheNextStart()
    {
        // Arrange
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);

        // Act
        await filler.ProgrammeAsync(Programme("News", SixPm, stopUtc: null), Token);
        await filler.ProgrammeAsync(Programme("Film", SixPm.AddMinutes(30), stopUtc: null), Token);
        await filler.CompleteAsync(Token);

        // Assert
        sink.Programmes.Count.ShouldBe(2);
        sink.Programmes[0].StopUtc.ShouldBe(SixPm.AddMinutes(30));

        // Nothing follows the last entry, so it gets the stated default rather than being dropped.
        sink.Programmes[1].StopUtc.ShouldBe(SixPm.AddMinutes(30) + XmltvStopTimeFiller.DefaultDuration);
    }

    /// <summary>
    /// Guides interleave channels, so an entry must be closed by the next entry of its own channel and
    /// not by whatever happened to be read next.
    /// </summary>
    [Fact]
    public async Task ProgrammeAsync_ClosesAnEntryFromItsOwnChannelOnly()
    {
        // Arrange
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);

        // Act
        await filler.ProgrammeAsync(Programme("News", SixPm, stopUtc: null, channelId: "one"), Token);
        await filler.ProgrammeAsync(Programme("Other", SixPm.AddMinutes(5), stopUtc: null, channelId: "two"), Token);
        await filler.ProgrammeAsync(Programme("Film", SixPm.AddHours(1), stopUtc: null, channelId: "one"), Token);
        await filler.CompleteAsync(Token);

        // Assert
        var news = sink.Programmes.Single(programme => programme.Title == "News");
        news.StopUtc.ShouldBe(SixPm.AddHours(1));
    }

    [Fact]
    public async Task ProgrammeAsync_LeavesAStatedStopTimeAlone()
    {
        // Arrange
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);
        var stated = SixPm.AddMinutes(20);

        // Act
        await filler.ProgrammeAsync(Programme("News", SixPm, stated), Token);
        await filler.CompleteAsync(Token);

        // Assert
        sink.Programmes.ShouldHaveSingleItem().StopUtc.ShouldBe(stated);
    }

    /// <summary>
    /// A gap of many hours is absence of information, not a programme of many hours. Inferring one would
    /// have the channel list state something false for most of the day.
    /// </summary>
    [Fact]
    public async Task ProgrammeAsync_DoesNotInferAnImplausiblyLongProgramme()
    {
        // Arrange
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);

        // Act
        await filler.ProgrammeAsync(Programme("Morning", SixPm, stopUtc: null), Token);
        await filler.ProgrammeAsync(Programme("Evening", SixPm.AddHours(12), stopUtc: null), Token);
        await filler.CompleteAsync(Token);

        // Assert
        sink.Programmes[0].StopUtc.ShouldBe(SixPm + XmltvStopTimeFiller.DefaultDuration);
    }

    [Fact]
    public async Task ProgrammeAsync_FallsBackToTheDefaultWhenTheNextEntryStartsEarlier()
    {
        // Arrange: an out-of-order document, which would otherwise produce a negative duration.
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);

        // Act
        await filler.ProgrammeAsync(Programme("Later", SixPm, stopUtc: null), Token);
        await filler.ProgrammeAsync(Programme("Earlier", SixPm.AddHours(-1), stopUtc: null), Token);
        await filler.CompleteAsync(Token);

        // Assert
        sink.Programmes[0].StopUtc.ShouldBe(SixPm + XmltvStopTimeFiller.DefaultDuration);
    }

    [Fact]
    public async Task ChannelAsync_PassesChannelDeclarationsStraightThrough()
    {
        // Arrange
        var sink = new RecordingXmltvSink();
        var filler = new XmltvStopTimeFiller(sink);

        // Act
        await filler.ChannelAsync(new XmltvChannel("tf1.fr", "TF1", null), Token);

        // Assert
        sink.Channels.ShouldHaveSingleItem().Id.ShouldBe("tf1.fr");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static XmltvProgramme Programme(
        string title,
        DateTimeOffset startUtc,
        DateTimeOffset? stopUtc,
        string channelId = "one")
    {
        return new XmltvProgramme(channelId, startUtc, stopUtc, title, null, null, null, null);
    }
}
