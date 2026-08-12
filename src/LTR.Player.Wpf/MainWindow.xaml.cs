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
    /// Pushes the entered password into the view model.
    /// </summary>
    /// <remarks>
    /// <see cref="PasswordBox.Password"/> is deliberately not a dependency property, so it cannot be
    /// bound. Forwarding it here is the standard workaround and keeps the view model free of any
    /// reference to a control.
    /// </remarks>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = PasswordInput.Password;
    }

    private void OnChannelActivated(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.PlaySelectedCommand.CanExecute(null))
        {
            _viewModel.PlaySelectedCommand.Execute(null);
        }
    }
}
