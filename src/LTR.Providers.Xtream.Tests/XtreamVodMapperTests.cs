using System.Text.Json;
using LTR.Providers.Xtream.Dtos;
using LTR.Providers.Xtream.Json;

namespace LTR.Providers.Xtream;

/// <summary>
/// Covers the film and series mapping, driven from JSON rather than from hand-built DTOs.
/// </summary>
/// <remarks>
/// Deliberately end-to-end through the deserialiser: the shapes that break here are shapes panels emit,
/// and half of the tolerance under test lives in the converters rather than in the mapper. Hand-built
/// DTOs would test the mapper against a shape no panel produces.
/// </remarks>
public sealed class XtreamVodMapperTests
{
    private const int SourceId = 7;

    [Fact]
    public void MapMovies_ReadsAFullListingEntry()
    {
        // Arrange: a newer panel, which states the synopsis in the listing as well.
        var dtos = DeserializeList<XtreamVodStreamDto>("""
            [{
              "num": 1,
              "name": "Arrival",
              "stream_id": 8412,
              "stream_icon": "http://covers.example/arrival.jpg",
              "container_extension": "mkv",
              "category_id": "58",
              "rating": "7.9",
              "added": "1600000000",
              "plot": "Linguist meets heptapods.",
              "genre": "Science Fiction",
              "releasedate": "2016-11-11"
            }]
            """);

        // Act
        var movies = XtreamVodMapper.MapMovies(SourceId, dtos, out var skipped);

        // Assert
        skipped.ShouldBe(0);
        var movie = movies.ShouldHaveSingleItem();
        movie.SourceId.ShouldBe(SourceId);
        movie.ExternalId.ShouldBe("8412");
        movie.Name.ShouldBe("Arrival");
        movie.CoverUrl.ShouldBe("http://covers.example/arrival.jpg");
        movie.ContainerExtension.ShouldBe("mkv");
        movie.CategoryExternalId.ShouldBe("58");
        movie.Rating.ShouldBe(7.9);
        movie.Year.ShouldBe(2016);
        movie.Genre.ShouldBe("Science Fiction");
        movie.AddedUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000));
        movie.HasDetail.ShouldBeFalse();
    }

    [Fact]
    public void MapMovies_ReadsASparseListingEntry()
    {
        // Arrange: an older panel states nothing but the identity, and the empty rating must not be read
        // as a rating of zero.
        var dtos = DeserializeList<XtreamVodStreamDto>("""
            [{ "name": "Unknown film", "stream_id": "1", "rating": "", "added": "0",
               "container_extension": "", "category_id": null }]
            """);

        // Act
        var movie = XtreamVodMapper.MapMovies(SourceId, dtos, out _).ShouldHaveSingleItem();

        // Assert
        movie.Rating.ShouldBeNull();
        movie.AddedUtc.ShouldBeNull();
        movie.ContainerExtension.ShouldBeNull();
        movie.CategoryExternalId.ShouldBeNull();
    }

    [Fact]
    public void MapMovies_DropsEntriesWithNoStreamId()
    {
        // Arrange: no identifier means no address, so the entry could never be played.
        var dtos = DeserializeList<XtreamVodStreamDto>("""
            [{ "name": "Playable", "stream_id": "1" },
             { "name": "Broken", "stream_id": "" },
             { "name": "Also broken" }]
            """);

        // Act
        var movies = XtreamVodMapper.MapMovies(SourceId, dtos, out var skipped);

        // Assert
        movies.ShouldHaveSingleItem().Name.ShouldBe("Playable");
        skipped.ShouldBe(2);
    }

    [Fact]
    public void MapMovies_StripsALeadingDotFromTheContainer()
    {
        // Arrange: panels state the extension both ways, and a doubled dot is a 404.
        var dtos = DeserializeList<XtreamVodStreamDto>("""
            [{ "name": "A", "stream_id": "1", "container_extension": ".mp4" }]
            """);

        // Act
        var movie = XtreamVodMapper.MapMovies(SourceId, dtos, out _).ShouldHaveSingleItem();

        // Assert
        movie.ContainerExtension.ShouldBe("mp4");
    }

    [Fact]
    public void MapMovies_NumbersEntriesInTheOrderThePanelSentThem()
    {
        // Arrange: the provider's ordering is the one the catalogue is browsed in.
        var dtos = DeserializeList<XtreamVodStreamDto>("""
            [{ "name": "First", "stream_id": "1" }, { "name": "Second", "stream_id": "2" }]
            """);

        // Act
        var movies = XtreamVodMapper.MapMovies(SourceId, dtos, out _);

        // Assert
        movies[0].SortOrder.ShouldBe(0);
        movies[1].SortOrder.ShouldBe(1);
    }

    [Fact]
    public void MapSeries_ReadsAListingEntry()
    {
        // Arrange: note releaseDate with a capital D here, unlike the film listing.
        var dtos = DeserializeList<XtreamSeriesDto>("""
            [{
              "num": 1,
              "name": "Breaking Bad",
              "series_id": 4321,
              "cover": "http://covers.example/bb.jpg",
              "plot": "A chemistry teacher.",
              "cast": "Bryan Cranston",
              "director": "Vince Gilligan",
              "genre": "Drama",
              "releaseDate": "2008-01-20",
              "rating": 9.5,
              "category_id": "75",
              "last_modified": "1700000000"
            }]
            """);

        // Act
        var series = XtreamVodMapper.MapSeries(SourceId, dtos, out var skipped).ShouldHaveSingleItem();

        // Assert
        skipped.ShouldBe(0);
        series.ExternalId.ShouldBe("4321");
        series.Name.ShouldBe("Breaking Bad");
        series.CoverUrl.ShouldBe("http://covers.example/bb.jpg");
        series.Year.ShouldBe(2008);
        series.Rating.ShouldBe(9.5);
        series.CategoryExternalId.ShouldBe("75");
        series.LastModifiedUtc.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        series.HasCurrentDetail.ShouldBeFalse("nothing has been fetched yet");
    }

    [Fact]
    public void MapSeries_DropsEntriesWithNoSeriesId()
    {
        // Arrange: without a series id the detail call cannot be made, so nothing in it could be played.
        var dtos = DeserializeList<XtreamSeriesDto>("""
            [{ "name": "Fine", "series_id": "1" }, { "name": "Broken", "series_id": "" }]
            """);

        // Act
        var series = XtreamVodMapper.MapSeries(SourceId, dtos, out var skipped);

        // Assert
        series.ShouldHaveSingleItem().Name.ShouldBe("Fine");
        skipped.ShouldBe(1);
    }

    [Fact]
    public void MapSeriesDetail_ReadsTheDocumentedObjectKeyedBySeasonNumber()
    {
        // Arrange
        var detail = MapDetail("""
            {
              "info": { "plot": "A chemistry teacher.", "genre": "Drama", "actors": "Bryan Cranston",
                        "releasedate": "2008-01-20", "rating": "9.5" },
              "seasons": [{ "season_number": 1, "name": "Season 1", "overview": "The first.",
                            "cover_big": "http://covers.example/s1.jpg" }],
              "episodes": {
                "1": [
                  { "id": "1001", "episode_num": 1, "title": "Pilot", "container_extension": "mkv",
                    "season": 1, "added": "1600000000",
                    "info": { "plot": "It begins.", "duration_secs": 2820,
                              "movie_image": "http://stills.example/1.jpg" } },
                  { "id": "1002", "episode_num": 2, "title": "Cat's in the Bag...",
                    "container_extension": "mp4", "season": 1 }
                ],
                "2": [
                  { "id": "2001", "episode_num": 1, "title": "Seven Thirty-Seven", "season": 2 }
                ]
              }
            }
            """);

        // Assert
        detail.Plot.ShouldBe("A chemistry teacher.");
        detail.Cast.ShouldBe("Bryan Cranston", "the panel spelled it 'actors'");
        detail.Year.ShouldBe(2008);
        detail.Seasons.Count.ShouldBe(2);

        var first = detail.Seasons[0];
        first.Number.ShouldBe(1);
        first.Name.ShouldBe("Season 1");
        first.CoverUrl.ShouldBe("http://covers.example/s1.jpg");
        first.Episodes.Count.ShouldBe(2);

        var pilot = first.Episodes.First();
        pilot.ExternalId.ShouldBe("1001");
        pilot.Title.ShouldBe("Pilot");
        pilot.Number.ShouldBe(1);
        pilot.ContainerExtension.ShouldBe("mkv");
        pilot.DurationSeconds.ShouldBe(2820);
        pilot.StillUrl.ShouldBe("http://stills.example/1.jpg");

        detail.Seasons[1].Number.ShouldBe(2);
        detail.Seasons[1].Episodes.ShouldHaveSingleItem().ExternalId.ShouldBe("2001");
    }

    [Fact]
    public void MapSeriesDetail_WithNoDeclaredSeasons_StillDerivesThemFromTheEpisodes()
    {
        // Arrange: a great many panels send an empty seasons array for a series with several of them.
        var detail = MapDetail("""
            {
              "seasons": [],
              "episodes": {
                "1": [{ "id": "1", "episode_num": 1, "title": "One", "season": 1 }],
                "2": [{ "id": "2", "episode_num": 1, "title": "Two", "season": 2 }]
              }
            }
            """);

        // Act & Assert
        detail.Seasons.Select(season => season.Number).ShouldBe([1, 2]);
        detail.Seasons[0].Name.ShouldBeNull("nothing declared one");
    }

    [Fact]
    public void MapSeriesDetail_IgnoresADeclaredSeasonWithNoEpisodes()
    {
        // Arrange: there would be nothing in it to play, and an empty season on screen reads as a fault.
        var detail = MapDetail("""
            {
              "seasons": [{ "season_number": 1, "name": "Season 1" },
                          { "season_number": 2, "name": "Season 2" }],
              "episodes": { "1": [{ "id": "1", "episode_num": 1, "title": "One", "season": 1 }] }
            }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Number.ShouldBe(1);
    }

    [Fact]
    public void MapSeriesDetail_ReadsAnArrayOfSeasonArrays()
    {
        // Arrange: several forks emit this shape instead of the documented object. A typed property
        // would have thrown here and lost the whole series over its container.
        var detail = MapDetail("""
            {
              "episodes": [
                [],
                [{ "id": "11", "episode_num": 1, "title": "One" }],
                [{ "id": "21", "episode_num": 1, "title": "Two" }]
              ]
            }
            """);

        // Act & Assert
        detail.Seasons.Select(season => season.Number).ShouldBe([1, 2]);
        detail.Seasons[0].Episodes.ShouldHaveSingleItem().ExternalId.ShouldBe("11");
    }

    [Fact]
    public void MapSeriesDetail_ReadsAFlatArrayOfEpisodes()
    {
        // Arrange: the third shape in circulation. Position must not be read as a season number here,
        // or every episode would land in a season of its own.
        var detail = MapDetail("""
            {
              "episodes": [
                { "id": "1", "episode_num": 1, "title": "One", "season": 1 },
                { "id": "2", "episode_num": 2, "title": "Two", "season": 1 }
              ]
            }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Episodes.Count.ShouldBe(2);
    }

    [Fact]
    public void MapSeriesDetail_WhenTheKeyIsASeasonName_TrustsTheEpisodesOwnSeason()
    {
        // Arrange: on the panels that key by name, the key means nothing.
        var detail = MapDetail("""
            {
              "episodes": {
                "Season 3": [{ "id": "31", "episode_num": 1, "title": "One", "season": 3 }]
              }
            }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Number.ShouldBe(3);
    }

    [Fact]
    public void MapSeriesDetail_WhenTheEpisodeNumberIsMissing_NumbersByPosition()
    {
        // Arrange: panels leave episode_num at zero often enough that a whole season would otherwise
        // present itself as episode zero.
        var detail = MapDetail("""
            {
              "episodes": {
                "1": [{ "id": "1", "title": "First", "season": 1 },
                      { "id": "2", "title": "Second", "season": 1 }]
              }
            }
            """);

        // Act
        var episodes = detail.Seasons.ShouldHaveSingleItem().Episodes.ToList();

        // Assert
        episodes.Select(episode => episode.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void MapSeriesDetail_WithoutATitle_LabelsTheEpisodeBySeasonAndNumber()
    {
        // Arrange
        var detail = MapDetail("""
            { "episodes": { "2": [{ "id": "1", "episode_num": 5, "season": 2, "title": "" }] } }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().Title.ShouldBe("S02E05");
    }

    [Fact]
    public void MapSeriesDetail_DropsEpisodesWithNoIdentifier()
    {
        // Arrange: an episode with no id has no address, so listing it would offer something unplayable.
        var detail = MapDetail("""
            {
              "episodes": {
                "1": [{ "id": "1", "episode_num": 1, "title": "Fine", "season": 1 },
                      { "episode_num": 2, "title": "No id", "season": 1 }]
              }
            }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().Title.ShouldBe("Fine");
    }

    [Fact]
    public void MapSeriesDetail_WhenOneEpisodeHasAnImpossibleFieldShape_KeepsTheOthers()
    {
        // Arrange: a title arriving as an object is unreadable, and losing one episode over it is far
        // better than losing the series.
        var detail = MapDetail("""
            {
              "episodes": {
                "1": [{ "id": "1", "episode_num": 1, "title": { "en": "Nested" }, "season": 1 },
                      { "id": "2", "episode_num": 2, "title": "Readable", "season": 1 }]
              }
            }
            """);

        // Act & Assert
        detail.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().Title.ShouldBe("Readable");
    }

    [Fact]
    public void MapSeriesDetail_WhenTheEpisodeListingIsAbsent_YieldsNoSeasons()
    {
        // Arrange: a panel that answers the detail call without episodes at all. Reported as empty so
        // the caller can say "this series is empty" rather than failing.
        var detail = MapDetail("""{ "info": { "plot": "Something" } }""");

        // Act & Assert
        detail.Seasons.ShouldBeEmpty();
        detail.Plot.ShouldBe("Something");
    }

    [Fact]
    public void MapMovieDetail_TakesTheContainerFromTheMovieDataBlock()
    {
        // Arrange: the one field here that decides whether the film can be opened at all.
        var response = Deserialize<XtreamVodInfoResponseDto>("""
            {
              "info": { "description": "A synopsis.", "cast": "Someone", "duration_secs": 0,
                        "episode_run_time": "95", "rating": "N/A", "releasedate": "2011-03-04" },
              "movie_data": { "stream_id": "42", "container_extension": ".mp4" }
            }
            """);

        // Act
        var detail = XtreamVodMapper.MapMovieDetail(response);

        // Assert
        detail.ContainerExtension.ShouldBe("mp4");
        detail.Plot.ShouldBe("A synopsis.", "the panel spelled it 'description'");
        detail.DurationSeconds.ShouldBe(95 * 60, "seconds were zero, so the stated minutes are used");
        detail.Rating.ShouldBeNull("'N/A' is not a rating");
        detail.Year.ShouldBe(2011);
    }

    [Fact]
    public void MapMovieDetail_WithNeitherBlockPopulated_IsAllAbsent()
    {
        // Arrange: applying a patch of nulls must not blank out what the listing already supplied, which
        // is why every field here is nullable.
        var response = Deserialize<XtreamVodInfoResponseDto>("""{ "info": [], "movie_data": null }""");

        // Act
        var detail = XtreamVodMapper.MapMovieDetail(response);

        // Assert
        detail.ShouldBe(new Core.Content.MovieDetail());
    }

    private static Core.Content.SeriesDetail MapDetail(string json)
    {
        return XtreamVodMapper.MapSeriesDetail(Deserialize<XtreamSeriesInfoResponseDto>(json));
    }

    private static T Deserialize<T>(string json)
        where T : class
    {
        return JsonSerializer.Deserialize<T>(json, XtreamJson.Options)
            ?? throw new InvalidOperationException("The fixture did not deserialise.");
    }

    private static List<T> DeserializeList<T>(string json)
    {
        return JsonSerializer.Deserialize<List<T>>(json, XtreamJson.Options) ?? [];
    }
}
