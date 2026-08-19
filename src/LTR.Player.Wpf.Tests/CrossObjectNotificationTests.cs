using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Every notification the shell has to carry across an object boundary by hand, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <c>[NotifyCanExecuteChangedFor]</c> cannot cross an object boundary: a command lives on the shell and
/// the property its guard reads belongs to a section. The shell therefore subscribes to each section and
/// forwards, and a forward that is missing is invisible — <c>CanExecute</c> invokes the guard directly and
/// answers correctly, while WPF never re-queries and the button keeps the state it had when the window
/// opened. That defect has shipped three times.
/// </para>
/// <para>
/// Several of these forwards had only their *value* asserted, which is the assertion that cannot catch it.
/// They are gathered here so the set is visible as a set: anything added to the shell that guards on a
/// section's state belongs in this file, and so does anything the shell recomputes from one.
/// </para>
/// </remarks>
public sealed class CrossObjectNotificationTests
{
    [Fact]
    public async Task RestartMovie_AnnouncesThatItsGuardChanged_WhenTheSelectionChanges()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var announcements = 0;
        viewModel.RestartMovieCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task RestartMovie_AnnouncesThatItsGuardChanged_WhenTheDetailArrives()
    {
        // Arrange: the guard reads the resume position, which only the detail carries — so the detail
        // arriving is a second, separate reason to re-ask. The answer is held back, or it lands inside the
        // selection's own setter and the two reasons cannot be told apart.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.VodDetail.Movies.Add(Movie(1, "Arrival"));
        context.VodDetail.Gate = new TaskCompletionSource();

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];

        var announcements = 0;
        viewModel.RestartMovieCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        context.VodDetail.Gate.SetResult();

        while (!viewModel.SectionWorkCompletion.IsCompleted)
        {
            await viewModel.SectionWorkCompletion;
        }

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ResumeEntry_AnnouncesThatItsGuardChanged_WhenTheSelectedEntryChanges()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.ContinueWatching.Add(Entry());

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var announcements = 0;
        viewModel.ResumeEntryCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        viewModel.ContinueWatching.SelectedEntry = viewModel.ContinueWatching.Entries[0];

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task IsImportingGuide_IsAnnounced_WhenAnImportStartsAndFinishes()
    {
        // Arrange: the progress panel binds this, and the coordinator owns the flag behind it. The import is
        // held open so the two announcements cannot collapse into one.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.GuideImport.BlockUntilReleased = true;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var announcements = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsImportingGuide))
            {
                announcements++;
            }
        };

        // Act
        viewModel.ImportGuideCommand.Execute(parameter: null);
        var whileRunning = announcements;

        context.GuideImport.Release();
        await viewModel.GuideImportCompletion;

        // Assert
        whileRunning.ShouldBeGreaterThan(0, "the panel has to appear when the import starts");
        announcements.ShouldBeGreaterThan(whileRunning, "and go away when it finishes");
    }

    [Fact]
    public async Task NowPlaying_IsAnnounced_WhenPlaybackStarts()
    {
        // Arrange: the overlay binds NowPlaying on the shell rather than reaching into the coordinator.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(CreateChannel(1, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.VisibleChannels()[0];

        var announcements = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.NowPlaying))
            {
                announcements++;
            }
        };

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        announcements.ShouldBeGreaterThan(0);
        viewModel.NowPlaying.ShouldBe("Erste");
    }

    [Fact]
    public async Task IsShowingCatalogue_IsAnnounced_WhenTheAddSourceFormOpens()
    {
        // Arrange: the second reason the left pane changes, and the one no test covered — the settings pane
        // was covered and this was not, though both compute the same shell property from another object.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var announcements = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsShowingCatalogue))
            {
                announcements++;
            }
        };

        // Act
        viewModel.SourceManagement.ShowAddSourceCommand.Execute(parameter: null);

        // Assert
        announcements.ShouldBeGreaterThan(0);
        viewModel.IsShowingCatalogue.ShouldBeFalse("the form has taken the pane");
    }

    [Fact]
    public async Task PlaySelected_AnnouncesThatItsGuardChanged_WhenTheSourceSelectionChanges()
    {
        // Arrange: switching source clears the channel selection, so the button has to fall back to
        // disabled — the same forward as choosing a channel, from the other direction.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Sources.Add(CreateSource(id: 2));
        context.Store.Channels.Add(CreateChannel(1, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.VisibleChannels()[0];

        var announcements = 0;
        viewModel.PlaySelectedCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        viewModel.SourceManagement.SelectedSource = viewModel.SourceManagement.Sources[1];

        while (!viewModel.SectionWorkCompletion.IsCompleted)
        {
            await viewModel.SectionWorkCompletion;
        }

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The forward added when what previous and next act on moved to the coordinator.
    /// </summary>
    /// <remarks>
    /// Their guard reads <see cref="PlaybackCoordinator.NowPlayingItem"/>, so the same crossing applies as for
    /// every other entry here — and it applies in both directions. Starting a film has to close the buttons and
    /// stopping it has to reopen them, which is the case a forward registered for only one of the two would
    /// pass.
    /// </remarks>
    [Fact]
    public async Task PreviousAndNext_AnnounceTheirGuard_WhenWhatIsPlayingChanges()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await viewModel.WaitForIdleAsync();

        var next = 0;
        var previous = 0;
        viewModel.PlayNextCommand.CanExecuteChanged += (_, _) => next++;
        viewModel.PlayPreviousCommand.CanExecuteChanged += (_, _) => previous++;

        // Act
        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        // Assert
        next.ShouldBeGreaterThan(0);
        previous.ShouldBeGreaterThan(0);
        viewModel.PlayNextCommand.CanExecute(null).ShouldBeFalse("a film has no neighbour");

        // Act: and the other direction, which a one-way forward would not announce.
        next = 0;
        await viewModel.StopCommand.ExecuteAsync(null);

        // Assert
        next.ShouldBeGreaterThan(0);
        viewModel.PlayNextCommand.CanExecute(null).ShouldBeTrue("next means the next channel again");
    }

    private static XtreamSource CreateSource(int id = 1)
    {
        return new XtreamSourceBuilder()
            .WithId(id)
            .WithName($"Source {id}")
            .WithCredentials("alice", "s3cret")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = true,
                ProbedAtUtc = MainViewModelHarness.Now,
            })
            .Build();
    }

    private static Channel CreateChannel(int id, string externalId, string name)
    {
        return new Channel
        {
            Id = id,
            SourceId = 1,
            ExternalId = externalId,
            Name = name,
        };
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
            ResumePositionSeconds = 600,
        };
    }

    private static ContinueWatchingEntry Entry()
    {
        return new ContinueWatchingEntry(
            ContentKind.Movie,
            ItemId: 1,
            Title: "Arrival",
            Subtitle: string.Empty,
            CoverUrl: null,
            PositionSeconds: 600,
            DurationSeconds: 6_000,
            LastWatchedUtc: MainViewModelHarness.Now);
    }
}
