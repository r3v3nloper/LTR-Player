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
/// Holds the glue that XAML cannot express, and no decisions. Most of it exists because of the same thing:
/// WPF reports a drag as a pair of events and gives a view model no way to observe them, and the pointer
/// being alive — or resting on the controls — is not something a binding can state.
/// </remarks>
public partial class PlayerOverlayView : UserControl
{
    /// <summary>
    /// The window this content is hosted in, which is not the shell's. See <see cref="AttachTo"/>.
    /// </summary>
    private Window? _pictureWindow;

    /// <summary>
    /// The view model being watched for the one thing the cursor cannot be told by a binding alone.
    /// </summary>
    private PlayerOverlayViewModel? _watched;

    public PlayerOverlayView()
    {
        InitializeComponent();

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
        AttachTo(Window.GetWindow(this));
        WatchForCursorChanges(Overlay);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachTo(null);
        WatchForCursorChanges(null);
    }

    /// <summary>
    /// Follows the pointer over the whole picture, by way of the window the picture is drawn in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VideoView</c> hosts this content in a window of its own over the native video surface, so a pointer
    /// move over the picture never reaches the shell window's handlers — which is why waking the controls
    /// cannot be left to the shell, and why the mouse and the keyboard are handled in two different places:
    /// focus stays with the shell, the pointer does not.
    /// </para>
    /// <para>
    /// Taken from that window rather than from this control, and as the tunnelling event, so that nothing
    /// between the two can decide whether the controls come back. A slider being dragged marks the moves it
    /// consumes as handled, and hit-testing over a native video surface is not something this file should be
    /// relying on to be exactly right; the window sees every move regardless of both.
    /// </para>
    /// <para>
    /// Re-attached rather than assumed once, because the hosting window is created and replaced by
    /// <c>VideoView</c> rather than by anything here, and this content is moved between windows as that
    /// happens.
    /// </para>
    /// </remarks>
    private void AttachTo(Window? window)
    {
        if (ReferenceEquals(window, _pictureWindow))
        {
            return;
        }

        if (_pictureWindow is not null)
        {
            _pictureWindow.PreviewMouseMove -= OnPointerActivity;
            _pictureWindow.MouseDoubleClick -= OnPictureDoubleClicked;
        }

        _pictureWindow = window;

        if (_pictureWindow is null)
        {
            return;
        }

        _pictureWindow.PreviewMouseMove += OnPointerActivity;
        _pictureWindow.MouseDoubleClick += OnPictureDoubleClicked;
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

    private void OnPointerActivity(object sender, MouseEventArgs e)
    {
        Overlay?.Reveal();
    }

    /// <remarks>
    /// Bubbling rather than tunnelling, unlike the move above: buttons and pickers mark their own clicks
    /// handled, so this only ever sees a double-click on the picture itself rather than one aimed at a
    /// control.
    /// </remarks>
    private void OnPictureDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        Overlay?.ToggleFullscreenCommand.Execute(parameter: null);
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
