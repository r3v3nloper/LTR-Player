using LTR.Catalogue;
using LTR.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Player.Wpf;

/// <summary>
/// Assembles the composed view model over fakes, so each test states only what it cares about.
/// </summary>
/// <remarks>
/// Shared by every test class here rather than repeated in each. The composed view model takes twelve
/// constructor arguments and has gained two in a single session more than once; a copy per test class meant
/// the same edit twice, and the second copy is the one that gets forgotten.
/// </remarks>
internal sealed class MainViewModelHarness
{
    private readonly FakePlayerSettingsStore _settingsStore;

    public MainViewModelHarness()
    {
        _settingsStore = new FakePlayerSettingsStore(Settings);
    }

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

    public FakeStreamFailureExplainer Failures { get; } = new();

    /// <summary>
    /// The progress recorder the last built view model was given, so a test can prove a position was
    /// followed rather than only that a stream was opened.
    /// </summary>
    public WatchProgressRecorder? Progress { get; private set; }

    /// <summary>
    /// The settings the built view model shares, so a test can seed a remembered volume or read one back.
    /// </summary>
    /// <remarks>
    /// One instance, as the container hands out: the overlay writes the viewer's volume into it and the
    /// settings pane writes the tuning, and a second copy would hide either from the other.
    /// </remarks>
    public PlayerSettings Settings { get; } = new();

    /// <summary>What the shell saved, or null when it has not. Set by the way out of the window.</summary>
    public PlayerSettings? SavedSettings => _settingsStore.Saved;

    /// <remarks>
    /// <see cref="Store"/> appears several times over in the call below, because the store's five faces are
    /// one object here exactly as they are one object in the container. The repetition is the point: it shows
    /// which parts of the catalogue each view model actually reaches for.
    /// </remarks>
    public MainViewModel Build()
    {
        // One status line for all of them, exactly as the container hands it out.
        var status = new StatusLine();

        Progress = new WatchProgressRecorder(Store, NullLogger<WatchProgressRecorder>.Instance);

        return new MainViewModel(
            new SourceManagementViewModel(Store, Import, status, NullLogger<SourceManagementViewModel>.Instance),
            new ChannelListViewModel(Store, Store, Store, Clock, status, NullLogger<ChannelListViewModel>.Instance),
            new GuideViewModel(Store, Clock),
            new MovieListViewModel(Store, Store, VodDetail, NullLogger<MovieListViewModel>.Instance),
            new SeriesCatalogueViewModel(
                Store,
                Store,
                VodDetail,
                NullLogger<SeriesCatalogueViewModel>.Instance),
            new ContinueWatchingViewModel(Store, Store),
            status,
            new PlaybackCoordinator(
                new StubProviderRegistry(),
                Session,
                Session,
                Progress,
                Failures,
                status,
                NullLogger<PlaybackCoordinator>.Instance),
            new PlayerOverlayViewModel(Session, Settings, Clock),
            new SettingsViewModel(
                _settingsStore,
                Settings,
                Store,
                status,
                NullLogger<SettingsViewModel>.Instance),
            new GuideImportCoordinator(GuideImport, status, NullLogger<GuideImportCoordinator>.Instance),
            NullLogger<MainViewModel>.Instance);
    }
}
