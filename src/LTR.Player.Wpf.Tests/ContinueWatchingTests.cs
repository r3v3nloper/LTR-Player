using LTR.Core.Content;
using LTR.Core.Playback;
using static LTR.Player.Wpf.VodSectionFixtures;

namespace LTR.Player.Wpf;

/// <summary>
/// The continue-watching list, which is the one list that mixes films and episodes.
/// </summary>
/// <remarks>
/// That mixing is the subject. An entry names a film or an episode and never a series, so resuming one loads
/// the item directly; and removing one is deliberately not a viewing, which is the distinction every case here
/// turns on.
/// </remarks>
public sealed class ContinueWatchingTests
{
    [Fact]
    public async Task ResumeEntry_ForAnEpisode_PlaysThatEpisodeRatherThanItsSeries()
    {
        // Arrange: an entry carries the identity of a film or an episode, never of a series.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Episodes.Add(new Episode
        {
            Id = 7,
            ExternalId = "1001",
            Title = "Pilot",
            Number = 1,
            ContainerExtension = "mkv",
        });

        var entry = new ContinueWatchingEntry(
            ContentKind.Series,
            ItemId: 7,
            Title: "Breaking Bad",
            Subtitle: "S01E01 · Pilot",
            CoverUrl: null,
            PositionSeconds: 600,
            DurationSeconds: 2_820,
            LastWatchedUtc: MainViewModelHarness.Now);

        context.Store.ContinueWatching.Add(entry);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.PlaybackCommands.ResumeEntryCommand.ExecuteAsync(entry);

        // Assert
        var request = context.Session.Started.ShouldHaveSingleItem();
        request.Url.AbsoluteUri.ShouldContain("/series/1001");
        request.StartAt.ShouldBe(ResumePolicy.StartFrom(TimeSpan.FromSeconds(600)));
    }

    [Fact]
    public async Task ForgetEntry_ClearsTheStoredPositionWithoutMarkingItWatched()
    {
        // Arrange: for the film that did not hold the viewer's attention. Marking it watched would be the
        // worse lie of the two — nobody saw it.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var entry = new ContinueWatchingEntry(
            ContentKind.Movie,
            ItemId: 1,
            Title: "Arrival",
            Subtitle: string.Empty,
            CoverUrl: null,
            PositionSeconds: 2_400,
            DurationSeconds: 6_000,
            LastWatchedUtc: MainViewModelHarness.Now);

        context.Store.ContinueWatching.Add(entry);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ContinueWatching.Entries.ShouldHaveSingleItem();

        // Act
        await viewModel.PlaybackCommands.ForgetEntryCommand.ExecuteAsync(entry);

        // Assert
        var forgotten = context.Store.ForgottenEntries.ShouldHaveSingleItem();
        forgotten.Kind.ShouldBe(ContentKind.Movie);
        forgotten.ItemId.ShouldBe(1);
        context.Store.ProgressWrites.ShouldBeEmpty(
            "removing an entry records no viewing, so it must not stamp the row as watched now");
        context.Session.Started.ShouldBeEmpty("removing something is not playing it");
    }

    [Fact]
    public async Task ForgetEntry_ForAnEpisode_ClearsThatEpisode()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var entry = new ContinueWatchingEntry(
            ContentKind.Series,
            ItemId: 7,
            Title: "Breaking Bad",
            Subtitle: "S01E01 · Pilot",
            CoverUrl: null,
            PositionSeconds: 600,
            DurationSeconds: 2_820,
            LastWatchedUtc: MainViewModelHarness.Now);

        context.Store.ContinueWatching.Add(entry);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.PlaybackCommands.ForgetEntryCommand.ExecuteAsync(entry);

        // Assert
        var forgotten = context.Store.ForgottenEntries.ShouldHaveSingleItem();
        forgotten.Kind.ShouldBe(ContentKind.Series);
        forgotten.ItemId.ShouldBe(7);
        context.Store.ProgressWrites.ShouldBeEmpty("removing an entry records no viewing");
    }

    /// <summary>
    /// Removing the film that is playing has to stop it being followed as well, or stopping playback
    /// afterwards writes the position straight back and the entry returns.
    /// </summary>
    [Fact]
    public async Task ForgetEntry_ForTheFilmThatIsPlaying_IsNotUndoneByStopping()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = await OpenFilmAsync(context);
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(20);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        var entry = new ContinueWatchingEntry(
            ContentKind.Movie,
            ItemId: 1,
            Title: "Arrival",
            Subtitle: string.Empty,
            CoverUrl: null,
            PositionSeconds: 1_200,
            DurationSeconds: 6_000,
            LastWatchedUtc: MainViewModelHarness.Now);

        // Act
        await viewModel.PlaybackCommands.ForgetEntryCommand.ExecuteAsync(entry);
        await viewModel.PlaybackCommands.StopCommand.ExecuteAsync(null);

        // Assert: sharper than it could be before forgetting became its own operation. The write-back this
        // guards against would now appear as a progress write of its own rather than as a second one that
        // looked like the forget.
        context.Store.ForgottenEntries.ShouldHaveSingleItem().ItemId.ShouldBe(1);
        context.Store.ProgressWrites.ShouldBeEmpty("stopping must not write the position back");
    }

    [Fact]
    public async Task ResumeEntry_ForSomethingNoLongerStored_SaysSoInsteadOfFailing()
    {
        // Arrange: a refresh removes what the provider has withdrawn, and the list is a moment behind it.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var entry = new ContinueWatchingEntry(
            ContentKind.Movie,
            ItemId: 99,
            Title: "Withdrawn",
            Subtitle: string.Empty,
            CoverUrl: null,
            PositionSeconds: 600,
            DurationSeconds: null,
            LastWatchedUtc: MainViewModelHarness.Now);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.PlaybackCommands.ResumeEntryCommand.ExecuteAsync(entry);

        // Assert
        context.Session.Started.ShouldBeEmpty();
        viewModel.Status.Text.ShouldContain("no longer in the catalogue");
    }

}
