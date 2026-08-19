using System.Windows.Controls;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The series catalogue: a search, the results, and the seasons and episodes of the open series.
/// </summary>
public partial class SeriesCatalogueView : UserControl
{
    public SeriesCatalogueView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Double-clicking an episode plays it.
    /// </summary>
    /// <remarks>
    /// In code because a double click is not a command source. The row also carries a play button, which
    /// needs no selection because it passes the episode as its parameter — but a list that answers only to
    /// a button at its right edge is a list most people will conclude is broken.
    /// </remarks>
    private void OnEpisodeActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || viewModel.SeriesCatalogue.SelectedEpisode is not { } episode)
        {
            return;
        }

        if (viewModel.PlaybackCommands.PlayEpisodeCommand.CanExecute(episode))
        {
            viewModel.PlaybackCommands.PlayEpisodeCommand.Execute(episode);
        }
    }
}
