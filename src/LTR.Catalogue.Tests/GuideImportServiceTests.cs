using LTR.Core;
using LTR.Core.Content;
using LTR.Core.Security;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Catalogue;

/// <summary>
/// Covers the guide import end to end, from an XMLTV document to channels that show programmes.
/// </summary>
/// <remarks>
/// Only the provider boundary is faked. The reader, the batch writer, the matcher and the store are the
/// real ones, because what is under test is how they fit together — a guide that parses, stores and
/// matches in isolation can still produce a player with no programmes in it.
/// </remarks>
public sealed class GuideImportServiceTests : IAsyncDisposable
{
    private const string Guide = """
        <tv>
          <channel id="tf1.fr"><display-name>TF1</display-name></channel>
          <channel id="ard.de"><display-name>ARD</display-name></channel>
          <programme start="20260812170000 +0000" stop="20260812180000 +0000" channel="tf1.fr">
            <title>Before</title>
          </programme>
          <programme start="20260812180000 +0000" stop="20260812190000 +0000" channel="tf1.fr">
            <title>Running</title>
          </programme>
          <programme start="20260812190000 +0000" stop="20260812200000 +0000" channel="tf1.fr">
            <title>Next</title>
          </programme>
          <programme start="20260812180000 +0000" stop="20260812200000 +0000" channel="ard.de">
            <title>Tagesschau</title>
          </programme>
        </tv>
        """;

    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly AdjustableTimeProvider _time = new(SixPm);
    private ServiceProvider? _services;

    [Fact]
    public async Task ImportAsync_StoresTheGuideAndMatchesItToTheChannels()
    {
        // Arrange: the channel names differ from the guide's, which is the normal case and the reason
        // matching exists at all.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["FR: TF1 HD", "DE: ARD FHD"], cancellationToken);
        registry.GuideDocument = Guide;

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.ProgrammeCount.ShouldBe(4);
        result.MatchedChannelCount.ShouldBe(2);
        result.WasTruncated.ShouldBeFalse();
        result.Summary!.CoverageUntilUtc.ShouldBe(SixPm.AddHours(2));
    }

