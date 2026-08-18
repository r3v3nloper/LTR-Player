using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers command enabling and filtering.
/// </summary>
/// <remarks>
/// This project exists because three shipped defects had one cause: a command whose guard reads a
/// property that never notified it, so CanExecute was evaluated once at construction and never again.
/// Connect stayed disabled whatever was typed, Refresh and Remove stayed disabled once a source existed,
/// and a filter refresh silently dropped the selection. None of that is presentation — it is logic with
/// no test behind it, found by a person looking at a window.
/// <para>
/// Everything is exercised through the composed <see cref="MainViewModel"/> rather than through the two
/// halves on their own. The coordination between them — a selection loading a catalogue, a removal
/// releasing the stream first, a guard notified across an object boundary — is where this class earns
/// its keep, and only the composition has it.
/// </para>
/// </remarks>
public sealed class MainViewModelTests
{

    [Fact]
    public void ConnectCommand_IsDisabledUntilTheXtreamFieldsAreFilled()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act & Assert: each field in turn, because the guard reads all three.
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeFalse("nothing entered yet");

        viewModel.SourceManagement.PanelUrl = "http://panel.example:8080";
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeFalse("no username yet");

        viewModel.SourceManagement.Username = "alice";
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeFalse("no password yet");

        viewModel.SourceManagement.Password = "s3cret";
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <remarks>
    /// This is the test that actually catches the shipped defect, and the reason the ones asserting
    /// CanExecute alone are not enough: RelayCommand.CanExecute invokes the guard directly, so it always
    /// reports the current state whether or not anything was notified. What broke was that WPF never
    /// re-queried, because CanExecuteChanged was never raised — the button therefore kept whatever state
    /// it had at construction time.
    /// </remarks>
    [Theory]
    [InlineData(nameof(SourceManagementViewModel.PanelUrl))]
    [InlineData(nameof(SourceManagementViewModel.Username))]
    [InlineData(nameof(SourceManagementViewModel.Password))]
    [InlineData(nameof(SourceManagementViewModel.PlaylistUrl))]
    [InlineData(nameof(SourceManagementViewModel.NewSourceProtocol))]
    public void ConnectCommand_AnnouncesThatItsGuardChanged_WhenAFieldItReadsChanges(string propertyName)
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        var announcements = 0;
        viewModel.SourceManagement.ConnectCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        SetProperty(viewModel, propertyName);

