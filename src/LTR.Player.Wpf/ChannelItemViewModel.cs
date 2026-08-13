using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One row of the channel list.
/// </summary>
/// <remarks>
/// A wrapper exists because the favourite marker has to update in place when toggled, and that needs
/// change notification. Putting <see cref="System.ComponentModel.INotifyPropertyChanged"/> on
/// <see cref="Core.Content.Channel"/> would push a presentation concern into the domain and into the
/// database entity. One wrapper per channel costs a couple of megabytes at twenty thousand channels,
/// which is the cheaper of the two prices.
/// </remarks>
public sealed partial class ChannelItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isFavorite;

    public ChannelItemViewModel(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        Channel = channel;
        _isFavorite = channel.IsFavorite;
    }

    public Channel Channel { get; }

    public int Id => Channel.Id;

    public string Name => Channel.Name;

    /// <summary>
    /// Catch-up availability, shown so the user can see which channels the provider retains.
    /// </summary>
    public bool HasArchive => Channel.HasArchive;

    partial void OnIsFavoriteChanged(bool value)
    {
        // Kept in step so the filter, which reads the entity, agrees with what the row displays.
        Channel.IsFavorite = value;
    }
}
