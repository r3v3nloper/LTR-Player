namespace LTR.Player.Wpf;

/// <summary>
/// One entry of a track menu.
/// </summary>
/// <param name="Id">
/// The engine's identifier for the track, or <see cref="LTR.Playback.MediaTrack.DisabledId"/> for the
/// entry that switches the kind off.
/// </param>
/// <param name="Label">What the menu shows.</param>
public sealed record TrackChoice(int Id, string Label);
