using System.Globalization;
using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers what previous and next mean, which depends on what is playing.
/// </summary>
/// <remarks>
/// <para>
/// Written for a reported defect: the two were wired straight to channel zapping, so pressing next during an
/// episode switched the left pane to Live and tuned a channel. Both of the viewer's routes into an episode
/// are covered, because only one of them has the series open — resuming from the continue-watching list has
/// an episode identifier and nothing else, and that is the harder case.
/// </para>
/// <para>
/// Asserted on what the engine was asked to open rather than on a view model flag. A next that moved a
/// selection without changing the stream, or changed the stream to the wrong thing, is precisely the defect.
/// </para>
/// </remarks>
public sealed class PlayerNextPreviousTests
{
    [Fact]
    public async Task Next_WhileAnEpisodePlays_PlaysTheFollowingEpisodeAndNotAChannel()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAnEpisodePlayingAsync(context);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.Count.ShouldBe(2);
        ShouldHaveOpened(context, seasonNumber: 1, episodeNumber: 2);
        viewModel.NowPlaying.ShouldBe("Breaking Bad · S01E02 · Cat in the Bag");
        viewModel.SelectedSection.ShouldBe(CatalogueSection.Series, "the live list must not be switched to");
    }

    /// <summary>
    /// The route the defect was reported from: Continue, then the episode, then next.
    /// </summary>
    /// <remarks>
    /// Nothing about the series has been opened here — the entry carries an episode identifier and no more —
    /// so this is the case a walk over the on-screen episode rows could not answer, and the reason the
    /// neighbour is looked up in the store.
    /// </remarks>
    [Fact]
    public async Task Next_AfterResumingFromContinueWatching_PlaysTheFollowingEpisode()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithASeriesInTheCatalogueAsync(context);

        context.Store.ContinueWatching.Add(new ContinueWatchingEntry(
            ContentKind.Series,
            EpisodeId(1, 1),
            "Breaking Bad",
            "S01E01",
            CoverUrl: null,
            PositionSeconds: 600,
            DurationSeconds: 2700,
            MainViewModelHarness.Now));

        await viewModel.ContinueWatching.ReloadAsync(TestContext.Current.CancellationToken);
        var entry = viewModel.ContinueWatching.Entries.ShouldHaveSingleItem();

        viewModel.SelectedSection = CatalogueSection.ContinueWatching;
        await viewModel.ResumeEntryCommand.ExecuteAsync(entry);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.Count.ShouldBe(2);
        ShouldHaveOpened(context, seasonNumber: 1, episodeNumber: 2);
        viewModel.NowPlaying.ShouldBe("Breaking Bad · S01E02 · Cat in the Bag");
        viewModel.SelectedSection.ShouldBe(
            CatalogueSection.ContinueWatching,
            "the viewer was left looking at the list they started from");
    }

    /// <remarks>
    /// The season boundary, through the shell rather than only over <see cref="EpisodeSequence"/>: the store
    /// has to reach the whole series for it, and a lookup that stopped at the played episode's own season
    /// would pass every other test here.
    /// </remarks>
    [Fact]
    public async Task Next_AtTheEndOfASeason_CrossesIntoTheNext()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAnEpisodePlayingAsync(context, episodeNumber: 2);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        ShouldHaveOpened(context, seasonNumber: 2, episodeNumber: 1);
        viewModel.NowPlaying.ShouldBe("Breaking Bad · S02E01 · Seven Thirty-Seven");
    }

    [Fact]
    public async Task Previous_WhileAnEpisodePlays_PlaysTheOneBefore()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAnEpisodePlayingAsync(context, episodeNumber: 2);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayPrevious, TestContext.Current.CancellationToken);

        // Assert
        ShouldHaveOpened(context, seasonNumber: 1, episodeNumber: 1);
        viewModel.NowPlaying.ShouldBe("Breaking Bad · S01E01 · Pilot");
    }

    [Fact]
    public async Task Next_AfterTheLastEpisode_PlaysNothingAndSaysSo()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAnEpisodePlayingAsync(context, seasonNumber: 2, episodeNumber: 1);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldHaveSingleItem();
        viewModel.Status.Text.ShouldBe("That was the last episode of the series.");
    }

    /// <remarks>
    /// A film is one item, and the film list's order is a search result rather than a sequence anybody watches
    /// through. Greyed out rather than silently inert, and the notification is what is asserted: the guard
    /// itself answers correctly even when nothing tells the button to re-read it.
    /// </remarks>
    [Fact]
    public async Task WhileAFilmPlays_NextIsUnavailableAndDoesNothing()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAndAFilmAsync(context);

        var notified = 0;
        viewModel.PlayNextCommand.CanExecuteChanged += (_, _) => notified++;

        // Act
        await viewModel.PlayMovieCommand.ExecuteAsync(null);
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        notified.ShouldBeGreaterThan(0);
        viewModel.PlayNextCommand.CanExecute(null).ShouldBeFalse();
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Arrival");
    }

    /// <remarks>
    /// The behaviour that was there before and has to stay: watching live, next is the next channel.
    /// </remarks>
    [Fact]
    public async Task Next_WhileAChannelPlays_StillZaps()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAndAFilmAsync(context);

        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView
            .Cast<ChannelItemViewModel>()
            .First();

        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Act
        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started[^1].DisplayName.ShouldBe("Zweite");
    }

    /// <remarks>
    /// After a stop there is nothing to be the next of, and next means the next channel again — what it means
    /// on a freshly opened window. A film left recorded would keep the buttons greyed out for good.
    /// </remarks>
    [Fact]
    public async Task AfterAStop_NextIsAvailableAgain()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAndAFilmAsync(context);

        await viewModel.PlayMovieCommand.ExecuteAsync(null);
        viewModel.PlayNextCommand.CanExecute(null).ShouldBeFalse();

        // Act
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        viewModel.PlayNextCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>
    /// After switching subscription, next must not reach for the previous one's series.
    /// </summary>
    /// <remarks>
    /// Switching source does not stop what is playing, so the episode of the source just left is still what
    /// next refers to. Its successor would be opened against the newly selected account — an address built
    /// from one subscription's identifier and another's credentials, which comes back as a dead stream.
    /// </remarks>
    [Fact]
    public async Task AfterSwitchingSource_NextDoesNotReachIntoThePreviousSubscription()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAnEpisodePlayingAsync(context);

        context.Store.Sources.Add(CreateSource(id: 2, name: "Source 2"));

        // Act
        viewModel.SourceManagement.SelectedSource = context.Store.Sources[1];
        await WaitForIdleAsync(viewModel);

        await viewModel.PerformAsync(PlayerAction.PlayNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldHaveSingleItem("nothing new may be opened");
    }

    /// <summary>
    /// Asserts which episode the engine was last asked to open.
    /// </summary>
    /// <remarks>
    /// By the address rather than by the request's own label: the label is the resolver's, and asserting on it
    /// would pass for any episode whose title happened to match. The address carries the identifier the
    /// provider builds a stream from, which is the thing that decides what actually plays.
    /// </remarks>
    private static void ShouldHaveOpened(MainViewModelHarness context, int seasonNumber, int episodeNumber)
    {
        var expected = EpisodeId(seasonNumber, episodeNumber).ToString(CultureInfo.InvariantCulture);

        context.Session.Started[^1].Url.AbsoluteUri.ShouldContain($"/series/{expected}.");
    }

    /// <summary>
    /// Plays one episode the way the viewer does: open the series, pick the season, play the row.
    /// </summary>
    private static async Task<MainViewModel> WithAnEpisodePlayingAsync(
        MainViewModelHarness context,
        int seasonNumber = 1,
        int episodeNumber = 1)
    {
        var viewModel = await WithASeriesInTheCatalogueAsync(context);

        viewModel.SelectedSection = CatalogueSection.Series;
        viewModel.SeriesCatalogue.SelectedSeries = viewModel.SeriesCatalogue.Series[0];
        await WaitForIdleAsync(viewModel);

        viewModel.SeriesCatalogue.SelectedSeason = viewModel.SeriesCatalogue.Seasons
            .Single(season => season.Number == seasonNumber);

        var row = viewModel.SeriesCatalogue.Episodes
            .Single(episode => episode.Id == EpisodeId(seasonNumber, episodeNumber));

        await viewModel.PlayEpisodeCommand.ExecuteAsync(row);

        return viewModel;
    }

    /// <summary>
    /// Seeds two seasons of one series, in both places the window reads them from.
    /// </summary>
    /// <remarks>
    /// The store and the detail service hold the same object, as they do in the container: opening a series
    /// goes through the detail service, and finding the next episode goes through the store.
    /// </remarks>
    private static async Task<MainViewModel> WithASeriesInTheCatalogueAsync(MainViewModelHarness context)
    {
        context.Store.Sources.Add(CreateSource());

        var series = SeriesWithTwoSeasons();
        context.Store.SeriesCatalogue.Add(series);
        context.VodDetail.Series.Add(series);

        // The episodes again on their own, as the real store holds them: resuming a continue-watching row
        // loads one by identifier without going near its series.
        context.Store.Episodes.AddRange(series.Seasons.SelectMany(season => season.Episodes));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        return viewModel;
    }

    private static async Task<MainViewModel> WithThreeChannelsAndAFilmAsync(MainViewModelHarness context)
    {
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(Channel(1, "101", "Erste"));
        context.Store.Channels.Add(Channel(2, "102", "Zweite"));
        context.Store.Channels.Add(Channel(3, "103", "Dritte"));
        context.Store.Movies.Add(new VodItem
        {
            Id = 1,
            SourceId = 1,
            ExternalId = "1",
            Name = "Arrival",
            ContainerExtension = "mkv",
        });

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await WaitForIdleAsync(viewModel);

        return viewModel;
    }

    private static async Task WaitForIdleAsync(MainViewModel viewModel)
    {
        while (!viewModel.SectionWorkCompletion.IsCompleted)
        {
            await viewModel.SectionWorkCompletion;
        }
    }

    private static Series SeriesWithTwoSeasons()
    {
        return new Series
        {
            Id = 10,
            SourceId = 1,
            ExternalId = "10",
            Name = "Breaking Bad",
            Seasons =
            [
                new Season
                {
                    Number = 1,
                    Episodes =
                    [
                        Episode(1, 1, "Pilot"),
                        Episode(1, 2, "Cat in the Bag"),
                    ],
                },
                new Season
                {
                    Number = 2,
                    Episodes = [Episode(2, 1, "Seven Thirty-Seven")],
                },
            ],
        };
    }

    private static Episode Episode(int seasonNumber, int number, string title)
    {
        return new Episode
        {
            Id = EpisodeId(seasonNumber, number),
            ExternalId = EpisodeId(seasonNumber, number).ToString(CultureInfo.InvariantCulture),
            Title = title,
            Number = number,
            ContainerExtension = "mkv",
        };
    }

    /// <summary>A stable identifier per season and episode number, so a test can name the one it means.</summary>
    private static int EpisodeId(int seasonNumber, int episodeNumber)
    {
        return (seasonNumber * 100) + episodeNumber;
    }

    private static Channel Channel(int id, string externalId, string name, int sourceId = 1)
    {
        return new Channel
        {
            Id = id,
            SourceId = sourceId,
            ExternalId = externalId,
            Name = name,
        };
    }

    private static XtreamSource CreateSource(int id = 1, string name = "Source 1")
    {
        return new XtreamSourceBuilder()
            .WithId(id)
            .WithName(name)
            .WithCredentials("alice", "s3cret")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = true,
                SupportsSeries = true,
                ProbedAtUtc = MainViewModelHarness.Now,
            })
            .Build();
    }
}
