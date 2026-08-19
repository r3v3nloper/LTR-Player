using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Playback;
using static LTR.Player.Wpf.VodSectionFixtures;

namespace LTR.Player.Wpf;

/// <summary>
/// The film section, and above all what happens to a resume position.
/// </summary>
/// <remarks>
/// The interesting behaviour is in the seams: a position that has to be sampled before playback stops, and a
/// detail fetch that must not overwrite a newer selection. Each has a wrong version that looks perfectly
/// correct.
/// </remarks>
public sealed class MovieSectionTests
{
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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.RestartMovieCommand.ExecuteAsync(null);

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
        viewModel.PlaybackCommands.RestartMovieCommand.CanExecute(null).ShouldBeFalse();
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
        viewModel.PlaybackCommands.PlayMovieCommand.CanExecuteChanged += (_, _) => announcements++;

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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

        // The engine is playing and reports a position, which the window's timer samples.
        context.Session.Position = TimeSpan.FromMinutes(40);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act
        await viewModel.PlaybackCommands.StopCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(30);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // The engine loses both the moment the stream goes, exactly as the fake does on StopAsync.
        // Act
        await viewModel.PlaybackCommands.StopCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);

        context.Session.Position = TimeSpan.FromMinutes(20);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act
        viewModel.Channels.SelectedChannel = viewModel.VisibleChannels()[0];
        await viewModel.PlaybackCommands.PlaySelectedCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.PlaybackCommands.StopCommand.ExecuteAsync(null);

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
        await viewModel.PlaybackCommands.PlayMovieCommand.ExecuteAsync(null);
        await viewModel.PlaybackCommands.StopCommand.ExecuteAsync(null);

        // Assert
        context.Store.ProgressWrites.ShouldBeEmpty();
        viewModel.NowPlaying.ShouldBeEmpty();
    }

}
