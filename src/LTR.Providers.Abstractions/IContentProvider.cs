using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Talks to one configured source and returns its catalogue.
/// </summary>
/// <remarks>
/// An instance is bound to a single <see cref="PlaylistSource"/>, so returned entities already
/// carry the correct <c>SourceId</c>. They are otherwise unsaved: identifiers stay zero until the
/// persistence layer reconciles them against what is already stored.
/// </remarks>
public interface IContentProvider
{
    PlaylistSource Source { get; }

    /// <summary>
    /// Verifies the credentials and reports the subscription's live state, including the
    /// connection limit that governs how many streams may be opened.
    /// </summary>
    Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> FetchCategoriesAsync(ContentKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The film catalogue, or an empty list for a source that offers none.
    /// </summary>
    /// <remarks>
    /// Empty rather than unsupported, in the same spirit as <see cref="FetchCategoriesAsync"/> answering
    /// a kind the source has no notion of: a playlist is a flat list of live entries, and "there are no
    /// films" is the truthful answer rather than an error the caller has to be ready for.
    /// </remarks>
    Task<IReadOnlyList<VodItem>> FetchMoviesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The series catalogue, without seasons or episodes.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow. A subscription lists thousands of series and each one's seasons need a call
    /// of its own, so an import that fetched them all would take hours; <see cref="FetchSeriesDetailAsync"/>
    /// is made when a series is actually opened.
    /// </remarks>
    Task<IReadOnlyList<Series>> FetchSeriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Extended information about one film, or <see langword="null"/> when the source has none.
    /// </summary>
    /// <param name="externalId">The film's identifier within this source.</param>
    Task<MovieDetail?> FetchMovieDetailAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>
    /// One series' seasons and episodes, or <see langword="null"/> when the source has none.
    /// </summary>
    /// <param name="externalId">The series' identifier within this source.</param>
    Task<SeriesDetail?> FetchSeriesDetailAsync(string externalId, CancellationToken cancellationToken);
}