    [Fact]
    public async Task ImportAsync_MakesTheGuideReadableAsNowAndNext()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["FR: TF1 HD"], cancellationToken);
        registry.GuideDocument = Guide;

        await _services!.GetRequiredService<IGuideImportService>()
            .ImportAsync(source, progress: null, cancellationToken);

        // Act
        var slices = await _services!.GetRequiredService<ICatalogueStore>()
            .GetNowAndNextAsync(source.Id, SixPm, cancellationToken);

        // Assert
        var slice = slices.ShouldHaveSingleItem();
        slice.Now!.Title.ShouldBe("Running");
        slice.Next!.Title.ShouldBe("Next");
    }

    /// <summary>
    /// Guides carry days of history no view here shows. Keeping it would grow the table on every import
    /// and never shrink it.
    /// </summary>
    [Fact]
    public async Task ImportAsync_DiscardsProgrammesThatEndedLongAgo()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);

        registry.GuideDocument = """
            <tv>
              <channel id="tf1.fr"><display-name>TF1</display-name></channel>
              <programme start="20260805180000 +0000" stop="20260805190000 +0000" channel="tf1.fr">
                <title>Last week</title>
              </programme>
              <programme start="20260812180000 +0000" stop="20260812190000 +0000" channel="tf1.fr">
                <title>Tonight</title>
              </programme>
            </tv>
            """;

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.ProgrammeCount.ShouldBe(1);
        result.Summary!.ProgrammeCount.ShouldBe(1);
    }

    /// <summary>
    /// A reimport has to replace what it stored before. Adding to it would double every listing each time
    /// the guide is refreshed.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RunTwice_DoesNotDuplicateAnything()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1", "ARD"], cancellationToken);
        registry.GuideDocument = Guide;

        var guide = _services!.GetRequiredService<IGuideImportService>();
        await guide.ImportAsync(source, progress: null, cancellationToken);

        // Act
        var second = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        second.Summary!.ProgrammeCount.ShouldBe(4);
        second.Summary.GuideChannelCount.ShouldBe(2);
    }

    /// <summary>
    /// Batching is where a 200 MB guide stays affordable, and a programme whose channel spans two batches
    /// is the case that would silently lose rows if the per-channel replacement were applied twice.
    /// </summary>
    [Fact]
    public async Task ImportAsync_KeepsEveryProgrammeWhenAChannelSpansSeveralBatches()
    {
        // Arrange: comfortably more programmes than one batch holds.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = BuildLongGuide(programmeCount: 4_500, slot: TimeSpan.FromMinutes(5));

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.ProgrammeCount.ShouldBe(4_500);
        result.Summary!.ProgrammeCount.ShouldBe(4_500);
    }

    /// <summary>
    /// Guides reference channels they never declare. Dropping those programmes would lose the listings of
    /// exactly the channels that state a guide id — the ones matching works best for.
    /// </summary>
    [Fact]
    public async Task ImportAsync_KeepsProgrammesOfChannelsTheGuideNeverDeclared()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync([], cancellationToken);

        await AddChannelAsync(source, "Whatever", epgChannelId: "undeclared.fr", cancellationToken);

        registry.GuideDocument = """
            <tv>
              <programme start="20260812180000 +0000" stop="20260812190000 +0000" channel="undeclared.fr">
                <title>Running</title>
              </programme>
            </tv>
            """;

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.ProgrammeCount.ShouldBe(1);
        result.MatchedChannelCount.ShouldBe(1);
    }

    [Fact]
    public async Task ImportAsync_WhenTheSourceHasNoGuide_SaysSoRatherThanFailing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = null;

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert: a panel without xmltv.php is a fact about the panel, not an error.
        result.Outcome.ShouldBe(GuideImportOutcome.NoGuideAvailable);
        source.LastGuideImportedUtc.ShouldBeNull("nothing was imported, so nothing is recorded");
    }

    [Fact]
    public async Task ImportAsync_WhenTheAddressServesSomethingElse_ReportsAnEmptyGuide()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = "<html><body>Not found</body></html>";

        var guide = _services!.GetRequiredService<IGuideImportService>();

        // Act
        var result = await guide.ImportAsync(source, progress: null, cancellationToken);

        // Assert: distinguished from "no guide", because this one means the address is wrong.
        result.Outcome.ShouldBe(GuideImportOutcome.Empty);
    }

    [Fact]
    public async Task ImportAsync_ReportsEveryStageInOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = Guide;

        var stages = new List<GuideImportStage>();
        var progress = new SynchronousProgress<GuideImportStage>(stages.Add);

        // Act
        await _services!.GetRequiredService<IGuideImportService>()
            .ImportAsync(source, progress, cancellationToken);

        // Assert
        stages.ShouldBe(
        [
            GuideImportStage.Locating,
            GuideImportStage.Reading,
            GuideImportStage.Matching,
            GuideImportStage.Pruning,
        ]);
    }

    [Fact]
    public async Task ImportIfStaleAsync_SkipsAGuideThatWasJustImported()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = Guide;

        var guide = _services!.GetRequiredService<IGuideImportService>();
        await guide.ImportAsync(source, progress: null, cancellationToken);

        // Act
        var result = await guide.ImportIfStaleAsync(source, progress: null, cancellationToken);

        // Assert: a guide is a download of tens of megabytes, so the point is that it is not fetched again.
        result.Outcome.ShouldBe(GuideImportOutcome.NotDue);
        registry.Calls.Count(call => call == "guide").ShouldBe(1);
    }

    [Fact]
    public async Task ImportIfStaleAsync_FetchesOnceTheGuideHasAged()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = Guide;

        var guide = _services!.GetRequiredService<IGuideImportService>();
        await guide.ImportAsync(source, progress: null, cancellationToken);

        // Act: time moves on and the provider publishes listings for the new moment, which is what a
        // stale guide being refreshed actually looks like.
        _time.Advance(IGuideImportService.StaleAfter + TimeSpan.FromMinutes(1));
        registry.GuideDocument = BuildGuideAround(_time.GetUtcNow());
        var result = await guide.ImportIfStaleAsync(source, progress: null, cancellationToken);

        // Assert
        result.Succeeded.ShouldBeTrue();
        registry.Calls.Count(call => call == "guide").ShouldBe(2);
    }

    [Fact]
    public async Task ImportIfStaleAsync_FetchesWhenNoGuideHasEverBeenImported()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = Guide;

        // Act
        var result = await _services!.GetRequiredService<IGuideImportService>()
            .ImportIfStaleAsync(source, progress: null, cancellationToken);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The caller holds an untracked source instance, and it is that instance the staleness check reads.
    /// Left alone, the next refresh would fetch the whole guide again.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RecordsTheImportOnTheInstanceItWasGiven()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, registry) = await ArrangeAsync(["TF1"], cancellationToken);
        registry.GuideDocument = Guide;

        // Act
        await _services!.GetRequiredService<IGuideImportService>()
            .ImportAsync(source, progress: null, cancellationToken);

        // Assert
        source.LastGuideImportedUtc.ShouldBe(SixPm);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    private static string BuildGuideAround(DateTimeOffset instant)
    {
        return BuildLongGuide(programmeCount: 3, slot: TimeSpan.FromHours(1), start: instant);
    }

    /// <summary>
    /// Builds a guide of consecutive programmes.
    /// </summary>
    /// <param name="slot">
    /// Time each programme occupies. Chosen by the caller so that the whole run stays inside the window
    /// the import keeps — programmes beyond that are discarded by design, and a test wanting all of them
    /// stored has to stay within it.
    /// </param>
    private static string BuildLongGuide(int programmeCount, TimeSpan slot, DateTimeOffset? start = null)
    {
        var document = new StringBuilder();
        document.AppendLine("<tv>");
        document.AppendLine("""  <channel id="tf1.fr"><display-name>TF1</display-name></channel>""");

        var from = start ?? SixPm;

        for (var index = 0; index < programmeCount; index++)
        {
            var to = from + slot;

            document.AppendLine(
                CultureInfo.InvariantCulture,
                $"""  <programme start="{Stamp(from)}" stop="{Stamp(to)}" channel="tf1.fr">""");
            document.AppendLine(CultureInfo.InvariantCulture, $"    <title>Programme {index}</title>");
            document.AppendLine("  </programme>");

            from = to;
        }

        document.AppendLine("</tv>");
        return document.ToString();
    }

    private static string Stamp(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000";
    }

    private async Task<(PlaylistSource Source, FakeProviderRegistry Registry)> ArrangeAsync(
        IReadOnlyList<string> channelNames,
        CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken);

        var source = new XtreamSource
        {
            Name = "Test source",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
            CreatedUtc = SixPm,
        };

        var registry = new FakeProviderRegistry(source);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ICredentialProtector, PassThroughCredentialProtector>();
        services.AddSingleton<IProviderRegistry>(registry);
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        services.AddDbContext<LtrDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<CatalogueUnitOfWork>();
        services.AddSingleton<ICatalogueStore, CatalogueStore>();
        services.AddSingleton<IGuideImportService, GuideImportService>();

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        await context.AddSourceAsync(source, cancellationToken);

        if (channelNames.Count > 0)
        {
            await context.ReconcileLiveCatalogueAsync(
                source.Id,
                [],
                [.. channelNames.Select((name, index) => new Channel
                {
                    ExternalId = index.ToString(CultureInfo.InvariantCulture),
                    Name = name,
                })],
                SixPm,
                cancellationToken);
        }

        return (source, registry);
    }

    private async Task AddChannelAsync(
        PlaylistSource source,
        string name,
        string epgChannelId,
        CancellationToken cancellationToken)
    {
        await using var scope = _services!.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        await context.ReconcileLiveCatalogueAsync(
            source.Id,
            [],
            [new Channel { ExternalId = "with-guide-id", Name = name, EpgChannelId = epgChannelId }],
            SixPm,
            cancellationToken);
    }
}
