using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LTR.Playback.LibVlc;

namespace LTR.Player.Wpf;

/// <summary>
/// Shell window. Holds only the imperative glue that XAML cannot express.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// How often the guide display is brought up to date.
    /// </summary>
    /// <remarks>
    /// A minute is fine for what this shows: programme boundaries land on the minute, and the progress bar
    /// moving a pixel late is invisible. More often would query the database for thousands of channels for
    /// no visible gain.
    /// </remarks>
    private static readonly TimeSpan GuideRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often playback's position is sampled, so a resume point survives the stream being closed.
    /// </summary>
    /// <remarks>
    /// Far more often than the guide refresh, and for a different reason: this decides how much of a film
    /// the viewer loses if the player is closed between two samples. Five seconds is imperceptible on
    /// resuming and cheap — reading a position from the engine touches nothing but memory.
    /// </remarks>
    private static readonly TimeSpan PositionSampleInterval = TimeSpan.FromSeconds(5);

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _guideRefreshTimer;
    private readonly DispatcherTimer _positionSampleTimer;
    private bool _hasReleasedPlayback;

    public MainWindow(MainViewModel viewModel, IVlcVideoSink videoSink)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(videoSink);

        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;

        // Attaching the player is the one place the engine's implementation is visible, by way of the
        // documented IVlcVideoSink seam: VideoView needs the concrete MediaPlayer to render into.
        Video.MediaPlayer = videoSink.MediaPlayer;

        // The clock belongs to the view rather than the view model: "what time is it" is the one piece of
        // state nothing in the application changes, and a DispatcherTimer is what makes the update land on
        // the thread the bindings live on.
        _guideRefreshTimer = new DispatcherTimer { Interval = GuideRefreshInterval };
        _guideRefreshTimer.Tick += OnGuideRefreshTick;

        _positionSampleTimer = new DispatcherTimer { Interval = PositionSampleInterval };
        _positionSampleTimer.Tick += OnPositionSampleTick;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _guideRefreshTimer.Start();
        _positionSampleTimer.Start();

        await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async void OnGuideRefreshTick(object? sender, EventArgs e)
    {
        await _viewModel.RefreshGuideDisplayAsync().ConfigureAwait(true);
    }

    private void OnPositionSampleTick(object? sender, EventArgs e)
    {
        _viewModel.ObservePlaybackPosition();
    }

    /// <summary>
    /// Releases the stream before the window is allowed to close.
    /// </summary>
    /// <remarks>
    /// The first close attempt is cancelled so the release can run here, on the UI thread, while the
    /// message loop is still pumping. LibVLC's video output owns a child window of this one and needs
    /// that loop to tear itself down; waiting in the application's exit handler instead would block
    /// the very thread the teardown depends on.
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_hasReleasedPlayback)
        {
            base.OnClosing(e);
            return;
        }

        // Stopped before anything else: a tick arriving during teardown would query a database whose
        // container is on its way out.
        _guideRefreshTimer.Stop();
        _positionSampleTimer.Stop();

        e.Cancel = true;
        base.OnClosing(e);

        _ = ReleaseThenCloseAsync();
    }

    private async Task ReleaseThenCloseAsync()
    {
        // Yield first, so this runs after OnClosing has returned. Without it, a release that completes
        // synchronously — the normal case when nothing is playing — calls Close() re-entrantly from
        // inside the very OnClosing that just cancelled the close, and the window never shuts.
        await Task.Yield();

        try
        {
            await _viewModel.ShutdownAsync().ConfigureAwait(true);
        }
        finally
        {
            // Set even if the release failed. A window that refuses to close is a worse outcome than
            // a connection the provider will time out on its own.
            _hasReleasedPlayback = true;
            Close();
        }
    }
}
