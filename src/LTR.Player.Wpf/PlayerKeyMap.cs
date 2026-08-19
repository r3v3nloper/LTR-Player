using System.Windows.Input;

namespace LTR.Player.Wpf;

/// <summary>
/// Which key means what.
/// </summary>
/// <remarks>
/// <para>
/// A lookup rather than <c>KeyBinding</c> entries in the window's markup, and the reason is the search box.
/// An input binding declared on the window is offered the key before the focused element gets it, so binding
/// the arrow keys to skipping would take them away from every text box in the window — a viewer correcting a
/// typo in the channel filter would seek the film instead. The caller therefore asks this map only once it
/// has established that the keystroke was not meant for something being typed into.
/// </para>
/// <para>
/// Only unmodified keys are mapped. A player's shortcuts are single presses, and answering
/// <c>Ctrl+F</c> — which is what someone reaches for to search — with fullscreen would be actively wrong.
/// </para>
/// </remarks>
public static class PlayerKeyMap
{
    /// <summary>
    /// What <paramref name="key"/> means, or <see langword="null"/> when it means nothing to the player.
    /// </summary>
    public static PlayerAction? Resolve(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None)
        {
            return null;
        }

        return key switch
        {
            Key.Space or Key.MediaPlayPause => PlayerAction.TogglePause,
            Key.MediaStop => PlayerAction.Stop,

            // Page keys rather than the arrows. Arrow keys move the channel list's selection without
            // playing anything, and taking them for zapping would leave no way to look down a list of
            // seventeen thousand channels without opening every one on the way.
            Key.PageDown or Key.MediaNextTrack => PlayerAction.PlayNext,
            Key.PageUp or Key.MediaPreviousTrack => PlayerAction.PlayPrevious,

            // Plus and minus, from either the main row or the numeric keypad, for the same reason.
            Key.OemPlus or Key.Add or Key.VolumeUp => PlayerAction.VolumeUp,
            Key.OemMinus or Key.Subtract or Key.VolumeDown => PlayerAction.VolumeDown,
            Key.M or Key.VolumeMute => PlayerAction.ToggleMute,

            Key.Left => PlayerAction.SkipBack,
            Key.Right => PlayerAction.SkipForward,

            Key.F or Key.F11 => PlayerAction.ToggleFullscreen,
            Key.Escape => PlayerAction.LeaveFullscreen,

            Key.G => PlayerAction.ToggleGuide,
            Key.I => PlayerAction.ShowInfo,
            Key.A => PlayerAction.CycleAspectRatio,

            _ => null,
        };
    }
}
