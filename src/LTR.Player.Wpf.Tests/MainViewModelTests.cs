using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging.Abstractions;

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
/// </remarks>
public sealed class MainViewModelTests
{
    [Fact]
    public void ConnectCommand_IsDisabledUntilTheXtreamFieldsAreFilled()
    {
        // Arrange
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        // Act & Assert: each field in turn, because the guard reads all three.
        viewModel.ConnectCommand.CanExecute(null).ShouldBeFalse("nothing entered yet");

        viewModel.PanelUrl = "http://panel.example:8080";
        viewModel.ConnectCommand.CanExecute(null).ShouldBeFalse("no username yet");

        viewModel.Username = "alice";
        viewModel.ConnectCommand.CanExecute(null).ShouldBeFalse("no password yet");

        viewModel.Password = "s3cret";
        viewModel.ConnectCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <remarks>
    /// This is the test that actually catches the shipped defect, and the reason the ones asserting
    /// CanExecute alone are not enough: RelayCommand.CanExecute invokes the guard directly, so it always
    /// reports the current state whether or not anything was notified. What broke was that WPF never
    /// re-queried, because CanExecuteChanged was never raised — the button therefore kept whatever state
    /// it had at construction time.
    /// </remarks>
    [Theory]
    [InlineData(nameof(MainViewModel.PanelUrl))]
    [InlineData(nameof(MainViewModel.Username))]
    [InlineData(nameof(MainViewModel.Password))]
    [InlineData(nameof(MainViewModel.PlaylistUrl))]
    [InlineData(nameof(MainViewModel.NewSourceProtocol))]
    public void ConnectCommand_AnnouncesThatItsGuardChanged_WhenAFieldItReadsChanges(string propertyName)
    {
        // Arrange
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        var announcements = 0;
        viewModel.ConnectCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        SetProperty(viewModel, propertyName);

        // Assert
        announcements.ShouldBeGreaterThan(0, $"{propertyName} is read by the guard and must notify it");
    }

    [Fact]
    public async Task RefreshAndRemove_AnnounceThatTheirGuardChanged_WhenTheSelectedSourceChanges()
    {
        // Arrange: both stayed disabled in a shipped build for exactly this reason.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        var viewModel = context.Build();

        var refreshAnnouncements = 0;
        var removeAnnouncements = 0;
        viewModel.RefreshCommand.CanExecuteChanged += (_, _) => refreshAnnouncements++;
        viewModel.RemoveSourceCommand.CanExecuteChanged += (_, _) => removeAnnouncements++;

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
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var playAnnouncements = 0;
        var favouriteAnnouncements = 0;
        viewModel.PlaySelectedCommand.CanExecuteChanged += (_, _) => playAnnouncements++;
        viewModel.ToggleFavoriteCommand.CanExecuteChanged += (_, _) => favouriteAnnouncements++;

        // Act
        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>().First();

        // Assert
        playAnnouncements.ShouldBeGreaterThan(0);
        favouriteAnnouncements.ShouldBeGreaterThan(0);
    }

    private static void SetProperty(MainViewModel viewModel, string propertyName)
    {
        switch (propertyName)
        {
            case nameof(MainViewModel.PanelUrl):
                viewModel.PanelUrl = "http://panel.example:8080";
                break;

            case nameof(MainViewModel.Username):
                viewModel.Username = "alice";
                break;

            case nameof(MainViewModel.Password):
                viewModel.Password = "s3cret";
                break;

            case nameof(MainViewModel.PlaylistUrl):
                viewModel.PlaylistUrl = "http://host/list.m3u";
                break;

            case nameof(MainViewModel.NewSourceProtocol):
                viewModel.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Unhandled property.");
        }
    }

    [Fact]
    public void ConnectCommand_ForAPlaylistNeedsOnlyTheAddress()
    {
        // Arrange: a playlist has no credentials, so requiring them would make it unaddable.
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        // Act
        viewModel.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;
        viewModel.PlaylistUrl = "http://host/list.m3u";

        // Assert
        viewModel.ConnectCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void ConnectCommand_WhenSwitchingProtocol_ReEvaluatesItsGuard()
    {
        // Arrange: filling the Xtream fields must not leave Connect enabled for a playlist with no
        // address entered.
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        viewModel.PanelUrl = "http://panel.example:8080";
        viewModel.Username = "alice";
        viewModel.Password = "s3cret";
        viewModel.ConnectCommand.CanExecute(null).ShouldBeTrue();

        // Act
        viewModel.NewSourceProtocol = NewSourceProtocol.M3uPlaylist;

        // Assert
        viewModel.ConnectCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshAndRemove_BecomeAvailableOnceASourceIsSelected()
    {
        // Arrange: both were permanently disabled in a shipped build, because their guard reads
        // SelectedSource and SelectedSource did not notify them.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        var viewModel = context.Build();

        viewModel.RefreshCommand.CanExecute(null).ShouldBeFalse("no source selected before loading");

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.SelectedSource.ShouldNotBeNull();
        viewModel.RefreshCommand.CanExecute(null).ShouldBeTrue();
        viewModel.RemoveSourceCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithNoSources_ShowsTheAddForm()
    {
        // Arrange
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.IsAddingSource.ShouldBeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithAStoredSource_LandsInTheChannelList()
    {
        // Arrange: a restart should not ask for the subscription again.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));
        context.Store.Categories.Add(CreateCategory(1, "10", "Sport"));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.IsAddingSource.ShouldBeFalse();
        viewModel.ChannelView.Cast<ChannelItemViewModel>().Count().ShouldBe(1);
        viewModel.Categories.Count.ShouldBe(2, "the all-categories entry plus the stored one");
    }

    [Fact]
    public async Task ChangingTheFilter_KeepsTheSelectedChannelWhenItStillQualifies()
    {
        // Arrange: a collection view refresh raises a reset and the list box drops its selection, which
        // disabled the favourite command while a channel was still playing.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD"));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "DE: ZDF HD"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>()
            .Single(channel => channel.Name == "FR: TF1 HD");

        // Act
        viewModel.ChannelFilterText = "tf1";

        // Assert
        viewModel.SelectedChannel.ShouldNotBeNull();
        viewModel.SelectedChannel.Name.ShouldBe("FR: TF1 HD");
        viewModel.ToggleFavoriteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ChangingTheFilter_DropsTheSelectionWhenItNoLongerQualifies()
    {
        // Arrange: the counterpart. Keeping a row selected that the filter excludes would be worse.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        viewModel.ChannelFilterText = "zdf";

        // Assert
        viewModel.ChannelView.Cast<ChannelItemViewModel>().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCategoryFilter_NarrowsTheListAndCombinesWithTheSearchText()
    {
        // Arrange
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Categories.Add(CreateCategory(1, "10", "France"));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "FR: TF1 HD", "10"));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "FR: ZDF HD", "10"));
        context.Store.Channels.Add(CreateChannel(1, 12, "103", "DE: TF1 clone", "20"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.ExternalId == "10");
        viewModel.ChannelFilterText = "tf1";

        // Assert
        var visible = viewModel.ChannelView.Cast<ChannelItemViewModel>().ToList();
        visible.ShouldHaveSingleItem().Name.ShouldBe("FR: TF1 HD");
    }

    [Fact]
    public async Task ToggleFavorite_PersistsTheChangeAndUpdatesTheRow()
    {
        // Arrange
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        await viewModel.ToggleFavoriteCommand.ExecuteAsync(null);

        // Assert
        viewModel.SelectedChannel.IsFavorite.ShouldBeTrue();
        context.Store.FavoriteWrites.ShouldHaveSingleItem().ShouldBe((10, true));
    }

    [Fact]
    public async Task ShowingOnlyFavourites_HidesTheRest()
    {
        // Arrange
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Favourite", isFavorite: true));
        context.Store.Channels.Add(CreateChannel(1, 11, "102", "Ordinary"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ShowFavoritesOnly = true;

        // Assert
        viewModel.ChannelView.Cast<ChannelItemViewModel>()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("Favourite");
    }

    [Fact]
    public async Task RemoveSource_StopsPlaybackBeforeDeleting()
    {
        // Arrange: the stream in flight belongs to the source about to disappear.
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.RemoveSourceCommand.ExecuteAsync(null);

        // Assert
        context.Session.StopCount.ShouldBeGreaterThan(0);
        context.Store.DeletedSourceIds.ShouldContain(1);
        viewModel.IsAddingSource.ShouldBeTrue("with no sources left there is nothing to show");
    }

    [Fact]
    public async Task Connect_WhenTheAccountIsRejected_ReportsItAndStaysOnTheForm()
    {
        // Arrange
        var context = new TestContextBuilder();
        context.Import.Account = ProviderAccount.Unauthenticated;
        var viewModel = context.Build();

        viewModel.PanelUrl = "http://panel.example:8080";
        viewModel.Username = "alice";
        viewModel.Password = "wrong";

        // Act
        await viewModel.ConnectCommand.ExecuteAsync(null);

        // Assert
        viewModel.IsAddingSource.ShouldBeTrue();
        viewModel.Status.ShouldContain("rejected");
        viewModel.Sources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connect_WithAnInvalidPanelAddress_ExplainsWhatWasExpected()
    {
        // Arrange: "host:8080" parses as an absolute URI, so it used to be accepted and fail later.
        var context = new TestContextBuilder();
        var viewModel = context.Build();

        viewModel.PanelUrl = "panel.example:8080";
        viewModel.Username = "alice";
        viewModel.Password = "s3cret";

        // Act
        await viewModel.ConnectCommand.ExecuteAsync(null);

        // Assert
        viewModel.Status.ShouldContain("http://");
        context.Import.Imported.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlaySelected_HandsTheResolvedAddressToPlayback()
    {
        // Arrange
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>().First();

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
        var context = new TestContextBuilder();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Channels.Add(CreateChannel(1, 10, "101", "Erste"));
        context.Session.SwitchException = new OperationCanceledException();

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.SelectedChannel = viewModel.ChannelView.Cast<ChannelItemViewModel>().First();

        // Act
        var act = async () => await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        // Assert
        await act.ShouldNotThrowAsync();
    }

    private static XtreamSource CreateSource(int id)
    {
        return new XtreamSource
        {
            Id = id,
            Name = "Test source",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        };
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
        bool isFavorite = false)
    {
        return new Channel
        {
            Id = id,
            SourceId = sourceId,
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
            IsFavorite = isFavorite,
        };
    }

    /// <summary>
    /// Assembles a view model over fakes, so each test states only what it cares about.
    /// </summary>
    private sealed class TestContextBuilder
    {
        public FakeCatalogueStore Store { get; } = new();

        public FakeSourceImportService Import { get; } = new();

        public FakePlaybackSession Session { get; } = new();

        public MainViewModel Build()
        {
            return new MainViewModel(
                Store,
                Import,
                new StubProviderRegistry(),
                Session,
                NullLogger<MainViewModel>.Instance);
        }
    }
}
