namespace LTR.Core.Content;

/// <summary>
/// Brings a stored series' seasons and episodes in line with a detail response.
/// </summary>
/// <remarks>
/// <para>
/// A reconciliation and not a replacement, for the same reason the catalogue as a whole is one: a viewer's
/// position in an episode is their own data, and rebuilding the seasons would throw it away. Episodes are
/// matched by their own identifier across the *whole* series rather than within one season, so an episode the
/// provider refiles keeps the position the viewer reached in it.
/// </para>
/// <para>
/// It performs no I/O and never did — it works on entities already in hand — but it lived inside the database
/// context, where the only way to test it was against real SQLite. Here it is reachable on its own, which is
/// what the cases below deserve: they are the ones a panel produces and a reader would not guess.
/// </para>
/// </remarks>
public static class SeriesReconciliation
{
    /// <summary>
    /// Applies a detail response to a stored series, and returns how many episodes it now holds.
    /// </summary>
    public static int Apply(Series stored, SeriesDetail detail)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(detail);

        var storedEpisodes = stored.Seasons.SelectMany(season => season.Episodes).ToList();
        var episodesByExternalId = IndexEpisodes(storedEpisodes);
        var storedSeasons = stored.Seasons.ToDictionary(season => season.Number);

        var seenSeasons = new HashSet<int>();
        var kept = new HashSet<Episode>();
        var episodeCount = 0;

        foreach (var incomingSeason in detail.Seasons)
        {
            seenSeasons.Add(incomingSeason.Number);

            if (!storedSeasons.TryGetValue(incomingSeason.Number, out var season))
            {
                season = new Season { Number = incomingSeason.Number, Episodes = [] };
                stored.Seasons.Add(season);
                storedSeasons[incomingSeason.Number] = season;
            }

            season.Name = incomingSeason.Name ?? season.Name;
            season.CoverUrl = incomingSeason.CoverUrl ?? season.CoverUrl;
            season.Plot = incomingSeason.Plot ?? season.Plot;

            foreach (var incomingEpisode in incomingSeason.Episodes)
            {
                episodeCount++;
                Apply(stored, season, incomingEpisode, episodesByExternalId, kept);
            }
        }

        // Driven by which instances were matched rather than by which identifiers were seen, so that a row
        // the provider has stopped listing goes — and so does the second copy of one it listed twice.
        foreach (var episode in storedEpisodes.Where(item => !kept.Contains(item)))
        {
            RemoveFromCurrentSeason(stored, episode);
        }

        foreach (var season in stored.Seasons.Where(item => !seenSeasons.Contains(item.Number)).ToList())
        {
            stored.Seasons.Remove(season);
        }

        return episodeCount;
    }

    /// <summary>
    /// Indexed across the whole series, so an episode the provider moves between seasons is recognised as the
    /// same episode.
    /// </summary>
    /// <remarks>
    /// <c>TryAdd</c> rather than <c>Add</c>: a provider that lists the same episode under two seasons would
    /// otherwise throw here, and the duplicate is dealt with by keeping only what was matched.
    /// </remarks>
    private static Dictionary<string, Episode> IndexEpisodes(IReadOnlyList<Episode> storedEpisodes)
    {
        var index = new Dictionary<string, Episode>(StringComparer.Ordinal);

        foreach (var episode in storedEpisodes)
        {
            index.TryAdd(episode.ExternalId, episode);
        }

        return index;
    }

    private static void Apply(
        Series stored,
        Season season,
        Episode incoming,
        Dictionary<string, Episode> episodesByExternalId,
        HashSet<Episode> kept)
    {
        if (!episodesByExternalId.TryGetValue(incoming.ExternalId, out var episode))
        {
            season.Episodes.Add(incoming);
            episodesByExternalId[incoming.ExternalId] = incoming;
            kept.Add(incoming);

            return;
        }

        kept.Add(episode);

        episode.Title = incoming.Title;
        episode.Number = incoming.Number;
        episode.ContainerExtension = incoming.ContainerExtension ?? episode.ContainerExtension;
        episode.Plot = incoming.Plot ?? episode.Plot;
        episode.StillUrl = incoming.StillUrl ?? episode.StillUrl;
        episode.DurationSeconds = incoming.DurationSeconds ?? episode.DurationSeconds;
        episode.AddedUtc = incoming.AddedUtc ?? episode.AddedUtc;

        // Refiled by the provider: move it rather than duplicating it, and the viewer's position travels
        // with the row.
        if (episode.SeasonId != season.Id || !season.Episodes.Contains(episode))
        {
            RemoveFromCurrentSeason(stored, episode);
            season.Episodes.Add(episode);
        }
    }

    private static void RemoveFromCurrentSeason(Series stored, Episode episode)
    {
        foreach (var season in stored.Seasons)
        {
            if (season.Episodes.Remove(episode))
            {
                return;
            }
        }
    }
}
