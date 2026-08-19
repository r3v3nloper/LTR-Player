using System.Windows;
using System.Windows.Input;

namespace LTR.Player.Wpf.Views;

/// <summary>
/// Watches the pointer over the whole picture, and remembers what it was aimed at.
/// </summary>
/// <remarks>
/// <para>
/// One subject, not two. Following the pointer has to be done on the *window* the picture is drawn in, and
/// telling a double-click on the picture from one on a button can only be done by remembering the press — and
/// that press is one of the events this already subscribes to. Split apart they read as two unrelated pieces of
/// glue; together they are "what the pointer is doing over the picture".
/// </para>
/// <para>
/// <c>VideoView</c> hosts the overlay's content in a window of its own over the native video surface, so a
/// pointer move over the picture never reaches the shell window's handlers — which is why waking the controls
/// cannot be left to the shell, and why the mouse and the keyboard are handled in two different places: focus
/// stays with the shell, the pointer does not.
/// </para>
/// <para>
/// Taken from that window rather than from the control, and as the tunnelling events, so that nothing between
/// the two can decide whether the controls come back. A slider being dragged marks the moves it consumes as
/// handled, and hit-testing over a native video surface is not something this should be relying on to be
/// exactly right; the window sees every move regardless of both.
/// </para>
/// </remarks>
internal sealed class PicturePointer
{
    private readonly UIElement _picture;
    private readonly Action _activity;
    private readonly Action _pictureDoubleClicked;

    /// <summary>
    /// The window the content is hosted in, which is not the shell's.
    /// </summary>
    /// <remarks>
    /// Held so it can be let go of again: the hosting window is created and replaced by <c>VideoView</c> rather
    /// than by anything here, and the content is moved between windows as that happens.
    /// </remarks>
    private Window? _window;

    /// <summary>What the last press was aimed at, which is what decides whose double-click it is.</summary>
    private object? _pressedOn;

    /// <param name="picture">
    /// The surface that counts as the picture. A double-click is only the picture's when the press was on this,
    /// which is also what keeps a double-click in the programme guide — drawn over the same picture — out.
    /// </param>
    /// <param name="activity">Run whenever the pointer moves anywhere over the picture's window.</param>
    /// <param name="pictureDoubleClicked">Run for a double-click that was aimed at the picture itself.</param>
    public PicturePointer(UIElement picture, Action activity, Action pictureDoubleClicked)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(pictureDoubleClicked);

        _picture = picture;
        _activity = activity;
        _pictureDoubleClicked = pictureDoubleClicked;
    }

    /// <summary>
    /// Starts watching <paramref name="window"/>, or stops watching altogether when it is null.
    /// </summary>
    /// <remarks>
    /// Re-attached rather than assumed once — see <see cref="_window"/>. Returning early on the same window is
    /// what keeps a second <c>Loaded</c> from subscribing twice.
    /// </remarks>
    public void AttachTo(Window? window)
    {
        if (ReferenceEquals(window, _window))
        {
            return;
        }

        if (_window is not null)
        {
            _window.PreviewMouseMove -= OnMoved;
            _window.PreviewMouseLeftButtonDown -= OnPressed;
            _window.MouseDoubleClick -= OnDoubleClicked;
        }

        _window = window;

        if (_window is null)
        {
            return;
        }

        _window.PreviewMouseMove += OnMoved;
        _window.PreviewMouseLeftButtonDown += OnPressed;
        _window.MouseDoubleClick += OnDoubleClicked;
    }

    private void OnMoved(object sender, MouseEventArgs e)
    {
        _activity();
    }

    /// <summary>
    /// Remembers what the pointer was actually aimed at, which the double-click itself cannot say.
    /// </summary>
    /// <remarks>
    /// Taken as the tunnelling event, so a control that deals with its own clicks — every button and both
    /// sliders — is still recorded as what was pressed.
    /// </remarks>
    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        _pressedOn = e.OriginalSource;
    }

    /// <summary>
    /// A double-click on the picture is passed on — and one aimed at anything else is not.
    /// </summary>
    /// <remarks>
    /// What was aimed at has to be checked against the press, because neither marking a click handled nor this
    /// event's own source will say. WPF raises <c>MouseDoubleClick</c> from a class handler registered for
    /// handled events too, so a button that has already dealt with the click still lets it reach here; and the
    /// event is *direct*, so it arrives naming the window rather than what was under the pointer. Two quick
    /// clicks on skip-forward, or into the volume bar, therefore went fullscreen and back.
    /// </remarks>
    private void OnDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(_pressedOn, _picture))
        {
            return;
        }

        _pictureDoubleClicked();
    }
}
