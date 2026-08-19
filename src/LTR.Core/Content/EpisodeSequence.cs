namespace LTR.Core.Content;

/// <summary>
/// States the order a series is watched in, and what lies either side of one episode.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the player because it decides nothing about presentation and performs no I/O — the
/// same reason <see cref="SeriesReconciliation"/> is in Core. What season an episode follows is a fact about a
/// series, and a web front end is planned that would have to agree with this one about it.
/// </para>
/// <para>
/// The order is imposed rather than assumed. Seasons and episodes arrive from a panel in whatever order it
/// listed them, and a season fetched later is frequently appended rather than inserted, so a walk that
/// trusted the stored order would call season three's first episode the successor of season one's last.
/// </para>
/// </remarks>
public static class EpisodeSequence
{
    /// <summary>
    /// Every episode of <paramref name="series"/> in the order it is watched: season by season, and within
    /// a season by episode number.
    /// </summary>
    public static IReadOnlyList<EpisodeInSeries> InViewingOrder(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return
        [
            .. series.Seasons
                .OrderBy(season => season.Number)
                .SelectMany(season => season.Episodes
                    .OrderBy(episode => episode.Number)
                    .Select(episode => new EpisodeInSeries(episode, season.Number))),
        ];
    }

    /// <summary>
    /// The episode <paramref name="offset"/> places from the one identified by <paramref name="episodeId"/>,
    /// or <see langword="null"/> when that would fall outside the series.
    /// </summary>
    /// <remarks>
    /// Crossing a season boundary deliberately: the last episode of a season is followed by the first of the
    /// next, because that is what watching a series means. Running off either end answers nothing rather than
    /// wrapping around, so the end of the last season is quiet instead of restarting the series.
    /// </remarks>
    public static EpisodeInSeries? Neighbour(Series series, int episodeId, int offset)
    {
        var ordered = InViewingOrder(series);

        var position = -1;

        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Episode.Id == episodeId)
            {
                position = index;
                break;
            }
        }

        if (position < 0)
        {
            return null;
        }

        var target = position + offset;

        return target >= 0 && target < ordered.Count ? ordered[target] : null;
    }
}
