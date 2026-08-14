using LTR.Playback.LibVlc;

namespace LTR.Player.Wpf;

/// <summary>
/// One entry of the decoding menu.
/// </summary>
/// <remarks>
/// Worded by symptom rather than by API name. Nobody reaches for this because they want Direct3D; they reach
/// for it because a channel is a slideshow or shows green blocks, and the label has to say which setting
/// answers which.
/// </remarks>
public sealed record HardwareDecodingChoice(HardwareDecoding Value, string Label)
{
    public static IReadOnlyList<HardwareDecodingChoice> All { get; } =
    [
        new(HardwareDecoding.Automatic, "Automatic (recommended)"),
        new(HardwareDecoding.Direct3D11, "Force the graphics card"),
        new(HardwareDecoding.Disabled, "Software only (for a corrupted picture)"),
    ];

    public static HardwareDecodingChoice For(HardwareDecoding value)
    {
        return All.First(choice => choice.Value == value);
    }
}
