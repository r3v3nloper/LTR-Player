using System.IO;
using System.Text;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;
using LTR.TestSupport;

namespace LTR.Catalogue;

/// <summary>
/// Stands in for the provider layer, and records what the import asked it for.
/// </summary>
/// <remarks>
/// The provider boundary is faked rather than the database: the behaviour under test is the order of the
/// import sequence and what it stores, and both are only meaningful against a real store.
/// </remarks>
internal sealed class FakeProviderRegistry
    : NotSupportedProviderRegistry, IContentProvider, IProviderCapabilityProbe, IGuideSource
{
    private readonly List<string> _calls = [];

    public FakeProviderRegistry(PlaylistSource source)
    {
        Source = source;
    }

    public PlaylistSource Source { get; }

    /// <summary>Calls received, in order, so the sequence itself can be asserted.</summary>
    public IReadOnlyList<string> Calls => _calls;

    public ProviderAccount Account { get; set; } = new(
        AccountStatus.Active,
        ExpiresAtUtc: null,
        IsTrial: false,
        MaxConnections: 1,
        ActiveConnections: 0,
        AllowedFormats: [StreamFormat.MpegTs]);

    public List<Category> Categories { get; } = [];

    public List<Channel> Channels { get; } = [];

    public List<VodItem> Movies { get; } = [];

    public List<Series> Series { get; } = [];

    /// <summary>Seasons and episodes handed back for any series asked about.</summary>
    public SeriesDetail? SeriesDetail { get; set; }

    public MovieDetail? MovieDetail { get; set; }

    public ProviderCapabilities Capabilities { get; set; } = new() { SupportsLive = true };

    public override IContentProvider CreateProvider(PlaylistSource source)
    {
        return this;
    }

    public override IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source)
    {
        return this;
    }

    /// <summary>
    /// The XMLTV document this source serves, or <see langword="null"/> for a source that has no guide.
    /// </summary>
    public string? GuideDocument { get; set; }

    public override IGuideSource GetGuideSource(PlaylistSource source)
    {
        return this;
    }

    public async Task<bool> TryReadGuideAsync(
        PlaylistSource source,
        Func<Stream, CancellationToken, Task> read,
        CancellationToken cancellationToken)
    {
        _calls.Add("guide");

        if (GuideDocument is null)
        {
            return false;
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(GuideDocument));
        await read(stream, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>When set, <see cref="AuthenticateAsync"/> throws it instead of answering.</summary>
    public Exception? AuthenticateException { get; set; }

    public Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken)
    {
        _calls.Add("authenticate");

        return AuthenticateException is not null
            ? Task.FromException<ProviderAccount>(AuthenticateException)
            : Task.FromResult(Account);
    }

    public bool Supports(PlaylistSource source)
    {
        return true;
    }

    public Task<ProviderCapabilities> ProbeAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        _calls.Add("probe");
        return Task.FromResult(Capabilities);
    }

    /// <summary>When set, every detail fetch fails, standing in for a panel that cannot be reached.</summary>
    public bool DetailFetchFails { get; set; }

    public Task<IReadOnlyList<Category>> FetchCategoriesAsync(
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        // Recorded with its kind: an import asks for three of them, and which kinds it asked for is
        // exactly what the capability guard is supposed to decide.
        _calls.Add($"categories:{kind}");

        return Task.FromResult<IReadOnlyList<Category>>(
            [.. Categories.Where(category => category.Kind == kind)]);
    }

    public Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken)
    {
        _calls.Add("channels");
        return Task.FromResult<IReadOnlyList<Channel>>(Channels);
    }

    public Task<IReadOnlyList<VodItem>> FetchMoviesAsync(CancellationToken cancellationToken)
    {
        _calls.Add("movies");
        return Task.FromResult<IReadOnlyList<VodItem>>(Movies);
    }

    public Task<IReadOnlyList<Series>> FetchSeriesAsync(CancellationToken cancellationToken)
    {
        _calls.Add("series");
        return Task.FromResult<IReadOnlyList<Series>>(Series);
    }

    public Task<MovieDetail?> FetchMovieDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        _calls.Add($"movie-detail:{externalId}");

        return DetailFetchFails
            ? Task.FromException<MovieDetail?>(new HttpRequestException("The panel is unreachable."))
            : Task.FromResult(MovieDetail);
    }

    public Task<SeriesDetail?> FetchSeriesDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        _calls.Add($"series-detail:{externalId}");

        return DetailFetchFails
            ? Task.FromException<SeriesDetail?>(new HttpRequestException("The panel is unreachable."))
            : Task.FromResult(SeriesDetail);
    }
}
