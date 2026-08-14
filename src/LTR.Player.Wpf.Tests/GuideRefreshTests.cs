using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers when the periodic guide refresh asks the database and when it does not.
/// </summary>
/// <remarks>
/// The query behind it is the largest the player makes — two programmes for every matched channel, once a
/// minute — and it ran whether or not a single channel was matched. A subscription with no guide imported
/// paid for it every minute for as long as the window stayed open, to change nothing.
/// </remarks>
public sealed class GuideRefreshTests
{
    [Fact]
    public async Task RefreshGuideDisplay_WithNothingMatched_DoesNotQuery()
    {
        // Arrange: channels, no guide. The common state for a subscription whose guide was never imported.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(Channel(1, "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var queriesAfterLoad = context.Store.NowAndNextQueries;
        viewModel.Channels.HasGuide.ShouldBeFalse();

        // Act: three timer ticks.
        await viewModel.RefreshGuideDisplayAsync();
        await viewModel.RefreshGuideDisplayAsync();
        await viewModel.RefreshGuideDisplayAsync();

        // Assert
        context.Store.NowAndNextQueries.ShouldBe(queriesAfterLoad, "nothing is matched, so nothing to reread");
    }

    [Fact]
    public async Task RefreshGuideDisplay_WithAGuide_RereadsWhatIsOnNow()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(Channel(1, "Erste"));
        context.Store.GuideLinks[1] = 10;
        context.Store.Programmes.Add(Programme(10, "Running", MainViewModelHarness.Now.AddMinutes(-10)));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Channels.HasGuide.ShouldBeTrue();
        var queriesAfterLoad = context.Store.NowAndNextQueries;

        // Act
        await viewModel.RefreshGuideDisplayAsync();

        // Assert
        context.Store.NowAndNextQueries.ShouldBe(queriesAfterLoad + 1);
    }

    /// <summary>
    /// The skip must not outlive the condition. A guide that has just been imported is exactly when the
    /// channel list has to be reread, and it is reread directly rather than through the timer's path.
    /// </summary>
    [Fact]
    public async Task AGuideArrivingAfterTheSkip_IsStillPickedUp()
    {
        // Arrange: no guide at load, so the timer path is skipping.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Channels.Add(Channel(1, "Erste"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.RefreshGuideDisplayAsync();

        viewModel.Channels.HasGuide.ShouldBeFalse();

        // A guide import lands.
        context.Store.GuideLinks[1] = 10;
        context.Store.Programmes.Add(Programme(10, "Running", MainViewModelHarness.Now.AddMinutes(-10)));

        // Act: what the import's continuation does.
        await viewModel.Channels.RefreshGuideAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Channels.HasGuide.ShouldBeTrue();

        // And the timer resumes rereading, because the condition it skips on has changed.
        var queriesAfterImport = context.Store.NowAndNextQueries;
        await viewModel.RefreshGuideDisplayAsync();
        context.Store.NowAndNextQueries.ShouldBe(queriesAfterImport + 1);
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Source",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
        };
    }

    private static Channel Channel(int id, string name)
    {
        return new Channel
        {
            Id = id,
            SourceId = 1,
            ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
        };
    }

    private static EpgEntry Programme(int guideChannelId, string title, DateTimeOffset startUtc)
    {
        return new EpgEntry
        {
            GuideChannelId = guideChannelId,
            Title = title,
            StartUtc = startUtc,
            StopUtc = startUtc.AddHours(1),
        };
    }
}
