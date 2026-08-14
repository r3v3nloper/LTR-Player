using System.Text.Json;
using LTR.Core.Content;
using LTR.Providers.Xtream.Dtos;
using LTR.Providers.Xtream.Json;

namespace LTR.Providers.Xtream;

/// <summary>
/// Turns film and series responses into domain entities.
/// </summary>
/// <remarks>
/// Separate from <see cref="XtreamContentProvider"/>, which keeps the live catalogue and the account
/// reading. The film and series shapes are the most irregular part of the protocol — an episode listing
/// alone arrives in three different containers — and that irregularity is worth having under test on its
/// own rather than behind an HTTP boundary.
/// </remarks>
internal static class XtreamVodMapper
{
    private const string UnnamedMovieFallback = "(untitled film)";
    private const string UnnamedSeriesFallback = "(untitled series)";

    /// <summary>
    /// Maps a film listing, dropping entries with no stream id.
    /// </summary>
    /// <param name="skippedWithoutId">
    /// How many entries carried no identifier. Reported rather than logged here, so the mapper stays
    /// free of a logger and the count is stated once, by the caller that knows which source it was.
    /// </param>
    public static IReadOnlyList<VodItem> MapMovies(
        int sourceId,
        IReadOnlyList<XtreamVodStreamDto> dtos,
        out int skippedWithoutId)
    {
        var films = new List<VodItem>(dtos.Count);
        skippedWithoutId = 0;

        foreach (var dto in dtos)
        {
            // Without a stream identifier there is no address to build, so the entry is unplayable.
            if (string.IsNullOrWhiteSpace(dto.StreamId))
            {
                skippedWithoutId++;
                continue;
            }

            films.Add(new VodItem
            {
                SourceId = sourceId,
                ExternalId = dto.StreamId,
                Name = XtreamFields.Text(dto.Name) ?? UnnamedMovieFallback,
                CoverUrl = XtreamFields.ImageUrl(dto.StreamIcon),
                ContainerExtension = XtreamFields.Text(dto.ContainerExtension)?.TrimStart('.'),
                CategoryExternalId = XtreamFields.Text(dto.CategoryId),
                Rating = dto.Rating,
                Year = XtreamFields.Year(dto.ReleaseDate),
                Plot = XtreamFields.Text(dto.Plot),
                Genre = XtreamFields.Text(dto.Genre),
                AddedUtc = XtreamFields.Instant(dto.AddedUnixSeconds),
                SortOrder = films.Count,
            });
        }

        return films;
    }

    /// <summary>
    /// Maps a series listing, dropping entries with no series id.
    /// </summary>
    public static IReadOnlyList<Series> MapSeries(
        int sourceId,
        IReadOnlyList<XtreamSeriesDto> dtos,
        out int skippedWithoutId)
    {
        var series = new List<Series>(dtos.Count);
        skippedWithoutId = 0;

        foreach (var dto in dtos)
        {
            // Without a series id the detail call cannot be made, so nothing in it could ever be played.
            if (string.IsNullOrWhiteSpace(dto.SeriesId))
            {
                skippedWithoutId++;
                continue;
            }

            series.Add(new Series
            {
                SourceId = sourceId,
                ExternalId = dto.SeriesId,
                Name = XtreamFields.Text(dto.Name) ?? UnnamedSeriesFallback,
                CoverUrl = XtreamFields.ImageUrl(dto.Cover),
                CategoryExternalId = XtreamFields.Text(dto.CategoryId),
                Plot = XtreamFields.Text(dto.Plot),
                Genre = XtreamFields.Text(dto.Genre),
                Cast = XtreamFields.Text(dto.Cast),
                Director = XtreamFields.Text(dto.Director),
                Rating = dto.Rating,
                Year = XtreamFields.Year(dto.ReleaseDate),
                LastModifiedUtc = XtreamFields.Instant(dto.LastModifiedUnixSeconds),
                SortOrder = series.Count,
            });
        }

        return series;
    }

    /// <summary>
    /// Maps a film's detail response into the patch that is applied over the stored entry.
    /// </summary>
    public static MovieDetail MapMovieDetail(XtreamVodInfoResponseDto response)
    {
        var info = response.Info;

        return new MovieDetail(
            Plot: XtreamFields.Either(info?.Plot, info?.Description),
            Genre: XtreamFields.Text(info?.Genre),
            Cast: XtreamFields.Either(info?.Cast, info?.Actors),
            Director: XtreamFields.Text(info?.Director),
            Year: XtreamFields.Year(info?.ReleaseDate),
            Rating: info?.Rating,
            DurationSeconds: XtreamFields.DurationSeconds(info?.DurationSeconds, info?.RunTimeMinutes),
            ContainerExtension: XtreamFields.Text(response.MovieData?.ContainerExtension)?.TrimStart('.'));
    }

