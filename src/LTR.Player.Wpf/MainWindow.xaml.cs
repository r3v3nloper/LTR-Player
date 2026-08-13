using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LTR.Playback.LibVlc;

namespace LTR.Player.Wpf;

/// <summary>
/// Shell window. Holds only the imperative glue that XAML cannot express.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
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

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
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

    /// <summary>
    /// Pushes the entered password into the view model.
    /// </summary>
    /// <remarks>
    /// <see cref="PasswordBox.Password"/> is deliberately not a dependency property, so it cannot be
    /// bound. Forwarding it here is the standard workaround and keeps the view model free of any
    /// reference to a control.
    /// </remarks>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.SourceManagement.Password = PasswordInput.Password;
    }

    private void OnChannelActivated(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.PlaySelectedCommand.CanExecute(null))
        {
            _viewModel.PlaySelectedCommand.Execute(null);
        }
    }
}
