using LTR.Catalogue;
using LTR.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Player.Wpf;

/// <summary>
/// Assembles the composed view model over fakes, so each test states only what it cares about.
/// </summary>
/// <remarks>
/// Shared by every test class here rather than repeated in each. The composed view model takes eight
/// constructor arguments and gained two of them in a single session; a copy per test class meant the same
/// edit twice, and the second copy is the one that gets forgotten.
/// </remarks>
internal sealed class MainViewModelHarness
{
    /// <summary>
    /// The moment the fake clock stands at. Fixed so a test can place programmes around a known instant
    /// rather than around whenever it happens to run.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    public FakeCatalogueStore Store { get; } = new();

    public FakeSourceImportService Import { get; } = new();

    public FakePlaybackSession Session { get; } = new();

    public FakeGuideImportService GuideImport { get; } = new();

    public TestClock Clock { get; } = new(Now);

    public FakeVodDetailService VodDetail { get; } = new();

    /// <summary>
    /// The progress recorder the last built view model was given, so a test can prove a position was
    /// followed rather than only that a stream was opened.
    /// </summary>
    public WatchProgressRecorder? Progress { get; private set; }

    public MainViewModel Build()
    {
        // One status line for all of them, exactly as the container hands it out.
        var status = new StatusLine();

        Progress = new WatchProgressRecorder(Store, NullLogger<WatchProgressRecorder>.Instance);

        return new MainViewModel(
            new SourceManagementViewModel(Store, Import, status, NullLogger<SourceManagementViewModel>.Instance),
            new ChannelListViewModel(Store, Clock, status, NullLogger<ChannelListViewModel>.Instance),
            new GuideViewModel(Store, Clock),
            new MovieListViewModel(Store, VodDetail, NullLogger<MovieListViewModel>.Instance),
            new SeriesCatalogueViewModel(Store, VodDetail, NullLogger<SeriesCatalogueViewModel>.Instance),
            new ContinueWatchingViewModel(Store),
            status,
            new StubProviderRegistry(),
            Session,
            new GuideImportCoordinator(GuideImport, status, NullLogger<GuideImportCoordinator>.Instance),
            Progress,
            NullLogger<MainViewModel>.Instance);
    }
}