        // Assert
        announcements.ShouldBeGreaterThan(0, $"{propertyName} is read by the guard and must notify it");
    }

    [Fact]
    public async Task RefreshAndRemove_AnnounceThatTheirGuardChanged_WhenTheSelectedSourceChanges()
    {
        // Arrange: both stayed disabled in a shipped build for exactly this reason.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        var viewModel = context.Build();

        var refreshAnnouncements = 0;
        var removeAnnouncements = 0;
        viewModel.SourceManagement.RefreshCommand.CanExecuteChanged += (_, _) => refreshAnnouncements++;
        viewModel.SourceManagement.RemoveSourceCommand.CanExecuteChanged += (_, _) => removeAnnouncements++;

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        refreshAnnouncements.ShouldBeGreaterThan(0);
        removeAnnouncements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ChannelCommands_AnnounceThatTheirGuardChanged_WhenTheSelectionChanges()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var playAnnouncements = 0;
        var favouriteAnnouncements = 0;
        viewModel.PlaySelectedCommand.CanExecuteChanged += (_, _) => playAnnouncements++;
        viewModel.Channels.ToggleFavoriteCommand.CanExecuteChanged += (_, _) => favouriteAnnouncements++;

        // Act
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();

        // Assert
        playAnnouncements.ShouldBeGreaterThan(0);
        favouriteAnnouncements.ShouldBeGreaterThan(0);
    }

    private static void SetProperty(MainViewModel viewModel, string propertyName)
    {
        switch (propertyName)
        {
            case nameof(SourceManagementViewModel.PanelUrl):
                viewModel.SourceManagement.PanelUrl = "http://panel.example:8080";
                break;

            case nameof(SourceManagementViewModel.Username):
                viewModel.SourceManagement.Username = "alice";
                break;

            case nameof(SourceManagementViewModel.Password):
                viewModel.SourceManagement.Password = "s3cret";
                break;

            case nameof(SourceManagementViewModel.PlaylistUrl):
                viewModel.SourceManagement.PlaylistUrl = "http://host/list.m3u";
                break;

            case nameof(SourceManagementViewModel.NewSourceProtocol):
                viewModel.SourceManagement.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Unhandled property.");
        }
    }

    [Fact]
    public void ConnectCommand_ForAPlaylistNeedsOnlyTheAddress()
    {
        // Arrange: a playlist has no credentials, so requiring them would make it unaddable.
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act
        viewModel.SourceManagement.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;
        viewModel.SourceManagement.PlaylistUrl = "http://host/list.m3u";

        // Assert
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void ConnectCommand_WhenSwitchingProtocol_ReEvaluatesItsGuard()
    {
        // Arrange: filling the Xtream fields must not leave Connect enabled for a playlist with no
        // address entered.
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        viewModel.SourceManagement.PanelUrl = "http://panel.example:8080";
        viewModel.SourceManagement.Username = "alice";
        viewModel.SourceManagement.Password = "s3cret";
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeTrue();

        // Act
        viewModel.SourceManagement.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;

        // Assert
        viewModel.SourceManagement.ConnectCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshAndRemove_BecomeAvailableOnceASourceIsSelected()
    {
        // Arrange: both were permanently disabled in a shipped build, because their guard reads
        // SelectedSource and SelectedSource did not notify them.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        var viewModel = context.Build();

        viewModel.SourceManagement.RefreshCommand.CanExecute(null).ShouldBeFalse("no source selected before loading");

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.SourceManagement.SelectedSource.ShouldNotBeNull();
        viewModel.SourceManagement.RefreshCommand.CanExecute(null).ShouldBeTrue();
        viewModel.SourceManagement.RemoveSourceCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithNoSources_ShowsTheAddForm()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.SourceManagement.IsAddingSource.ShouldBeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithAStoredSource_LandsInTheChannelList()
    {
        // Arrange: a restart should not ask for the subscription again.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));
        context.Store.Categories.Add(CreateCategory(1, "10", "Sport"));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.SourceManagement.IsAddingSource.ShouldBeFalse();
        viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().Count().ShouldBe(1);
        viewModel.Channels.Categories.Count.ShouldBe(2, "the all-categories entry plus the stored one");
    }

    [Fact]
    public async Task ChangingTheFilter_KeepsTheSelectedChannelWhenItStillQualifies()
    {
        // Arrange: a collection view refresh raises a reset and the list box drops its selection, which
        // disabled the favourite command while a channel was still playing.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD"));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "DE: ZDF HD"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()
            .Single(channel => channel.Name == "FR: TF1 HD");

        // Act
        viewModel.Channels.ChannelFilterText = "tf1";

        // Assert
        viewModel.Channels.SelectedChannel.ShouldNotBeNull();
        viewModel.Channels.SelectedChannel.Name.ShouldBe("FR: TF1 HD");
        viewModel.Channels.ToggleFavoriteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ChangingTheFilter_DropsTheSelectionWhenItNoLongerQualifies()
    {
        // Arrange: the counterpart. Keeping a row selected that the filter excludes would be worse.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        viewModel.Channels.ChannelFilterText = "zdf";

        // Assert
        viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCategoryFilter_NarrowsTheListAndCombinesWithTheSearchText()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Categories.Add(CreateCategory(1, "10", "France"));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD", "10"));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "FR: ZDF HD", "10"));
        context.Store.Channels.Add(CreateChannel(1, 12, "103", "DE: TF1 clone", "20"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Channels.SelectedCategory = viewModel.Channels.Categories
            .Single(category => category.ExternalId == "10");
        viewModel.Channels.ChannelFilterText = "tf1";

        // Assert
        var visible = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().ToList();
        visible.ShouldHaveSingleItem().Name.ShouldBe("FR: TF1 HD");
    }

    [Fact]
    public async Task ToggleFavorite_PersistsTheChangeAndUpdatesTheRow()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        await viewModel.Channels.ToggleFavoriteCommand.ExecuteAsync(null);

        // Assert
        viewModel.Channels.SelectedChannel.IsFavorite.ShouldBeTrue();
        context.Store.FavoriteWrites.ShouldHaveSingleItem().ShouldBe((10, true));
    }

    [Fact]
    public async Task ShowingOnlyFavourites_HidesTheRest()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Favourite", isFavorite: true));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "Ordinary"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Channels.ShowFavoritesOnly = true;

        // Assert
        viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("Favourite");
    }

    /// <summary>
    /// The interaction the row used to mirror its favourite flag into the entity for: with the favourites
    /// filter on, un-favouriting has to remove the row, which only happens if the filter reads what the row
    /// now says rather than what the entity says.
    /// </summary>
    [Fact]
    public async Task UnFavouriting_WhileShowingOnlyFavourites_RemovesTheRow()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Favourite", isFavorite: true));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "Also favourite", isFavorite: true));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Channels.ShowFavoritesOnly = true;
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()
            .Single(row => row.Name == "Favourite");

        // Act
        await viewModel.Channels.ToggleFavoriteCommand.ExecuteAsync(null);

        // Assert
        viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("Also favourite");

        context.Store.FavoriteWrites.ShouldHaveSingleItem().ShouldBe((10, false));
    }

    /// <summary>
    /// Closing the window has to abandon a catalogue load in flight. Loading seventeen thousand channels is
    /// the longest thing the shell does, and it used to be started with a token nothing could cancel — so
    /// closing meant waiting for it.
    /// </summary>
    [Fact]
    public async Task Shutdown_AbandonsACatalogueLoadStillRunning()
    {
        // Arrange: a refresh, because that is the path that awaits the catalogue load and therefore the one
        // that would keep the window from closing.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        context.Store.BlockChannelLoad = true;
        var refresh = viewModel.SourceManagement.RefreshCommand.ExecuteAsync(null);

        refresh.IsCompleted.ShouldBeFalse("the reload is deliberately still in flight");

        // Act
        await viewModel.ShutdownAsync();

        // Assert: it ended. Bounded, because the failure being guarded against is a load that never ends —
        // without the limit this test would hang rather than fail.
        await refresh.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        refresh.IsCompletedSuccessfully.ShouldBeTrue("a cancelled reload is not a failure");
    }

    [Fact]
    public async Task RemoveSource_StopsPlaybackBeforeDeleting()
    {
        // Arrange: the stream in flight belongs to the source about to disappear.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.SourceManagement.RemoveSourceCommand.ExecuteAsync(null);

        // Assert
        context.Session.StopCount.ShouldBeGreaterThan(0);
        context.Store.DeletedSourceIds.ShouldContain(1);
        viewModel.SourceManagement.IsAddingSource.ShouldBeTrue("with no sources left there is nothing to show");
    }

    [Fact]
    public async Task Connect_WhenTheAccountIsRejected_ReportsItAndStaysOnTheForm()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Import.Account = ProviderAccount.Unauthenticated;
        var viewModel = context.Build();

        viewModel.SourceManagement.PanelUrl = "http://panel.example:8080";
        viewModel.SourceManagement.Username = "alice";
        viewModel.SourceManagement.Password = "wrong";

        // Act
        await viewModel.SourceManagement.ConnectCommand.ExecuteAsync(null);

        // Assert
        viewModel.SourceManagement.IsAddingSource.ShouldBeTrue();
        viewModel.Status.Text.ShouldContain("rejected");
        viewModel.SourceManagement.Sources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connect_WithAnInvalidPanelAddress_ExplainsWhatWasExpected()
    {
        // Arrange: "host:8080" parses as an absolute URI, so it used to be accepted and fail later.
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        viewModel.SourceManagement.PanelUrl = "panel.example:8080";
        viewModel.SourceManagement.Username = "alice";
        viewModel.SourceManagement.Password = "s3cret";

        // Act
        await viewModel.SourceManagement.ConnectCommand.ExecuteAsync(null);

        // Assert
        viewModel.Status.Text.ShouldContain("http://");
        context.Import.Imported.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlaySelected_HandsTheResolvedAddressToPlayback()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        context.Session.Started.ShouldHaveSingleItem().DisplayName.ShouldBe("Erste");
        viewModel.NowPlaying.ShouldBe("Erste");
    }

    [Fact]
    public async Task PlaySelected_WhenSupersededByTheNextChannel_IsNotTreatedAsAnError()
    {
        // Arrange: zapping cancels the open still in flight. Unhandled, that surfaced as an error dialog
        // for an ordinary key press.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));
        context.Session.SwitchException = new OperationCanceledException();

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.SelectedChannel = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        var act = async () => await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        await act.ShouldNotThrowAsync();
    }

    private static XtreamSource CreateSource(int id)
    {
        return new XtreamSourceBuilder().WithId(id).WithCredentials("alice", "s3cret").Build();
    }

    private static Category CreateCategory(int sourceId, string externalId, string name)
    {
        return new Category
        {
            Id = int.Parse(externalId, provider: null),
            SourceId = sourceId,
            ExternalId = externalId,
            Name = name,
            Kind = ContentKind.Live,
        };
    }

    private static Channel CreateChannel(
        int sourceId,
        int id,
        string externalId,
        string name,
        string? categoryExternalId = null,
        bool isFavorite = false,
        int? guideChannelId = null)
    {
        return new Channel
        {
            Id = id,
            SourceId = sourceId,
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
            IsFavorite = isFavorite,
            GuideChannelId = guideChannelId,
        };
    }

    private static EpgEntry CreateProgramme(
        int guideChannelId,
        string title,
        DateTimeOffset startUtc,
        TimeSpan? duration = null)
    {
        return new EpgEntry
        {
            GuideChannelId = guideChannelId,
            Title = title,
            StartUtc = startUtc,
            StopUtc = startUtc + (duration ?? TimeSpan.FromHours(1)),
        };
    }
}
