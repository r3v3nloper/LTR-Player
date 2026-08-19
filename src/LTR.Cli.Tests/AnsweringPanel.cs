using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers;
using LTR.TestSupport;

namespace LTR.Cli;

/// <summary>
/// A panel that answers a scripted series of connection counts and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Written for the release check, which asks one question repeatedly and decides from the answers. The script
/// is what makes the interesting cases reachable at all: a panel that reports the connection gone on the third
/// ask rather than the first is the ordinary case — panels notice on their own schedule — and it is
/// indistinguishable from a leak unless something tries it.
/// </para>
/// <para>
/// Local to this project rather than in <c>TestSupport</c>, on the rule that review already applied to the
/// source builder: it moves when a second project needs it. The catalogue tests' own provider fake answers
/// every member and records the sequence, which is a different thing from this.
/// </para>
/// </remarks>
internal sealed class AnsweringPanel : NotSupportedProviderRegistry, IContentProvider
{
    private readonly Queue<ProviderAccount> _answers;

    public AnsweringPanel(params ProviderAccount[] answers)
    {
        _answers = new Queue<ProviderAccount>(answers);
    }

    /// <summary>How many times the panel was asked, so an early answer can be shown to stop the asking.</summary>
    public int AskCount { get; private set; }

    /// <summary>The source last asked for, which is also what the real provider is bound to.</summary>
    public PlaylistSource Source { get; private set; } = null!;

    public override IContentProvider CreateProvider(PlaylistSource source)
    {
        Source = source;
        return this;
    }

    /// <remarks>
    /// The last scripted answer stands for every ask after it, so a test that means "still counted
    /// throughout" states one answer rather than five — and a test that runs off the end of a deliberate
    /// script fails on the assertion rather than on a queue.
    /// </remarks>
    public Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken)
    {
        AskCount++;

        var answer = _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();

        return Task.FromResult(answer);
    }

    /// <summary>An account the panel reports as healthy, counting <paramref name="active"/> connections.</summary>
    public static ProviderAccount Counting(int active, int maxConnections = 1)
    {
        return new ProviderAccount(
            AccountStatus.Active,
            ExpiresAtUtc: null,
            IsTrial: false,
            maxConnections,
            active,
            AllowedFormats: [StreamFormat.MpegTs]);
    }

    /// <summary>
    /// An account reporting no limit and no usage, which is what a panel that does not count them looks like.
    /// </summary>
    public static ProviderAccount CountingNothing()
    {
        return Counting(active: 0, maxConnections: 0);
    }

    public Task<IReadOnlyList<Category>> FetchCategoriesAsync(
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    public Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    public Task<IReadOnlyList<VodItem>> FetchMoviesAsync(CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    public Task<IReadOnlyList<Series>> FetchSeriesAsync(CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    public Task<MovieDetail?> FetchMovieDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    public Task<SeriesDetail?> FetchSeriesDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        throw NotAsked();
    }

    private static NotSupportedException NotAsked()
    {
        return new NotSupportedException(
            "The release check only authenticates; anything else reaching this panel is a fact about the "
            + "test rather than about the check.");
    }
}
