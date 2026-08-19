using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// The on-screen controls, drawn over the video.
/// </summary>
/// <remarks>
/// <para>
/// Holds the glue that XAML cannot express, and no decisions. Most of it exists because of the same thing:
/// WPF reports a drag as a pair of events and gives a view model no way to observe them, and the pointer
/// being alive — or resting on the controls — is not something a binding can state.
/// </para>
/// <para>
/// Following the pointer over the picture is <see cref="PicturePointer"/>'s, because it is a subject of its own
/// and the largest one here: which window to watch, and what a double-click was aimed at. What is left is the
/// cursor's timing, the two handlers the markup wires for the pointer resting on the bar, and the seek bar's
/// drag — three small things that only make sense against this control.
/// </para>
/// </remarks>
public partial class PlayerOverlayView : UserControl
{
    private readonly PicturePointer _pointer;

    /// <summary>
    /// The view model being watched for the one thing the cursor cannot be told by a binding alone.
    /// </summary>
    private PlayerOverlayViewModel? _watched;

    public PlayerOverlayView()
    {
        InitializeComponent();

        // After InitializeComponent, which is what creates the surface it compares a press against.
        _pointer = new PicturePointer(
            PictureSurface,
            () => Overlay?.Reveal(),
            () => Overlay?.ToggleFullscreenCommand.Execute(parameter: null));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// The overlay's own view model, taken from the element that narrows the data context to it.
    /// </summary>
    /// <remarks>
    /// Read from the element rather than cast from this control's own data context, which is the shell's.
    /// Reaching through the shell to get here would tie this view to a type it otherwise never mentions.
    /// </remarks>
    private PlayerOverlayViewModel? Overlay => OverlayRoot.DataContext as PlayerOverlayViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The hosting window is VideoView's to create, so it is only knowable once this is in the tree.
        _pointer.AttachTo(Window.GetWindow(this));
        WatchForCursorChanges(Overlay);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pointer.AttachTo(null);
        WatchForCursorChanges(null);
    }

    /// <summary>
    /// Keeps the cursor's disappearance in step with the controls'.
    /// </summary>
    /// <remarks>
    /// Which cursor the picture wears is stated in the markup and needs nothing here. What does is the
    /// timing: WPF settles the cursor when the pointer moves, and the moment this has to take effect —
    /// four seconds of nothing happening — is by definition a moment when it has not. Posted rather than
    /// called straight, so the bindings the markup's rule reads have already been through.
    /// </remarks>
    private void WatchForCursorChanges(PlayerOverlayViewModel? overlay)
    {
        if (ReferenceEquals(overlay, _watched))
        {
            return;
        }

        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnOverlayChanged;
        }

        _watched = overlay;

        if (_watched is null)
        {
            return;
        }

        _watched.PropertyChanged += OnOverlayChanged;
    }

    /// <remarks>
    /// An empty or null property name means every property, which is the rule everywhere in this
    /// application that a change is answered by name.
    /// </remarks>
    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        var affectsTheCursor = e.PropertyName
            is null
            or ""
            or nameof(PlayerOverlayViewModel.IsVisible)
            or nameof(PlayerOverlayViewModel.IsFullscreen);

        if (!affectsTheCursor)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, () => Mouse.UpdateCursor());
    }

    /// <summary>
    /// The pointer has come to rest on the controls, which is what stops them being taken away.
    /// </summary>
    /// <remarks>
    /// A pointer that is not moving raises nothing, so without this the bar hides from under a hand that is
    /// on its way to a button — and the click that follows lands on the picture instead.
    /// </remarks>
    private void OnPointerEnteredControls(object sender, MouseEventArgs e)
    {
        if (Overlay is not { } overlay)
        {
            return;
        }

        overlay.IsPointerOnControls = true;
        overlay.Reveal();
    }

    private void OnPointerLeftControls(object sender, MouseEventArgs e)
    {
        if (Overlay is not { } overlay)
        {
            return;
        }

        overlay.IsPointerOnControls = false;
        overlay.Reveal();
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
