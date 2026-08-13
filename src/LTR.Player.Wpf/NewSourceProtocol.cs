namespace LTR.Player.Wpf;

/// <summary>
/// Which kind of subscription the user is adding.
/// </summary>
/// <remarks>
/// A presentation-level choice, deliberately separate from the source hierarchy in the domain: the
/// form needs a single bindable value to switch its fields on, whereas the domain distinguishes the
/// protocols by type.
/// </remarks>
public enum NewSourceProtocol
{
    /// <summary>Panel address with a username and password.</summary>
    Xtream = 0,

    /// <summary>A playlist URL or local file.</summary>
    M3uPlaylist = 1,
}
