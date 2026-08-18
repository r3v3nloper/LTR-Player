using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Moving the timeline along the channel axis, which used to be a cap of two hundred and nothing further.
/// </summary>
/// <remarks>
/// Paged rather than scrolled, the same way the time window is moved, so these are commands with guards and
/// that is what the tests assert. Every case here needs more channels than one page holds, which is why the
/// numbers below are what they are: 250 covered channels make two pages, the second of them short.
/// </remarks>
public sealed class GuidePagingTests
{
    private const int Covered = 250;

    [Fact]
    public async Task Opening_ShowsTheFirstPageAndSaysWhereItSits()
    {
        // Arrange & Act
        var viewModel = await OpenGuideOverCoveredChannelsAsync();

        // Assert
        viewModel.Guide.Rows.Count.ShouldBe(GuideViewModel.RowsPerPage);
        viewModel.Guide.Rows[0].Name.ShouldBe("Channel 1");
        viewModel.Guide.Notice.ShouldBe($"Channels 1–200 of {Covered} with guide data.");
    }

    [Fact]
    public async Task ShowLaterChannels_DrawsTheNextPage()
    {
        // Arrange
        var viewModel = await OpenGuideOverCoveredChannelsAsync();

        // Act
        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Rows.Count.ShouldBe(Covered - GuideViewModel.RowsPerPage);
        viewModel.Guide.Rows[0].Name.ShouldBe("Channel 201");
        viewModel.Guide.Notice.ShouldBe($"Channels 201–{Covered} of {Covered} with guide data.");
    }

    [Fact]
    public async Task ShowEarlierChannels_ComesBack()
    {
        // Arrange
        var viewModel = await OpenGuideOverCoveredChannelsAsync();
        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);

        // Act
        await viewModel.Guide.ShowEarlierChannelsCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.Rows[0].Name.ShouldBe("Channel 1");
        viewModel.Guide.RowOffset.ShouldBe(0);
    }

    [Fact]
    public async Task AtTheEnds_TheCommandsRefuse()
    {
        // Arrange: the guards are how the viewer sees there is nothing further, since there is no scrollbar
        // to run out of.
        var viewModel = await OpenGuideOverCoveredChannelsAsync();

        // Act & Assert
        viewModel.Guide.ShowEarlierChannelsCommand.CanExecute(null).ShouldBeFalse("this is the first page");
        viewModel.Guide.ShowLaterChannelsCommand.CanExecute(null).ShouldBeTrue();

        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);

        viewModel.Guide.ShowEarlierChannelsCommand.CanExecute(null).ShouldBeTrue();
        viewModel.Guide.ShowLaterChannelsCommand.CanExecute(null).ShouldBeFalse("this is the last page");
    }

    [Fact]
    public async Task WithEverythingCoveredOnOnePage_ItSaysNothingAndRefusesBoth()
    {
        // Arrange: the common case — a filtered list, or a subscription the guide barely covers.
        var viewModel = await OpenGuideOverCoveredChannelsAsync(covered: 3);

        // Assert
        viewModel.Guide.Rows.Count.ShouldBe(3);
        viewModel.Guide.Notice.ShouldBeEmpty("there is nowhere else to go, so there is nothing to say");
        viewModel.Guide.ShowLaterChannelsCommand.CanExecute(null).ShouldBeFalse();
        viewModel.Guide.ShowEarlierChannelsCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task MovingTheTimeWindow_StaysOnTheSamePageOfChannels()
    {
        // Arrange: the two axes are independent. Moving time to look at the evening must not send the viewer
        // back to the first two hundred channels.
        var viewModel = await OpenGuideOverCoveredChannelsAsync();
        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);

        // Act
        await viewModel.Guide.MoveLaterCommand.ExecuteAsync(null);

        // Assert
        viewModel.Guide.RowOffset.ShouldBe(GuideViewModel.RowsPerPage);
        viewModel.Guide.Rows[0].Name.ShouldBe("Channel 201");
    }

    [Fact]
    public async Task WhenTheChannelSetChanges_TheGuideStartsFromTheFirstPage()
    {
        // Arrange: page two of a filtered list is not page two of the same list unfiltered, so a new set of
        // channels has to start over rather than opening somewhere arbitrary.
        var viewModel = await OpenGuideOverCoveredChannelsAsync();
        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);
        viewModel.Guide.RowOffset.ShouldBe(GuideViewModel.RowsPerPage);

        // Act: handed a new selection, as the shell does whenever the channel list changes. Asserted without
        // a reload on purpose — going through one would also clamp the offset, and then this would pass
        // whether or not attaching resets it.
        viewModel.Guide.Attach(viewModel.SourceManagement.SelectedSource, viewModel.Channels.VisibleChannels);

        // Assert
        viewModel.Guide.RowOffset.ShouldBe(0);
    }

    [Fact]
    public async Task WhenThePageOutlivesTheChannelsItIndexedInto_ItComesBackIntoRange()
    {
        // Arrange: a reload can find fewer covered channels than the page it is on — a refresh removed some,
        // or a filter narrowed them — and an offset past the end would draw an empty timeline over a guide
        // that has data.
        var context = Arrange(Covered);
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);
        await viewModel.Guide.ShowLaterChannelsCommand.ExecuteAsync(null);

        // Act: all but three channels lose their guide link, then the timeline reloads where it stands.
        var kept = context.Store.GuideLinks.Take(3).ToList();
        context.Store.GuideLinks.Clear();

        foreach (var (channelId, guideChannelId) in kept)
        {
            context.Store.GuideLinks[channelId] = guideChannelId;
        }

        await viewModel.Guide.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Guide.RowOffset.ShouldBe(0);
        viewModel.Guide.Rows.Count.ShouldBe(3, "and the rows that are left are drawn");
    }

    /// <summary>
    /// Opens the timeline over a source whose channels the guide all covers.
    /// </summary>
    private static async Task<MainViewModel> OpenGuideOverCoveredChannelsAsync(int covered = Covered)
    {
        var context = Arrange(covered);
        var viewModel = context.Build();

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.ToggleGuideCommand.ExecuteAsync(null);

        return viewModel;
    }

    /// <summary>
    /// A source with <paramref name="covered"/> channels, each linked to a guide channel with one programme
    /// running now — so every one of them is a row the timeline would draw.
    /// </summary>
    private static MainViewModelHarness Arrange(int covered)
    {
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        for (var number = 1; number <= covered; number++)
        {
            var channelId = number;
            var guideChannelId = 1_000 + number;

            context.Store.Channels.Add(new Channel
            {
                Id = channelId,
                SourceId = 1,
                ExternalId = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"Channel {number}",
                SortOrder = number,
            });

            context.Store.GuideLinks[channelId] = guideChannelId;

            context.Store.Programmes.Add(new EpgEntry
            {
                GuideChannelId = guideChannelId,
                Title = $"On {number}",
                StartUtc = MainViewModelHarness.Now.AddMinutes(-30),
                StopUtc = MainViewModelHarness.Now.AddMinutes(30),
            });
        }

        return context;
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSourceBuilder().WithId(1).WithCredentials("alice", "s3cret").Build();
    }
}
