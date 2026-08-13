using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers the guide's place in the shell: what reaches the channel rows, what the timeline draws, and the
/// command guards around the import.
/// </summary>
/// <remarks>
/// Exercised through the composed <see cref="MainViewModel"/> for the same reason the other view model
/// tests are: the guide is spread across three objects that deliberately do not know each other, and only
/// the composition has the coordination between them.
/// </remarks>
public sealed class GuideViewModelTests
{

    [Fact]
    public async Task LoadingACatalogue_PutsWhatIsOnNowOnTheRows()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        var row = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();
        row.NowTitle.ShouldBe("Running");
        row.NextTitle.ShouldBe("then Next");
        row.NowProgress.ShouldBe(0.5, tolerance: 0.01);
        viewModel.Channels.HasGuide.ShouldBeTrue();
    }

    /// <summary>
    /// Most channels of a real subscription have no guide entry. Their rows have to say nothing rather than
    /// keep whatever the previous source showed.
    /// </summary>
    [Fact]
    public async Task AChannelWithNoGuide_ShowsNothing()
    {
        // Arrange
        var context = Arrange();
        context.Store.Channels.Add(new Channel
        {
            Id = 20,
            SourceId = 1,
            ExternalId = "202",
            Name = "Unmatched",
        });

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        var row = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()
            .Single(item => item.Name == "Unmatched");

        row.NowTitle.ShouldBeEmpty();
        row.NextTitle.ShouldBeEmpty();
    }

    [Fact]
    public async Task ToggleGuide_DrawsTheChannelsTheListIsShowing()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.IsVisible.ShouldBeTrue();
        var row = viewModel.Guide.Rows.ShouldHaveSingleItem();
        row.Name.ShouldBe("Erste");
        row.Programmes.Select(programme => programme.Title).ShouldContain("Running");
    }

    /// <summary>
    /// The timeline showing something the channel list has filtered out would make the two disagree about
    /// what the user is looking at.
    /// </summary>
    [Fact]
    public async Task ToggleGuide_RespectsTheChannelFilter()
    {
        // Arrange
        var context = Arrange();
        context.Store.Channels.Add(new Channel
        {
            Id = 20,
            SourceId = 1,
            ExternalId = "202",
            Name = "Zweite",
        });

        context.Store.GuideLinks[20] = 100;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Channels.ChannelFilterText = "zweite";

        // Act
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Rows.ShouldHaveSingleItem().Name.ShouldBe("Zweite");
    }

    [Fact]
    public async Task ToggleGuide_CalledTwice_ClosesItAgain()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.IsVisible.ShouldBeFalse();
    }

    /// <summary>
    /// A window opened on the current moment must contain it, or the marker line and the running programme
    /// are both off screen.
    /// </summary>
    [Fact]
    public async Task ToggleGuide_OpensAWindowContainingTheCurrentMoment()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Timeline.StartUtc.ShouldBeLessThanOrEqualTo(MainViewModelHarness.Now);
        viewModel.Guide.Timeline.EndUtc.ShouldBeGreaterThan(MainViewModelHarness.Now);
        viewModel.Guide.IsNowVisible.ShouldBeTrue("the marker has to be inside the window it just opened");
    }

    [Fact]
    public async Task MoveLater_ThenMoveEarlier_ReturnsToTheSameWindow()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        var start = viewModel.Guide.Timeline.StartUtc;

        // Act
        await viewModel.Guide.MoveLaterCommand.ExecuteAsync(null);
        await viewModel.Guide.MoveEarlierCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Timeline.StartUtc.ShouldBe(start);
    }

    [Fact]
    public async Task SelectProgramme_ShowsItsDetail()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        var programme = viewModel.Guide.Rows[0].Programmes.First(item => item.Title == "Running");

        // Act
        viewModel.Guide.SelectProgrammeCommand.Execute(programme);

        // Assert
        viewModel.Guide.SelectedProgramme.ShouldBe(programme);
        viewModel.Guide.SelectedProgramme!.Description.ShouldBe("A description.");
    }

    /// <summary>
    /// A guide that lists nothing for the channels on screen has to explain itself. Silence looks like a
    /// broken timeline.
    /// </summary>
    [Fact]
    public async Task ToggleGuide_WithNoGuideData_SaysWhyItIsEmpty()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(new Channel { Id = 10, SourceId = 1, ExternalId = "101", Name = "Erste" });

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Rows.ShouldBeEmpty();
        viewModel.Guide.Notice.ShouldContain("guide data");
    }

    /// <summary>
    /// The defect found against a real 17,000-channel subscription: the channel list showed programmes and
    /// the timeline reported that no channel had guide data.
    /// </summary>
    /// <remarks>
    /// The cause was the timeline reading <c>Channel.GuideChannelId</c> from the entities it was handed.
    /// Those are loaded when the catalogue is shown, which is always before an import finishes, so the link
    /// is written to the database and not to them. Now-and-next was unaffected because the store answers
    /// that query itself, which is exactly why the two disagreed.
    /// </remarks>
    [Fact]
    public async Task ToggleGuide_AfterAnImportThatFollowedTheCatalogueLoad_StillFindsTheChannels()
    {
        // Arrange: a catalogue on screen with no guide yet, which is the state every first run is in.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(new Channel { Id = 10, SourceId = 1, ExternalId = "101", Name = "Erste" });

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act: the import lands. It writes the link and the programmes into the store, and touches nothing
        // the view layer is holding.
        context.Store.GuideLinks[10] = 100;
        context.Store.Programmes.Add(Programme(100, "Running", MainViewModelHarness.Now.AddMinutes(-30)));

        viewModel.ImportGuideCommand.Execute(null);
        await viewModel.GuideImportCompletion;

        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Notice.ShouldBeEmpty("every listed channel has guide data");
        viewModel.Guide.Rows.ShouldHaveSingleItem()
            .Programmes.Select(programme => programme.Title)
            .ShouldContain("Running");
    }

    [Fact]
    public async Task ImportGuide_FetchesUnconditionally()
    {
        // Arrange: the button means "now", unlike the automatic path.
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ImportGuideCommand.Execute(null);
        await viewModel.GuideImportCompletion;

        // Assert
        context.GuideImport.Imported.ShouldHaveSingleItem();
        context.GuideImport.ImportedIfStale.ShouldBeEmpty();
    }

    /// <summary>
    /// The guard reads the selected source, which belongs to source management — the boundary
    /// <c>[NotifyCanExecuteChangedFor]</c> cannot cross, and the defect class this project exists for.
    /// </summary>
    [Fact]
    public async Task ImportGuideCommand_AnnouncesThatItsGuardChanged_WhenASourceIsSelected()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();

        var announcements = 0;
        viewModel.ImportGuideCommand.CanExecuteChanged += (_, _) => announcements++;

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        announcements.ShouldBeGreaterThan(0);
        viewModel.ImportGuideCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ImportGuideCommand_IsDisabledWhileAnImportIsRunning()
    {
        // Arrange: an import runs for minutes, and starting a second one alongside it would fetch the same
        // guide twice.
        var context = Arrange();
        context.GuideImport.BlockUntilReleased = true;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ImportGuideCommand.Execute(null);

        // Assert
        viewModel.IsImportingGuide.ShouldBeTrue();
        viewModel.ImportGuideCommand.CanExecute(null).ShouldBeFalse();

        context.GuideImport.Release();
        await viewModel.GuideImportCompletion;
    }

    [Fact]
    public async Task AfterAnImport_TheRowsShowTheNewGuide()
    {
        // Arrange: the guide arrives after the channel list is already on screen, so something has to put
        // it there.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(new Channel
        {
            Id = 10,
            SourceId = 1,
            ExternalId = "101",
            Name = "Erste",
        });

        context.Store.GuideLinks[10] = 100;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var row = viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>().First();
        row.NowTitle.ShouldBeEmpty("nothing has been imported yet");

        // Act: the import lands, and only then does the store have programmes in it.
        context.Store.Programmes.Add(Programme(100, "Running", MainViewModelHarness.Now.AddMinutes(-30)));
        viewModel.ImportGuideCommand.Execute(null);
        await viewModel.GuideImportCompletion;

        // Assert
        row.NowTitle.ShouldBe("Running");
    }

    [Fact]
    public async Task AfterAnImportThatMatchedNothing_SaysSoRatherThanClaimingSuccess()
    {
        // Arrange: a guide can read perfectly and match no channel, and that is the case the user needs
        // told — every count would otherwise look healthy.
        var context = Arrange();
        context.GuideImport.Result = new GuideImportResult(
            GuideImportOutcome.Imported,
            ProgrammeCount: 5_000,
            MatchedChannelCount: 0,
            WasTruncated: false,
            Summary: null);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ImportGuideCommand.Execute(null);
        await viewModel.GuideImportCompletion;

        // Assert
        viewModel.Status.Text.ShouldContain("matched none");
    }

    /// <summary>
    /// The stages the import reports have to reach the status line, or a download of tens of megabytes looks
    /// like nothing happening.
    /// </summary>
    [Fact]
    public async Task ImportGuide_ReportsItsProgressInTheStatusLine()
    {
        // Arrange
        var context = Arrange();
        context.GuideImport.ReportProgress = true;
        context.GuideImport.BlockUntilReleased = true;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var reported = new List<string>();
        viewModel.Status.PropertyChanged += (_, _) => reported.Add(viewModel.Status.Text);

        // Act
        viewModel.ImportGuideCommand.Execute(null);
        context.GuideImport.Release();
        await viewModel.GuideImportCompletion;

        // Assert
        reported.ShouldContain(text => text.Contains("Reading the programme guide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportGuide_WhenTheSourceHasNoGuide_SaysSo()
    {
        // Arrange
        var context = Arrange();
        context.GuideImport.Result = GuideImportResult.NoGuideAvailable;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ImportGuideCommand.Execute(null);
        await viewModel.GuideImportCompletion;

        // Assert
        viewModel.Status.Text.ShouldContain("no programme guide");
    }

    /// <summary>
    /// Adding a subscription should bring its guide with it, without the Connect button waiting for a
    /// download of tens of megabytes.
    /// </summary>
    [Fact]
    public async Task Connect_StartsAStaleGuideImportInTheBackground()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        viewModel.SourceManagement.PanelUrl = "http://panel.example:8080";
        viewModel.SourceManagement.Username = "alice";
        viewModel.SourceManagement.Password = "s3cret";

        // Act
        await viewModel.SourceManagement.ConnectCommand.ExecuteAsync(null);
        await viewModel.GuideImportCompletion;

        // Assert: the stale-checking entry point, not the unconditional one.
        context.GuideImport.ImportedIfStale.ShouldHaveSingleItem();
        context.GuideImport.Imported.ShouldBeEmpty();
    }

    /// <summary>
    /// Merely switching between configured sources is not an invitation to download a guide, which is why
    /// the coordinator has a separate method for "the catalogue was just imported".
    /// </summary>
    [Fact]
    public async Task SelectingASource_DoesNotFetchAGuide()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        context.GuideImport.Imported.ShouldBeEmpty();
        context.GuideImport.ImportedIfStale.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_StartsAStaleGuideImportInTheBackground()
    {
        // Arrange
        var context = Arrange();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        await viewModel.SourceManagement.RefreshCommand.ExecuteAsync(null);
        await viewModel.GuideImportCompletion;

        // Assert
        context.GuideImport.ImportedIfStale.ShouldHaveSingleItem();
    }

    /// <summary>
    /// An import writes to the database throughout. Left running past shutdown it would write into a
    /// disposed container, and the process would not exit while it did.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CancelsAnImportStillRunning()
    {
        // Arrange
        var context = Arrange();
        context.GuideImport.BlockUntilReleased = true;

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ImportGuideCommand.Execute(null);

        // Act
        await viewModel.DisposeAsync();

        // Assert: it returned without the import ever being released.
        viewModel.IsImportingGuide.ShouldBeFalse();
    }

    private static MainViewModelHarness Arrange()
    {
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        context.Store.Channels.Add(new Channel
        {
            Id = 10,
            SourceId = 1,
            ExternalId = "101",
            Name = "Erste",
        });

        context.Store.GuideLinks[10] = 100;

        // Half an hour in, so the progress assertion has a value it can state exactly.
        context.Store.Programmes.Add(Programme(100, "Running", MainViewModelHarness.Now.AddMinutes(-30)));
        context.Store.Programmes.Add(Programme(100, "Next", MainViewModelHarness.Now.AddMinutes(30)));

        return context;
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Test source",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        };
    }

    private static EpgEntry Programme(int guideChannelId, string title, DateTimeOffset startUtc)
    {
        return new EpgEntry
        {
            GuideChannelId = guideChannelId,
            Title = title,
            Description = "A description.",
            StartUtc = startUtc,
            StopUtc = startUtc.AddHours(1),
        };
    }
}
