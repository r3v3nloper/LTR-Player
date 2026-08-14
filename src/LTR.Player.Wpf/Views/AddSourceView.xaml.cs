using System.Windows;
using System.Windows.Controls;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The form for adding a subscription.
/// </summary>
public partial class AddSourceView : UserControl
{
    public AddSourceView()
    {
        InitializeComponent();
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
        if (DataContext is SourceManagementViewModel viewModel)
        {
            viewModel.Password = PasswordInput.Password;
        }
    }
}
