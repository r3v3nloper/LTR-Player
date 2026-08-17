using LTR.Core.Content;

namespace LTR.Catalogue;

/// <summary>
/// A source's live channels, and the one thing about them the viewer owns.
/// </summary>
/// <remarks>
/// Deliberately holds no programme data. What is on a channel comes from <see cref="IGuideCatalogue"/>, which
/// is why the channel list depends on both: the two are published by different parties and imported on
/// different schedules, and a channel list is perfectly usable with no guide at all.
/// </remarks>
public interface ILiveCatalogue
{
    Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken);

    /// <remarks>
    /// A favourite is the user's own data, which is why a catalogue refresh reconciles rather than replaces.
    /// </remarks>
    Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken);
}
