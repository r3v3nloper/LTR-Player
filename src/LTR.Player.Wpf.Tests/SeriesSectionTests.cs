using LTR.Core.Content;
using LTR.Core.Playback;
using static LTR.Player.Wpf.VodSectionFixtures;

namespace LTR.Player.Wpf;

/// <summary>
/// The series section: seasons, the episodes of the one open, and playing one.
/// </summary>
/// <remarks>
/// A series is stored in two passes — a shallow listing at import, its seasons only when it is opened — so
/// nearly everything here is about what the second pass does to what the first left, and about the season
/// picker not moving the rows out from under the viewer.
/// </remarks>
public sealed class SeriesSectionTests
{
    [Fact]
    public async Task PlayEpisode_ResumesFromItsOwnStoredPosition()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var episode = new Episode
        {
            Id = 7,
            ExternalId = "1001",
            Title = "Pilot",
            Number = 1,
            ContainerExtension = "mkv",
            ResumePositionSeconds = 600,
        };

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.PlaybackCommands.PlayEpisodeCommand
            .ExecuteAsync(new EpisodeItemViewModel(episode, seasonNumber: 1));

        // Assert
        var request = context.Session.Started.ShouldHaveSingleItem();
        request.StartAt.ShouldBe(ResumePolicy.StartFrom(TimeSpan.FromSeconds(600)));
        request.Url.AbsoluteUri.ShouldContain("/series/");
    }

    /// <summary>
    /// The gesture every other list in the window answers to. Its absence here is what made a viewer report
    /// that nothing appeared under Continue after starting a series: double-clicking an episode did nothing,
    /// so nothing was ever watched.
    /// </summary>
    [Fact]
    public async Task TheSelectedEpisode_IsWhatDoubleClickingPlays()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var detailed = SeriesEntry(10, "Breaking Bad");
        detailed.Seasons = [new Season { Number = 1, Episodes = [Episode("1001", "Pilot", 1)] }];
        context.VodDetail.Series.Add(detailed);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SeriesCatalogue.SelectedSeries = viewModel.SeriesCatalogue.Series[0];
        await viewModel.WaitForIdleAsync();

        // Act: what the list box's selection and the view's double-click handler do between them.
        viewModel.SeriesCatalogue.SelectedEpisode = viewModel.SeriesCatalogue.Episodes[0];
        await viewModel.PlaybackCommands.PlayEpisodeCommand.ExecuteAsync(viewModel.SeriesCatalogue.SelectedEpisode);

        // Assert
        context.Session.Started.ShouldHaveSingleItem().Url.AbsoluteUri.ShouldContain("/series/1001");
    }

    [Fact]
    public async Task ChangingSeason_ForgetsTheSelectedEpisode()
    {
        // Arrange: a selection pointing at a row from another season would have the play command act on an
        // episode that is no longer on screen.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var detailed = SeriesEntry(10, "Breaking Bad");
        detailed.Seasons =
        [
            new Season { Number = 1, Episodes = [Episode("1001", "Pilot", 1)] },
            new Season { Number = 2, Episodes = [Episode("2001", "Later", 1)] },
        ];

        context.VodDetail.Series.Add(detailed);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SeriesCatalogue.SelectedSeries = viewModel.SeriesCatalogue.Series[0];
        await viewModel.WaitForIdleAsync();

        viewModel.SeriesCatalogue.SelectedEpisode = viewModel.SeriesCatalogue.Episodes[0];

        // Act
        viewModel.SeriesCatalogue.SelectedSeason = viewModel.SeriesCatalogue.Seasons[1];

        // Assert
        viewModel.SeriesCatalogue.SelectedEpisode.ShouldBeNull();
    }

    [Fact]
    public async Task SelectingASeries_LoadsItsSeasonsAndFirstSeasonsEpisodes()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var detailed = SeriesEntry(10, "Breaking Bad");
        detailed.Seasons =
        [
            new Season { Number = 1, Episodes = [Episode("1001", "Pilot", 1)] },
            new Season { Number = 2, Episodes = [Episode("2001", "Later", 1)] },
        ];

        context.VodDetail.Series.Add(detailed);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.SeriesCatalogue.SelectedSeries = viewModel.SeriesCatalogue.Series[0];
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.SeriesCatalogue.Seasons.Count.ShouldBe(2);
        viewModel.SeriesCatalogue.SelectedSeason!.Number.ShouldBe(1);
        viewModel.SeriesCatalogue.Episodes.ShouldHaveSingleItem().Title.ShouldBe("Pilot");
    }

    [Fact]
    public async Task ChangingSeason_ShowsThatSeasonsEpisodes()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var detailed = SeriesEntry(10, "Breaking Bad");
        detailed.Seasons =
        [
            new Season { Number = 1, Episodes = [Episode("1001", "Pilot", 1)] },
            new Season { Number = 2, Episodes = [Episode("2001", "Later", 1)] },
        ];

        context.VodDetail.Series.Add(detailed);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SeriesCatalogue.SelectedSeries = viewModel.SeriesCatalogue.Series[0];
        await viewModel.WaitForIdleAsync();

        // Act
        viewModel.SeriesCatalogue.SelectedSeason = viewModel.SeriesCatalogue.Seasons[1];

        // Assert
        viewModel.SeriesCatalogue.Episodes.ShouldHaveSingleItem().Title.ShouldBe("Later");
    }

}
