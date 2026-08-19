using System.Windows.Controls;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The film catalogue: a search, the results and the selected film.
/// </summary>
public partial class MovieCatalogueView : UserControl
{
    public MovieCatalogueView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Double-clicking a row plays it, which is what the gesture means everywhere else in the window.
    /// </summary>
    /// <remarks>
    /// In code because a double click is not a command source. Guarded by <c>CanExecute</c> rather than by
    /// re-checking the selection here, so the rule lives in one place.
    /// </remarks>
    private void OnMovieActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.PlaybackCommands.PlayMovieCommand.CanExecute(null))
        {
            viewModel.PlaybackCommands.PlayMovieCommand.Execute(null);
        }
    }
}
