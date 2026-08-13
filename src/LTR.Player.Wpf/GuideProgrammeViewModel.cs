using System.Globalization;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One programme block on the timeline.
/// </summary>
/// <remarks>
/// The geometry is computed once, when the block is built for a particular window, rather than bound
/// through a converter. A window holds a few thousand blocks, and a converter would recompute each of
/// them on every layout pass.
/// </remarks>
public sealed class GuideProgrammeViewModel
{
    public GuideProgrammeViewModel(EpgEntry entry, GuideTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(timeline);

        Entry = entry;
        (Left, Width) = timeline.Clip(entry.StartUtc, entry.StopUtc);

        Times = $"{Local(entry.StartUtc)}–{Local(entry.StopUtc)}";
    }

    public EpgEntry Entry { get; }

    public string Title => Entry.Title;

    public string Times { get; }

    public string? Description => Entry.Description;

    public string? Category => Entry.Category;

    public string? EpisodeReference => Entry.EpisodeReference;

    public double Left { get; }

    public double Width { get; }

    private static string Local(DateTimeOffset instant)
    {
        return instant.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
    }
}
