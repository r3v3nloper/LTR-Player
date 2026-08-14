using LTR.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// One entry of the aspect ratio menu.
/// </summary>
public sealed record AspectRatioChoice(VideoAspectRatio Value, string Label)
{
    /// <summary>
    /// The menu, in the order it is offered.
    /// </summary>
    /// <remarks>
    /// Wording rather than ratios first, because the reason to touch this is never "I want 4:3" but "this
    /// channel is stretched". A viewer who knows which ratio they want can read it off the label; one who
    /// only knows the picture looks wrong cannot work backwards from "4:3".
    /// </remarks>
    public static IReadOnlyList<AspectRatioChoice> All { get; } =
    [
        new(VideoAspectRatio.Source, "Aspect: as broadcast"),
        new(VideoAspectRatio.Widescreen, "Aspect: 16:9"),
        new(VideoAspectRatio.Standard, "Aspect: 4:3"),
    ];

    public static AspectRatioChoice For(VideoAspectRatio value)
    {
        return All.First(choice => choice.Value == value);
    }

    /// <summary>The entry after this one, wrapping round, for the keyboard shortcut.</summary>
    public static AspectRatioChoice After(VideoAspectRatio value)
    {
        var next = All.Select((choice, index) => (choice, index)).First(pair => pair.choice.Value == value);

        return All[(next.index + 1) % All.Count];
    }
}
