using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// The guide half of the store, against real SQLite.
/// </summary>
/// <remarks>
/// Real SQLite matters more here than anywhere else: the now-and-next query selects two rows per channel
/// inside one statement, the batch writer relies on a transaction around a delete and an insert, and the
/// unique index on (source, guide identifier) is what makes a duplicated declaration a failure. None of
/// those is modelled by the in-memory provider.
/// </remarks>
public sealed class LtrDbContextGuideTests
{
    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureGuideChannelsAsync_InsertsOnceAndReturnsTheSameIdentityAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        Dictionary<string, int> first;
        Dictionary<string, int> second;

        await using (var context = database.CreateContext())
        {
            first = await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", "TF1")],
                cancellationToken);
        }

        await using (var context = database.CreateContext())
        {
            second = await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", "TF1"), GuideChannel("ard.de", "ARD")],
                cancellationToken);
        }

        // Assert: the second pass recognises what the first stored rather than duplicating it.
        second["tf1.fr"].ShouldBe(first["tf1.fr"]);
        second.Count.ShouldBe(2);

        await using var verifyContext = database.CreateContext();
        (await verifyContext.GuideChannels.CountAsync(cancellationToken)).ShouldBe(2);
    }

    /// <summary>
    /// A channel first seen through a programme reference has no name. Letting that overwrite the name a
    /// declaration supplied would break name matching for it.
    /// </summary>
    [Fact]
    public async Task EnsureGuideChannelsAsync_DoesNotEraseANameWithNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", "TF1")],
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", displayName: null)],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var stored = await verifyContext.GuideChannels.AsNoTracking().SingleAsync(cancellationToken);
        stored.DisplayName.ShouldBe("TF1");
    }

    /// <summary>
    /// A reimport must replace a channel's listings rather than add to them, and it must do so channel by
    /// channel so the guide is never wholly absent.
    /// </summary>
    [Fact]
    public async Task AppendGuideProgrammesAsync_ReplacesOnlyTheChannelsNamed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [
                    Entry(guideChannelIds["tf1.fr"], "Old TF1", SixPm),
                    Entry(guideChannelIds["ard.de"], "Old ARD", SixPm),
                ],
                [guideChannelIds["tf1.fr"], guideChannelIds["ard.de"]],
                cancellationToken);
        }

        // Act: a second import that only touches one of the two channels.
        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "New TF1", SixPm)],
                [guideChannelIds["tf1.fr"]],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var titles = await verifyContext.EpgEntries
            .AsNoTracking()
            .OrderBy(entry => entry.Title)
            .Select(entry => entry.Title)
            .ToListAsync(cancellationToken);

        titles.ShouldBe(["New TF1", "Old ARD"]);
    }

    [Fact]
    public async Task AppendGuideProgrammesAsync_AddsToAChannelAlreadyClearedInThisImport()
    {
        // Arrange: a channel whose programmes span two batches is cleared by the first batch only, and the
        // second has to add rather than replace.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "First", SixPm)],
                [guideChannelIds["tf1.fr"]],
                cancellationToken);

            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "Second", SixPm.AddHours(1))],
                [],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        (await verifyContext.EpgEntries.CountAsync(cancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task GetNowAndNextAsync_ReportsTheRunningProgrammeAndTheOneAfterIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [],
                [Channel("1", "TF1")],
                SixPm,
                cancellationToken);

            await context.AppendGuideProgrammesAsync(
                [
                    Entry(guideChannelIds["tf1.fr"], "Finished", SixPm.AddHours(-2)),
                    Entry(guideChannelIds["tf1.fr"], "Running", SixPm.AddMinutes(-30)),
                    Entry(guideChannelIds["tf1.fr"], "Next", SixPm.AddMinutes(30)),
                    Entry(guideChannelIds["tf1.fr"], "Later", SixPm.AddHours(2)),
                ],
                [],
                cancellationToken);

            await LinkAsync(context, sourceId, guideChannelIds["tf1.fr"], cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var slices = await verifyContext.GetNowAndNextAsync(sourceId, SixPm, cancellationToken);

        // Assert
        var slice = slices.ShouldHaveSingleItem();
        slice.Now!.Title.ShouldBe("Running");
        slice.Next!.Title.ShouldBe("Next");
    }

    /// <summary>
    /// Guides have gaps. A channel with nothing on must read as nothing on, not as its next programme
    /// already running.
    /// </summary>
    [Fact]
    public async Task GetNowAndNextAsync_LeavesNowEmptyWhenTheGuideHasAGapOverTheMoment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(sourceId, [], [Channel("1", "TF1")], SixPm, cancellationToken);

            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "Tonight", SixPm.AddHours(3))],
                [],
                cancellationToken);

            await LinkAsync(context, sourceId, guideChannelIds["tf1.fr"], cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var slices = await verifyContext.GetNowAndNextAsync(sourceId, SixPm, cancellationToken);

        // Assert
        var slice = slices.ShouldHaveSingleItem();
        slice.Now.ShouldBeNull();
        slice.Next!.Title.ShouldBe("Tonight");
    }

    [Fact]
    public async Task GetNowAndNextAsync_SkipsChannelsWithNoGuide()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(sourceId, [], [Channel("1", "TF1")], SixPm, cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var slices = await verifyContext.GetNowAndNextAsync(sourceId, SixPm, cancellationToken);

        // Assert
        slices.ShouldBeEmpty();
    }

    /// <summary>
    /// A timeline needs the programme that is already running when its window opens, so the test is
    /// overlap and not containment.
    /// </summary>
    [Fact]
    public async Task GetGuideProgrammesAsync_ReturnsEverythingOverlappingTheWindow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [
                    Entry(guideChannelIds["tf1.fr"], "Before", SixPm.AddHours(-4)),
                    Entry(guideChannelIds["tf1.fr"], "Straddling the start", SixPm.AddMinutes(-30)),
                    Entry(guideChannelIds["tf1.fr"], "Inside", SixPm.AddMinutes(30)),
                    Entry(guideChannelIds["tf1.fr"], "After", SixPm.AddHours(6)),
                    Entry(guideChannelIds["ard.de"], "Other channel", SixPm),
                ],
                [],
                cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var programmes = await verifyContext.GetGuideProgrammesAsync(
            [guideChannelIds["tf1.fr"]],
            SixPm,
            SixPm.AddHours(2),
            cancellationToken);

        // Assert
        programmes.Select(entry => entry.Title).ShouldBe(["Straddling the start", "Inside"]);
    }

    [Fact]
    public async Task PruneGuideProgrammesAsync_DiscardsWhatHasAlreadyFinished()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [
                    Entry(guideChannelIds["tf1.fr"], "Yesterday", SixPm.AddDays(-1)),
                    Entry(guideChannelIds["tf1.fr"], "Tonight", SixPm),
                ],
                [],
                cancellationToken);
        }

        // Act
        int pruned;

        await using (var context = database.CreateContext())
        {
            pruned = await context.PruneGuideProgrammesAsync(SixPm.AddHours(-6), cancellationToken);
        }

        // Assert
        pruned.ShouldBe(1);

        await using var verifyContext = database.CreateContext();
        var remaining = await verifyContext.EpgEntries.AsNoTracking().SingleAsync(cancellationToken);
        remaining.Title.ShouldBe("Tonight");
    }

    /// <summary>
    /// A guide channel that has fallen out of the guide is recognisable only once its programmes have aged
    /// out, which is why the two run in that order.
    /// </summary>
    [Fact]
    public async Task RemoveGuideChannelsWithoutProgrammesAsync_ClearsWhatThePruningEmptied()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "Tonight", SixPm)],
                [],
                cancellationToken);
        }

        // Act
        int removed;

        await using (var context = database.CreateContext())
        {
            removed = await context.RemoveGuideChannelsWithoutProgrammesAsync(sourceId, cancellationToken);
        }

        // Assert
        removed.ShouldBe(1);

        await using var verifyContext = database.CreateContext();
        var remaining = await verifyContext.GuideChannels.AsNoTracking().SingleAsync(cancellationToken);
        remaining.ExternalId.ShouldBe("tf1.fr");
    }

    [Fact]
    public async Task LinkChannelsToGuideAsync_MatchesByNameAndClearsAMatchThatNoLongerHolds()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [],
                [Channel("1", "FR: TF1 HD"), Channel("2", "Obscure Local")],
                SixPm,
                cancellationToken);

            await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", "TF1")],
                cancellationToken);
        }

        // Act
        int matched;

        await using (var context = database.CreateContext())
        {
            matched = await context.LinkChannelsToGuideAsync(sourceId, cancellationToken);
        }

        // Assert
        matched.ShouldBe(1);

        await using var verifyContext = database.CreateContext();
        var channels = await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken);

        channels.Single(channel => channel.ExternalId == "1").GuideChannelId.ShouldNotBeNull();
        channels.Single(channel => channel.ExternalId == "2").GuideChannelId.ShouldBeNull();
    }

    /// <summary>
    /// A channel's guide link is the outcome of matching, not something the provider states, so a
    /// catalogue refresh must leave it where it is — the same reasoning that protects the favourite flag.
    /// </summary>
    [Fact]
    public async Task ReconcileLiveCatalogueAsync_KeepsTheGuideLinkOfAnExistingChannel()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(sourceId, [], [Channel("1", "TF1")], SixPm, cancellationToken);

            await context.EnsureGuideChannelsAsync(
                sourceId,
                [GuideChannel("tf1.fr", "TF1")],
                cancellationToken);

            await context.LinkChannelsToGuideAsync(sourceId, cancellationToken);
        }

        // Act: the provider re-sends the channel, renamed.
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [],
                [Channel("1", "TF1 HD")],
                SixPm.AddHours(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var channel = await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken);
        channel.ShouldHaveSingleItem().GuideChannelId.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetGuideSummaryAsync_ReportsCoverageAndHowManyChannelsTheGuideReaches()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [],
                [Channel("1", "TF1"), Channel("2", "Obscure Local")],
                SixPm,
                cancellationToken);

            await context.AppendGuideProgrammesAsync(
                [
                    Entry(guideChannelIds["tf1.fr"], "Tonight", SixPm),
                    Entry(guideChannelIds["tf1.fr"], "Later", SixPm.AddHours(2)),
                ],
                [],
                cancellationToken);

            await context.LinkChannelsToGuideAsync(sourceId, cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var summary = await verifyContext.GetGuideSummaryAsync(sourceId, cancellationToken);

        // Assert
        summary.GuideChannelCount.ShouldBe(2);
        summary.ProgrammeCount.ShouldBe(2);
        summary.MatchedChannelCount.ShouldBe(1);
        summary.TotalChannelCount.ShouldBe(2);
        summary.CoverageUntilUtc.ShouldBe(SixPm.AddHours(3));
    }

    /// <summary>
    /// Deleting a source has to take its guide with it, or the database keeps growing with programmes no
    /// channel can reach.
    /// </summary>
    [Fact]
    public async Task DeleteSourceAsync_TakesTheGuideWithIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var guideChannelIds = await SeedGuideChannelsAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AppendGuideProgrammesAsync(
                [Entry(guideChannelIds["tf1.fr"], "Tonight", SixPm)],
                [],
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.DeleteSourceAsync(sourceId, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        (await verifyContext.GuideChannels.CountAsync(cancellationToken)).ShouldBe(0);
        (await verifyContext.EpgEntries.CountAsync(cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task MarkGuideImportedAsync_RecordsWhenTheGuideWasLastFetched()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.MarkGuideImportedAsync(sourceId, SixPm, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var sources = await verifyContext.GetSourcesAsync(cancellationToken);
        sources.ShouldHaveSingleItem().LastGuideImportedUtc.ShouldBe(SixPm);
    }

    private static async Task LinkAsync(
        LtrDbContext context,
        int sourceId,
        int guideChannelId,
        CancellationToken cancellationToken)
    {
        // Set outright rather than through the matcher, so a test about queries does not also depend on
        // the matching rules.
        await context.Channels
            .Where(channel => channel.SourceId == sourceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(channel => channel.GuideChannelId, guideChannelId),
                cancellationToken);
    }

    private static async Task<Dictionary<string, int>> SeedGuideChannelsAsync(
        SqliteTestDatabase database,
        int sourceId,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.EnsureGuideChannelsAsync(
            sourceId,
            [GuideChannel("tf1.fr", "TF1"), GuideChannel("ard.de", "ARD")],
            cancellationToken);
    }

    private static async Task<int> AddSourceAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.AddSourceAsync(
            new XtreamSource
            {
                Name = "Test source",
                BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
                Username = "alice",
                Password = "pass",
                CreatedUtc = SixPm,
            },
            cancellationToken);
    }

    private static GuideChannel GuideChannel(string externalId, string? displayName)
    {
        return new GuideChannel { ExternalId = externalId, DisplayName = displayName };
    }

    private static Channel Channel(string externalId, string name)
    {
        return new Channel { ExternalId = externalId, Name = name };
    }

    /// <summary>Two hours long, which is what makes the window assertions above legible.</summary>
    private static EpgEntry Entry(int guideChannelId, string title, DateTimeOffset startUtc)
    {
        return new EpgEntry
        {
            GuideChannelId = guideChannelId,
            StartUtc = startUtc,
            StopUtc = startUtc.AddHours(1),
            Title = title,
        };
    }
}
