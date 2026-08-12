namespace LTR.Playback;

/// <summary>
/// One selectable audio, video or subtitle track of the stream currently open.
/// </summary>
/// <param name="Id">Engine-assigned identifier, valid only for the current stream.</param>
/// <param name="Kind">Which kind of track this is.</param>
/// <param name="Name">Label reported by the stream, if any.</param>
/// <param name="Language">Language tag reported by the stream, if any.</param>
public sealed record MediaTrack(int Id, MediaTrackKind Kind, string? Name, string? Language)
{
    /// <summary>
    /// Label for the track selection menu, falling back through name and language because IPTV
    /// streams frequently supply neither.
    /// </summary>
    public string DisplayLabel => Name ?? Language ?? $"Track {Id}";
}
