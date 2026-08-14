using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One entry of the container menu offered for a source.
/// </summary>
/// <remarks>
/// Only the two that can be requested. <see cref="StreamFormat.ProgressiveFile"/> is what a film is stored
/// as rather than something a viewer can ask for, and offering it would produce an address no panel serves.
/// </remarks>
public sealed record StreamFormatChoice(StreamFormat Value, string Label)
{
    public static IReadOnlyList<StreamFormatChoice> All { get; } =
    [
        new(StreamFormat.MpegTs, "Transport stream (zaps faster)"),
        new(StreamFormat.HlsPlaylist, "HLS (survives a flaky connection)"),
    ];

    public static StreamFormatChoice For(StreamFormat value)
    {
        // A film's own container is not a choice, so anything unexpected falls back to the default rather
        // than throwing at a picker.
        return All.FirstOrDefault(choice => choice.Value == value) ?? All[0];
    }
}
