using System.Windows.Controls;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The live channel list, with its category picker, search box and favourites filter.
/// </summary>
public partial class LiveChannelsView : UserControl
{
    public LiveChannelsView()
    {
        InitializeComponent();
    }

    private void OnChannelActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.PlaySelectedCommand.CanExecute(null))
        {
            viewModel.PlaySelectedCommand.Execute(null);
        }
    }
}
