using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers the film and series sections, and above all what happens to a resume position.
/// </summary>
/// <remarks>
/// The interesting behaviour is all in the seams: a position that has to be sampled before playback stops,
/// a detail fetch that must not overwrite a newer selection, and a section that must not stay on screen for
/// a subscription that does not offer it. Each of those has a wrong version that looks perfectly correct.
/// </remarks>
public sealed class VodSectionTests
{
    private static readonly TimeSpan FilmLength = TimeSpan.FromMinutes(100);

    [Fact]
    public async Task ShowCatalogue_LoadsFilmsAndSeriesForTheSelectedSource()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.Movies.ShouldHaveSingleItem().Name.ShouldBe("Arrival");
        viewModel.SeriesCatalogue.Series.ShouldHaveSingleItem().Name.ShouldBe("Breaking Bad");
    }

    [Fact]
    public async Task ShowCatalogue_ForASourceWithoutFilms_LeavesTheSectionUnavailable()
    {
        // Arrange: a playlist source, which offers live entries and nothing else.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(supportsVod: false, supportsSeries: false));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.IsAvailable.ShouldBeFalse();
        viewModel.SeriesCatalogue.IsAvailable.ShouldBeFalse();
    }

    /// <summary>
    /// Switching to a subscription that has no films while the film section is open would otherwise leave
    /// the previous subscription's catalogue on screen under the new subscription's name.
    /// </summary>
    [Fact]
    public async Task ShowCatalogue_WhenTheNewSourceLacksTheOpenSection_FallsBackToLive()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Sources.Add(CreateSource(id: 2, supportsVod: false, supportsSeries: false));
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;

        // Act
        viewModel.SourceManagement.SelectedSource = context.Store.Sources[1];
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.SelectedSection.ShouldBe(CatalogueSection.Live);
    }

    /// <summary>
    /// The category shown in the picker and the category the filter uses have to agree. They did not:
    /// emptying the bound collection makes a ComboBox write a null selection back through the binding, so a
    /// selection made before the picker was refilled was discarded — the picker rendered blank while the
    /// list, reading the same null, still showed every category and therefore looked perfectly correct.
    /// </summary>
    [Fact]
    public async Task ShowCatalogue_LeavesEveryCategorySelectedInThePicker()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(
            new Category { SourceId = 1, ExternalId = "58", Name = "Action", Kind = ContentKind.Movie });
        context.Store.Categories.Add(
            new Category { SourceId = 1, ExternalId = "75", Name = "Drama", Kind = ContentKind.Series });

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.Categories.Count.ShouldBe(2, "the catch-all entry and one film category");
        viewModel.Movies.SelectedCategory.ShouldBe(CategoryChoice.All);
        viewModel.SeriesCatalogue.SelectedCategory.ShouldBe(CategoryChoice.All);
    }

    [Fact]
    public async Task Search_NarrowsTheFilmList()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.Movies.Add(Movie(2, "The Matrix"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Movies.SearchText = "matrix";
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.Movies.Movies.ShouldHaveSingleItem().Name.ShouldBe("The Matrix");
    }

    [Fact]
    public async Task SelectingAFilm_FetchesItsDetail()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var detailed = Movie(1, "Arrival");
        detailed.Plot = "Linguist meets heptapods.";
        context.VodDetail.Movies.Add(detailed);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await viewModel.WaitForIdleAsync();

        // Assert
        context.VodDetail.Requests.ShouldContain("movie:1");
        viewModel.Movies.DetailedMovie!.Movie.Plot.ShouldBe("Linguist meets heptapods.");
    }

    /// <summary>
    /// A panel can take seconds to answer a detail call. If the viewer has moved on by then, the answer
    /// belongs to a film that is no longer selected and must be dropped.
    /// </summary>
    [Fact]
    public async Task SelectingAFilm_WhenTheAnswerArrivesLate_DoesNotOverwriteANewerSelection()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.Movies.Add(Movie(2, "The Matrix"));

        var slow = Movie(1, "Arrival");
        slow.Plot = "The first film's synopsis.";
        context.VodDetail.Movies.Add(slow);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var gate = new TaskCompletionSource();
        context.VodDetail.Gate = gate;

        // Act: select the first film, move to the second, then let the first answer arrive.
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[1];

        gate.SetResult();
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.Movies.DetailedMovie!.Id.ShouldBe(2);
        viewModel.Movies.DetailedMovie.Movie.Plot.ShouldBeNull();
    }

    [Fact]
    public async Task PlayMovie_WithAStoredPosition_ResumesShortOfIt()
    {
        // Arrange: the rewind is what gives the viewer a moment of context rather than a cut mid-word.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var movie = Movie(1, "Arrival");
        movie.ResumePositionSeconds = 2_400;
        context.Store.Movies.Add(movie);

        var viewModel = await OpenFilmAsync(context);

        // Act
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        // Assert
        var request = context.Session.Started.ShouldHaveSingleItem();
        request.StartAt.ShouldBe(ResumePolicy.StartFrom(TimeSpan.FromSeconds(2_400)));
        request.Format.ShouldBe(StreamFormat.ProgressiveFile);
    }

    [Fact]
    public async Task RestartMovie_IgnoresTheStoredPosition()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var movie = Movie(1, "Arrival");
        movie.ResumePositionSeconds = 2_400;
        context.Store.Movies.Add(movie);

        var viewModel = await OpenFilmAsync(context);

        // Act
        await viewModel.RestartMovieCommand.ExecuteAsync(null);

        // Assert
        context.Session.Started.ShouldHaveSingleItem().StartAt.ShouldBeNull();
    }

    [Fact]
    public void RestartMovie_IsDisabledWithoutAStoredPosition()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act & Assert
        viewModel.RestartMovieCommand.CanExecute(null).ShouldBeFalse();
    }

    /// <remarks>
    /// The same defect class the whole test project exists for: the guard reads the film section's
    /// selection, and <c>[NotifyCanExecuteChangedFor]</c> cannot cross an object boundary.
    /// </remarks>
    [Fact]
    public async Task PlayMovie_AnnouncesThatItsGuardChanged_WhenTheSelectionChanges()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var announcements = 0;
        viewModel.PlayMovieCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task StoppingAFilm_RecordsWhereItGotTo()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = await OpenFilmAsync(context);
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        // The engine is playing and reports a position, which the window's timer samples.
        context.Session.Position = TimeSpan.FromMinutes(40);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        var write = context.Store.ProgressWrites.ShouldHaveSingleItem();
        write.Kind.ShouldBe(ContentKind.Movie);
        write.ItemId.ShouldBe(1);
        write.Outcome.ShouldBe(WatchOutcome.Resumable);
        write.Position.ShouldBe(TimeSpan.FromMinutes(40));
    }

    /// <summary>
    /// The engine has no position left once the stream is closed, so a recorder that only looked when asked
    /// to save would always save nothing. This is that case: nothing samples between playing and stopping.
    /// </summary>
    [Fact]
    public async Task StoppingAFilm_WhenTheEngineHasAlreadyForgottenThePosition_UsesTheLastSample()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = await OpenFilmAsync(context);
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(30);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // The engine loses both the moment the stream goes, exactly as the fake does on StopAsync.
        // Act
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        context.Store.ProgressWrites.ShouldHaveSingleItem().Position.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task ClosingTheWindowWhileAFilmPlays_RecordsWhereItGotTo()
    {
        // Arrange: the commonest way a film is left, and the one that needs a final sample of its own
        // because the last timer tick may be seconds old.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = await OpenFilmAsync(context);
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(12);
        context.Session.Duration = FilmLength;

        // Act
        await viewModel.ShutdownAsync();

        // Assert
        context.Store.ProgressWrites.ShouldHaveSingleItem().Position.ShouldBe(TimeSpan.FromMinutes(12));
    }

    [Fact]
    public async Task SwitchingFromAFilmToAChannel_RecordsTheFilmFirst()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.Channels.Add(new Channel { Id = 5, SourceId = 1, ExternalId = "101", Name = "Erste" });

        var viewModel = await OpenFilmAsync(context);
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(20);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act
        viewModel.Channels.SelectedChannel = viewModel.VisibleChannels()[0];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        context.Store.ProgressWrites.ShouldHaveSingleItem().Position.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task PlayingAChannel_RecordsNothing()
    {
        // Arrange: live television has no position and nothing to resume.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(new Channel { Id = 5, SourceId = 1, ExternalId = "101", Name = "Erste" });

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Channels.SelectedChannel = viewModel.VisibleChannels()[0];

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        context.Store.ProgressWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFilmThatWillNotOpen_RecordsNothing()
    {
        // Arrange: leaving the recorder following a film that never played would attribute the next stop
        // to it, storing a position in something nobody watched.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = await OpenFilmAsync(context);
        context.Session.SwitchException = new PlaybackFailedException(
            "offline",
            new MediaRequest(new Uri("http://x/1.mp4"), "agent", StreamFormat.ProgressiveFile, "Arrival"));

        // Act
        await viewModel.PlayMovieCommand.ExecuteAsync(null);
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        context.Store.ProgressWrites.ShouldBeEmpty();
        viewModel.NowPlaying.ShouldBeEmpty();
    }

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
        await viewModel.PlayEpisodeCommand.ExecuteAsync(new EpisodeItemViewModel(episode, seasonNumber: 1));

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
        await viewModel.PlayEpisodeCommand.ExecuteAsync(viewModel.SeriesCatalogue.SelectedEpisode);

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
        await viewModel.ResumeEntryCommand.ExecuteAsync(entry);

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
        await viewModel.ForgetEntryCommand.ExecuteAsync(entry);

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
        await viewModel.ForgetEntryCommand.ExecuteAsync(entry);

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
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

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
        await viewModel.ForgetEntryCommand.ExecuteAsync(entry);
        await viewModel.StopCommand.ExecuteAsync(null);

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
        await viewModel.ResumeEntryCommand.ExecuteAsync(entry);

        // Assert
        context.Session.Started.ShouldBeEmpty();
        viewModel.Status.Text.ShouldContain("no longer in the catalogue");
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

    /// <summary>
    /// Opens the film section with its first film selected and its detail loaded, which is the state every
    /// playback test starts from.
    /// </summary>
    private static async Task<MainViewModel> OpenFilmAsync(MainViewModelHarness context)
    {
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await viewModel.WaitForIdleAsync();

        return viewModel;
    }

    private static XtreamSource CreateSource(
        int id = 1,
        bool supportsVod = true,
        bool supportsSeries = true)
    {
        return new XtreamSourceBuilder()
            .WithId(id)
            .WithName($"Source {id}")
            .WithCredentials("alice", "s3cret")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = supportsVod,
                SupportsSeries = supportsSeries,
                ProbedAtUtc = MainViewModelHarness.Now,
            })
            .Build();
    }

    private static VodItem Movie(int id, string name)
    {
        return new VodItem
        {
            Id = id,
            SourceId = 1,
            ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
            ContainerExtension = "mkv",
        };
    }

    private static Series SeriesEntry(int id, string name)
    {
        return new Series
        {
            Id = id,
            SourceId = 1,
            ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
        };
    }

    private static Episode Episode(string externalId, string title, int number)
    {
        return new Episode
        {
            ExternalId = externalId,
            Title = title,
            Number = number,
            ContainerExtension = "mkv",
        };
    }
}
