using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One channel's line on the timeline.
/// </summary>
public sealed class GuideRowViewModel
{
    public GuideRowViewModel(Channel channel, IReadOnlyList<GuideProgrammeViewModel> programmes)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(programmes);

        Channel = channel;
        Programmes = programmes;
    }

    public Channel Channel { get; }

    public string Name => Channel.Name;

    public IReadOnlyList<GuideProgrammeViewModel> Programmes { get; }

    /// <summary>
    /// Whether the guide says anything at all about this channel in this window, which is worth showing
    /// as such: an empty line explains itself, whereas no line at all looks like the channel is missing.
    /// </summary>
    public bool HasProgrammes => Programmes.Count > 0;
}
