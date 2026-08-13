using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One row of the channel list.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper exists because the favourite marker has to update in place when toggled, and that needs
/// change notification. Putting <see cref="System.ComponentModel.INotifyPropertyChanged"/> on
/// <see cref="Core.Content.Channel"/> would push a presentation concern into the domain and into the
/// database entity. One wrapper per channel costs a couple of megabytes at twenty thousand channels,
/// which is the cheaper of the two prices.
/// </para>
/// <para>
/// The favourite flag lives here and nowhere else. It used to be written back into the entity as well, so
/// that the filter — which read the entity — agreed with what the row displayed; the filter now reads the
/// row, and the entity is left as the provider's record of the channel.
/// </para>
/// </remarks>
public sealed partial class ChannelItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _nowTitle = string.Empty;

    [ObservableProperty]
    private string _nowTimes = string.Empty;

    [ObservableProperty]
    private string _nextTitle = string.Empty;

    /// <summary>How far the running programme has progressed, from 0 to 1.</summary>
    [ObservableProperty]
    private double _nowProgress;

    public ChannelItemViewModel(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Channel = channel;
        _isFavorite = channel.IsFavorite;
    }

    public Channel Channel { get; }

    public int Id => Channel.Id;

    public string Name => Channel.Name;

    /// <summary>The provider category this row belongs to, which the filter narrows by.</summary>
    public string? CategoryExternalId => Channel.CategoryExternalId;

    /// <summary>
    /// Catch-up availability, shown so the user can see which channels the provider retains.
    /// </summary>
    public bool HasArchive => Channel.HasArchive;

    /// <summary>
    /// Shows what is on now, or clears it when <paramref name="slice"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Clearing on absence is what stops a row keeping a programme that has since ended, or one belonging
    /// to a source the list no longer shows.
    /// </remarks>
    public void ShowGuide(ChannelGuideSlice? slice, DateTimeOffset atUtc)
    {
        NowTitle = slice?.Now?.Title ?? string.Empty;
        NowTimes = slice?.Now is { } now ? $"{Local(now.StartUtc)}–{Local(now.StopUtc)}" : string.Empty;
        NowProgress = slice?.Now is { } running ? ProgressThrough(running, atUtc) : 0;
        NextTitle = slice?.Next is { } next ? $"then {next.Title}" : string.Empty;
    }

    private static double ProgressThrough(EpgEntry entry, DateTimeOffset atUtc)
    {
        var duration = entry.Duration.TotalSeconds;

        // A zero-length programme cannot occur — the import rejects one — but dividing by it here would
        // take the window down rather than show an odd bar.
        return duration <= 0
            ? 0
            : Math.Clamp((atUtc - entry.StartUtc).TotalSeconds / duration, 0, 1);
    }

    /// <summary>
    /// Programme times are shown in the viewer's own zone. Everything is stored and compared in UTC;
    /// this is the only place that has any business converting.
    /// </summary>
    private static string Local(DateTimeOffset instant)
    {
        return instant.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture);
    }
}