    /// <summary>
    /// Maps a series' detail response into its seasons, each carrying its episodes in order.
    /// </summary>
    /// <remarks>
    /// The seasons are built from the episodes rather than from the panel's own season list, which is
    /// empty on a great many panels. Where a season list does exist it supplies only the name, cover and
    /// overview of seasons the episodes already established — a declared season with no episodes is not
    /// shown, because there would be nothing in it to play.
    /// </remarks>
    public static SeriesDetail MapSeriesDetail(XtreamSeriesInfoResponseDto response)
    {
        var info = response.Info;
        var declared = DeclaredSeasonsByNumber(response.Seasons);
        var seasons = new List<Season>();

        foreach (var group in ReadEpisodes(response.Episodes).GroupBy(entry => entry.Season).OrderBy(g => g.Key))
        {
            var season = new Season { Number = group.Key };

            if (declared.TryGetValue(group.Key, out var declaredSeason))
            {
                season.Name = XtreamFields.Text(declaredSeason.Name);
                season.Plot = XtreamFields.Text(declaredSeason.Overview);
                season.CoverUrl = XtreamFields.ImageUrl(declaredSeason.CoverBig)
                    ?? XtreamFields.ImageUrl(declaredSeason.Cover);
            }

            var number = 0;

            foreach (var entry in group.OrderBy(item => item.Episode.EpisodeNumber))
            {
                number++;
                season.Episodes.Add(MapEpisode(entry.Episode, group.Key, number));
            }

            seasons.Add(season);
        }

        return new SeriesDetail(
            seasons,
            Plot: XtreamFields.Either(info?.Plot, info?.Description),
            Genre: XtreamFields.Text(info?.Genre),
            Cast: XtreamFields.Either(info?.Cast, info?.Actors),
            Director: XtreamFields.Text(info?.Director),
            Year: XtreamFields.Year(info?.ReleaseDate),
            Rating: info?.Rating);
    }

    private static Episode MapEpisode(XtreamEpisodeDto dto, int seasonNumber, int positionInSeason)
    {
        // Panels leave episode_num at zero often enough that the position within the season is the
        // dependable number; without it a whole season would present itself as episode zero.
        var episodeNumber = dto.EpisodeNumber > 0 ? dto.EpisodeNumber : positionInSeason;

        return new Episode
        {
            ExternalId = dto.Id!,
            Title = XtreamFields.Text(dto.Title) ?? EpisodeNaming.Label(seasonNumber, episodeNumber),
            Number = episodeNumber,
            ContainerExtension = XtreamFields.Text(dto.ContainerExtension)?.TrimStart('.'),
            Plot = XtreamFields.Text(dto.Info?.Plot),
            StillUrl = XtreamFields.ImageUrl(dto.Info?.Image),
            DurationSeconds = XtreamFields.DurationSeconds(dto.Info?.DurationSeconds, dto.Info?.RunTimeMinutes),
            AddedUtc = XtreamFields.Instant(dto.AddedUnixSeconds),
        };
    }

    private static Dictionary<int, XtreamSeasonDto> DeclaredSeasonsByNumber(List<XtreamSeasonDto>? seasons)
    {
        var byNumber = new Dictionary<int, XtreamSeasonDto>();

        if (seasons is null)
        {
            return byNumber;
        }

        foreach (var season in seasons)
        {
            // Last one wins rather than throwing: duplicate season numbers do occur.
            byNumber[season.Number] = season;
        }

        return byNumber;
    }

    /// <summary>
    /// Reads the episode listing whichever of its three shapes the panel used.
    /// </summary>
    /// <remarks>
    /// The documented shape is an object keyed by season number. Several forks send an array of season
    /// arrays instead, and a few send one flat array of episodes. Each episode's own <c>season</c> field
    /// is trusted over the key it was filed under, because the panels that key by season *name* are
    /// exactly the ones where the key means nothing.
    /// </remarks>
    private static List<(int Season, XtreamEpisodeDto Episode)> ReadEpisodes(JsonElement episodes)
    {
        var entries = new List<(int Season, XtreamEpisodeDto Episode)>();

        switch (episodes.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in episodes.EnumerateObject())
                {
                    AddGroup(entries, property.Value, SeasonNumberFromKey(property.Name));
                }

                break;

            case JsonValueKind.Array:
            {
                var index = 0;

                foreach (var element in episodes.EnumerateArray())
                {
                    // A flat array of episodes must not have its position read as a season number.
                    AddGroup(entries, element, element.ValueKind == JsonValueKind.Array ? index : 1);
                    index++;
                }

                break;
            }
        }

        return entries;
    }

    private static void AddGroup(
        List<(int Season, XtreamEpisodeDto Episode)> entries,
        JsonElement group,
        int fallbackSeason)
    {
        if (group.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in group.EnumerateArray())
            {
                AddEpisode(entries, element, fallbackSeason);
            }

            return;
        }

        AddEpisode(entries, group, fallbackSeason);
    }

    private static void AddEpisode(
        List<(int Season, XtreamEpisodeDto Episode)> entries,
        JsonElement element,
        int fallbackSeason)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        XtreamEpisodeDto? dto;

        try
        {
            dto = element.Deserialize<XtreamEpisodeDto>(XtreamJson.Options);
        }
        catch (JsonException)
        {
            // One episode with an unexpected field shape must not cost the series its other episodes.
            return;
        }

        // No id means no address, and an episode that cannot be played is not worth listing.
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id))
        {
            return;
        }

        entries.Add((dto.Season ?? fallbackSeason, dto));
    }

    /// <summary>
    /// Reads a season number from the key an episode group was filed under, defaulting to one.
    /// </summary>
    /// <remarks>
    /// Keys are season numbers on most panels and season names on a few. A name yields no number here,
    /// and the episodes' own <c>season</c> field is what settles it in that case.
    /// </remarks>
    private static int SeasonNumberFromKey(string key)
    {
        return int.TryParse(key, out var number) && number >= 0 ? number : 1;
    }
}
