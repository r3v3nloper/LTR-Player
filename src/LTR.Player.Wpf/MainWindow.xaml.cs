using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    /// <summary>
    /// How often the same sampling runs while the on-screen controls are visible.
    /// </summary>
    /// <remarks>
    /// The same timer at a second rate rather than a timer of its own: both jobs read the same figures from
    /// the same place, and two timers doing that would sample twice as often for nothing. Half a second is
    /// what makes a seek bar look like it is moving rather than stepping.
    /// </remarks>
    private static readonly TimeSpan OverlaySampleInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The narrowest the side panel may be dragged, restored after fullscreen has set it aside.
    /// </summary>
    /// <remarks>
    /// Stated here rather than only in the markup because fullscreen has to put it back, and a second
    /// literal in the code-behind is how the two would come to disagree.
    /// </remarks>
    private const double SidePanelMinimumWidth = 300;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _guideRefreshTimer;
    private readonly DispatcherTimer _positionSampleTimer;

    /// <summary>
    /// The side panel's width before fullscreen took it away, so leaving fullscreen gives back the width
    /// the viewer had chosen with the splitter rather than the one this file was written with.
    /// </summary>
    private GridLength _sidePanelWidth;

    private WindowState _stateBeforeFullscreen = WindowState.Normal;
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

        _viewModel.PlayerOverlay.PropertyChanged += OnOverlayPropertyChanged;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _sidePanelWidth = SidePanelColumn.Width;
        _guideRefreshTimer.Start();
        _positionSampleTimer.Start();

        await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async void OnGuideRefreshTick(object? sender, EventArgs e)
    {
        await _viewModel.RefreshGuideDisplayAsync().ConfigureAwait(true);
    }

    private async void OnPositionSampleTick(object? sender, EventArgs e)
    {
        await _viewModel.SamplePlaybackAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Turns a keystroke into a player action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A preview handler rather than input bindings in the markup, because the shortcuts are single
    /// unmodified keys and several of them are letters. An input binding is offered the key before the
    /// focused element sees it, so declaring one for <c>A</c> would mean the channel search box could never
    /// contain the letter — hence the check for what has focus, which is the whole reason this cannot be
    /// declarative.
    /// </para>
    /// <para>
    /// Which key means what is not decided here. That is <see cref="PlayerKeyMap"/>, and what each action
    /// does is the view model's, so this handler holds neither half.
    /// </para>
    /// </remarks>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTyping())
        {
            return;
        }

        if (PlayerKeyMap.Resolve(e.Key, Keyboard.Modifiers) is not { } action)
        {
            return;
        }

        // Marked handled so a mapped key does not also reach the list underneath — Page Down zapping and
        // the list paging at the same time would land two channels away from either.
        e.Handled = true;

        await _viewModel.PerformAsync(action, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Whether the keystroke belongs to something the viewer is typing into.
    /// </summary>
    /// <remarks>
    /// The search boxes and the credential fields all take letters, and every letter this window answers to
    /// is a letter someone might type. Checked by focus rather than by listing the boxes, so a field added
    /// later is covered without anyone remembering to come back here.
    /// </remarks>
    private static bool IsTyping()
    {
        return Keyboard.FocusedElement is TextBoxBase or PasswordBox;
    }

    private void OnPointerActivity(object sender, MouseEventArgs e)
    {
        _viewModel.PlayerOverlay.Reveal();
    }

    /// <remarks>
    /// The controls' visibility decides how often playback is sampled, and fullscreen is applied to the
    /// window itself. Both are view concerns the view model only states.
    /// </remarks>
    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "" or nameof(PlayerOverlayViewModel.IsVisible):
                _positionSampleTimer.Interval = _viewModel.PlayerOverlay.IsVisible
                    ? OverlaySampleInterval
                    : PositionSampleInterval;
                break;

            case nameof(PlayerOverlayViewModel.IsFullscreen):
                ApplyFullscreen(_viewModel.PlayerOverlay.IsFullscreen);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Gives the picture the whole screen, and gives it back.
    /// </summary>
    /// <remarks>
    /// The trip through <see cref="WindowState.Normal"/> is required, not defensive: a window that is
    /// already maximised does not re-maximise when its chrome is removed, and stays sitting above the
    /// taskbar with a strip of desktop showing.
    /// </remarks>
    private void ApplyFullscreen(bool isFullscreen)
    {
        if (isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            _sidePanelWidth = SidePanelColumn.Width;

            SidePanelColumn.MinWidth = 0;
            SidePanelColumn.Width = new GridLength(0);

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;

            return;
        }

        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        WindowState = _stateBeforeFullscreen;

        SidePanelColumn.MinWidth = SidePanelMinimumWidth;
        SidePanelColumn.Width = _sidePanelWidth;
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
        _viewModel.PlayerOverlay.PropertyChanged -= OnOverlayPropertyChanged;

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
