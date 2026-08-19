using System.Windows.Controls;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// What the viewer is part-way through, films and episodes together.
/// </summary>
public partial class ContinueWatchingView : UserControl
{
    public ContinueWatchingView()
    {
        InitializeComponent();
    }

    private void OnEntryActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && viewModel.ContinueWatching.SelectedEntry is { } entry
            && viewModel.PlaybackCommands.ResumeEntryCommand.CanExecute(entry))
        {
            viewModel.PlaybackCommands.ResumeEntryCommand.Execute(entry);
        }
    }
}
