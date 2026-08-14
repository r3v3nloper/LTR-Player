namespace LTR.Playback;

/// <summary>
/// How the picture is fitted to the window.
/// </summary>
/// <remarks>
/// A short list on purpose. The reason a viewer reaches for this at all is that broadcasters and panels
/// misdeclare the ratio — a 4:3 channel flagged as widescreen arrives stretched, and no player can detect
/// that, only a person can. Offering every ratio VLC accepts would not help with that and would turn a
/// two-click correction into a menu.
/// </remarks>
public enum VideoAspectRatio
{
    /// <summary>Whatever the stream declares. Correct for nearly everything.</summary>
    Source = 0,

    /// <summary>Force 16:9, for a widescreen channel that declares itself as 4:3.</summary>
    Widescreen = 1,

    /// <summary>Force 4:3, for the opposite mistake.</summary>
    Standard = 2,
}
