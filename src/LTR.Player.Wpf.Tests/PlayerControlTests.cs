using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers what the shell does with a keystroke: zapping, and closing off a stream that ended by itself.
/// </summary>
/// <remarks>
/// Through the composed view model rather than the overlay on its own, because both of these need the
/// channel list, playback and the resume recorder at once — and that combination is only assembled here.
/// </remarks>
public sealed class PlayerControlTests
{
    private static readonly TimeSpan FilmLength = TimeSpan.FromMinutes(100);

    [Fact]
    public async Task ZapNext_PlaysTheChannelAfterTheOneSelected()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        viewModel.Channels.SelectedChannel.ShouldNotBeNull();
        viewModel.Channels.SelectedChannel.Name.ShouldBe("Zweite");
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Zweite");
    }

    [Fact]
    public async Task ZapPrevious_PlaysTheChannelBefore()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.SelectedChannel = Row(viewModel, index: 2);

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapPrevious, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Zweite");
    }

    [Fact]
    public async Task Zapping_WithNothingSelected_StartsAtTheTopOfTheList()
    {
        // Arrange: refusing would make the key do nothing at all on a freshly opened window.
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.SelectedChannel = null;

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Erste");
    }

    /// <remarks>
    /// Silence at the ends rather than wrapping. A wrap is indistinguishable from a zap that did nothing
    /// except by watching the picture, and an unwanted one costs a stream open — which costs the
    /// subscription's one connection.
    /// </remarks>
    [Fact]
    public async Task Zapping_StopsAtTheEndsOfTheList()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.SelectedChannel = Row(viewModel, index: 2);

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldBeEmpty("there is nothing after the last channel");
        viewModel.Channels.SelectedChannel.ShouldNotBeNull();
        viewModel.Channels.SelectedChannel.Name.ShouldBe("Dritte", "and the selection did not move");
    }

    /// <remarks>
    /// Zapping walks what the viewer can see, not what is loaded: a search or a category having narrowed the
    /// list is exactly the set they mean by "the next channel". This is also the case that keeps the selection
    /// honest now that a key press asks the view by index instead of copying it out — an index into the
    /// backing list would land on a hidden row here.
    /// </remarks>
    [Fact]
    public async Task Zapping_WithTheListFiltered_SkipsWhatIsHidden()
    {
        // Arrange: only Erste and Dritte contain an "r", so Zweite is hidden between them.
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.ChannelFilterText = "r";
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);
        viewModel.Channels.SelectedChannel.Name.ShouldBe("Erste");

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        viewModel.Channels.SelectedChannel.ShouldNotBeNull();
        viewModel.Channels.SelectedChannel.Name.ShouldBe("Dritte");
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Dritte");
    }

    /// <remarks>
    /// Zapping only means anything in the channel list, so it switches back to it. Having the picture change
    /// while the film catalogue stays on screen leaves no way to tell what is playing.
    /// </remarks>
    [Fact]
    public async Task Zapping_FromAnotherSection_ReturnsToTheChannelList()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);
        viewModel.SelectedSection = CatalogueSection.ContinueWatching;

        // Act
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        viewModel.SelectedSection.ShouldBe(CatalogueSection.Live);
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Zweite");
    }

    [Fact]
    public async Task Zapping_WalksOnlyTheChannelsTheFilterAdmits()
    {
        // Arrange: a category or a search having narrowed the list is exactly the set the viewer means by
        // "the next channel".
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.Channels.ChannelFilterText = "te";

        viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().Select(row => row.Name)
            .ShouldBe(["Erste", "Zweite", "Dritte"], "every fixture name matches, so narrow it further");

        viewModel.Channels.ChannelFilterText = "Zwei";

        // Act: from the only admitted channel, so a walk over the unfiltered list would show up here.
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);
        await viewModel.PerformAsync(PlayerAction.ZapNext, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task VolumeKeys_ReachTheEngine()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act
        await viewModel.PerformAsync(PlayerAction.VolumeDown, TestContext.Current.CancellationToken);

        // Assert
        context.Session.Volume.ShouldBe(100 - PlayerOverlayViewModel.VolumeStep);

        // Act
        await viewModel.PerformAsync(PlayerAction.ToggleMute, TestContext.Current.CancellationToken);

        // Assert
        context.Session.IsMuted.ShouldBeTrue();
    }

    [Fact]
    public async Task TheInfoKey_BringsTheControlsUpWithoutChangingAnything()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAFilmPlayingAsync(context);

        viewModel.PlayerOverlay.Sample();
        var stopsBefore = context.Session.StopCount;

        // Act
        await viewModel.PerformAsync(PlayerAction.ShowInfo, TestContext.Current.CancellationToken);
        viewModel.PlayerOverlay.Sample();

        // Assert
        viewModel.PlayerOverlay.IsVisible.ShouldBeTrue();
        viewModel.PlayerOverlay.IsPaused.ShouldBeFalse();
        context.Session.StopCount.ShouldBe(stopsBefore);
    }

    [Fact]
    public async Task StartingSomething_BringsTheControlsUpToSayWhatItIs()
    {
        // Arrange: the controls take themselves away again after a few seconds, so a channel change that
        // announced nothing would leave the viewer to recognise the channel from the picture.
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        viewModel.PlayerOverlay.Sample();
        viewModel.PlayerOverlay.IsVisible.ShouldBeFalse("nothing is playing yet");

        // Act
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        viewModel.PlayerOverlay.Sample();

        // Assert
        viewModel.PlayerOverlay.IsVisible.ShouldBeTrue();
    }

    /// <remarks>
    /// Backlog rank 15. A film that plays out and sits there is not a stop anybody asked for, so nothing
    /// else brought it to the point where a position is written — it stayed on the continue-watching list
    /// until the next channel change or the window closing.
    /// </remarks>
    [Fact]
    public async Task AFilmReachingItsOwnEnd_IsRecordedAsWatched()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithAFilmPlayingAsync(context);

        // Watched almost to the end, sampled while it was still playing as the timer does.
        context.Session.Position = FilmLength - TimeSpan.FromSeconds(20);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act: the engine reports the stream having run out on its own.
        context.Session.ReachEndOfStream();
        await viewModel.SamplePlaybackAsync();

        // Assert
        var write = context.Store.ProgressWrites.ShouldHaveSingleItem();
        write.Kind.ShouldBe(ContentKind.Movie);
        write.ItemId.ShouldBe(1);
        write.Outcome.ShouldBe(WatchOutcome.Finished);

        // And the connection went back, rather than the session being left holding a dead stream.
        context.Session.Current.ShouldBeNull();
        viewModel.NowPlaying.ShouldBeEmpty();
        viewModel.Status.Text.ShouldContain("ended");
    }

    /// <remarks>
    /// The other half of the same rule, and the reason a reason is needed at all. The identical transition
    /// occurs in the middle of every channel change, where the position was recorded a moment earlier —
    /// acting on it there would overwrite a deliberate position with whatever the engine reported while
    /// tearing down.
    /// </remarks>
    [Fact]
    public async Task AChannelChange_DoesNotRecordASecondTime()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Channels.Add(Channel(id: 10, "101", "Erste"));

        var viewModel = await WithAFilmPlayingAsync(context);

        context.Session.Position = TimeSpan.FromMinutes(30);
        context.Session.Duration = FilmLength;
        await viewModel.SamplePlaybackAsync();

        // Act: zapping to a channel, which stops the film — the same Stopped state, a different reason.
        viewModel.SelectedSection = CatalogueSection.Live;
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.SamplePlaybackAsync();

        // Assert
        context.Store.ProgressWrites.ShouldHaveSingleItem().Position.ShouldBe(TimeSpan.FromMinutes(30));
        viewModel.NowPlaying.ShouldBe("Erste", "and the channel is still playing");
    }

    /// <remarks>
    /// The message this replaced named an offline channel and a busy connection in the same breath, and
    /// mentioned neither an expired subscription nor rejected credentials — four causes that look identical
    /// from inside the engine and want four different things from the viewer.
    /// </remarks>
    [Fact]
    public async Task AChannelThatWillNotPlay_ReportsWhatTheProviderSaid()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        context.Session.SwitchException = new PlaybackFailedException(
            "the provider refused the connection",
            new MediaRequest(
                new Uri("http://panel.example/live/u/p/101.ts"),
                "TestAgent/1.0",
                StreamFormat.MpegTs,
                "Erste"));

        context.Failures.Reason = StreamFailureReason.ConnectionLimitReached;
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        context.Failures.Asked.ShouldHaveSingleItem().Name.ShouldBe("Source 1");
        viewModel.Status.Text.ShouldContain("Erste");
        viewModel.Status.Text.ShouldContain("other device");
        viewModel.NowPlaying.ShouldBeEmpty("nothing is playing, so the overlay must not claim otherwise");
    }

    [Fact]
    public async Task AnExpiredSubscription_IsNotReportedAsAnOfflineChannel()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        context.Session.SwitchException = new PlaybackFailedException(
            "the provider refused the connection",
            new MediaRequest(
                new Uri("http://panel.example/live/u/p/101.ts"),
                "TestAgent/1.0",
                StreamFormat.MpegTs,
                "Erste"));

        context.Failures.Reason = StreamFailureReason.SubscriptionExpired;
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        viewModel.Status.Text.ShouldContain("expired");
        viewModel.Status.Text.ShouldNotContain("Try another one");
    }

    /// <remarks>
    /// Zapping onwards cancels the open still in flight, which is the intended behaviour of a channel change
    /// rather than a failure — so the panel must not be interrogated about it, once per key press.
    /// </remarks>
    [Fact]
    public async Task AZapThatSupersedesTheOpen_DoesNotAskTheProviderAnything()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = await WithThreeChannelsAsync(context);

        context.Session.SwitchException = new OperationCanceledException();
        viewModel.Channels.SelectedChannel = Row(viewModel, index: 0);

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        context.Failures.Asked.ShouldBeEmpty();
    }

    private static async Task<MainViewModel> WithThreeChannelsAsync(MainViewModelHarness context)
    {
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(Channel(id: 10, "101", "Erste"));
        context.Store.Channels.Add(Channel(id: 11, "102", "Zweite"));
        context.Store.Channels.Add(Channel(id: 12, "103", "Dritte"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        return viewModel;
    }

    private static async Task<MainViewModel> WithAFilmPlayingAsync(MainViewModelHarness context)
    {
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(id: 1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await WaitForIdleAsync(viewModel);

        await viewModel.PlayMovieCommand.ExecuteAsync(null);

        return viewModel;
    }

    /// <summary>Waits for the shell to finish reacting to the last selection.</summary>
    private static async Task WaitForIdleAsync(MainViewModel viewModel)
    {
        while (!viewModel.SectionWorkCompletion.IsCompleted)
        {
            await viewModel.SectionWorkCompletion;
        }
    }

    private static ChannelItemViewModel Row(MainViewModel viewModel, int index)
    {
        return viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().ElementAt(index);
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Source 1",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
            Capabilities = new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = true,
                ProbedAtUtc = MainViewModelHarness.Now,
            },
        };
    }

    private static Channel Channel(int id, string externalId, string name)
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
        };
    }
}
