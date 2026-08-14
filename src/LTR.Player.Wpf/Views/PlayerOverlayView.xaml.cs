using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The on-screen controls, drawn over the video.
/// </summary>
/// <remarks>
/// Holds the glue that XAML cannot express, and no decisions. Both handlers here exist because of the same
/// thing: WPF reports a drag as a pair of events and gives a view model no way to observe them, and the
/// pointer being alive is not something a binding can state.
/// </remarks>
public partial class PlayerOverlayView : UserControl
{
    public PlayerOverlayView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The overlay's own view model, taken from the element that narrows the data context to it.
    /// </summary>
    /// <remarks>
    /// Read from the element rather than cast from this control's own data context, which is the shell's.
    /// Reaching through the shell to get here would tie this view to a type it otherwise never mentions.
    /// </remarks>
    private PlayerOverlayViewModel? Overlay => OverlayRoot.DataContext as PlayerOverlayViewModel;

    private void OnPointerActivity(object sender, MouseEventArgs e)
    {
        Overlay?.Reveal();
    }

    /// <remarks>
    /// Buttons and pickers mark their own clicks handled, so this only ever sees a double-click on the
    /// picture itself rather than one aimed at a control.
    /// </remarks>
    private void OnPictureDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        Overlay?.ToggleFullscreenCommand.Execute(parameter: null);
    }

    /// <summary>
    /// The seek bar has been taken hold of, so the timer must stop writing the position into it.
    /// </summary>
    private void OnSeekStarted(object sender, DragStartedEventArgs e)
    {
        Overlay?.BeginScrub();
    }

    private void OnSeekCompleted(object sender, DragCompletedEventArgs e)
    {
        Overlay?.EndScrub();
    }
}
