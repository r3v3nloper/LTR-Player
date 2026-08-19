namespace LTR.Player.Wpf;

/// <summary>
/// Something the viewer can ask for from the keyboard.
/// </summary>
/// <remarks>
/// Named intentions rather than key codes, because the two belong to different layers: which key does what
/// is a decision about the keyboard, and what each thing does is a decision about the player. Separating
/// them is also what makes the keyboard testable at all — asserting that Page Down means "next" needs no
/// window.
/// </remarks>
public enum PlayerAction
{
    TogglePause,
    Stop,

    /// <summary>The next thing of the kind that is playing: the next episode, or the next channel.</summary>
    PlayNext,

    PlayPrevious,

    VolumeUp,
    VolumeDown,
    ToggleMute,
    SkipBack,
    SkipForward,
    ToggleFullscreen,
    LeaveFullscreen,
    ToggleGuide,

    /// <summary>Bring the on-screen display up without changing anything.</summary>
    ShowInfo,

    CycleAspectRatio,
}
