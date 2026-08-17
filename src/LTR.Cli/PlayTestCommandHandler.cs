using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Opens a channel headlessly for a few seconds, then releases it and reports what happened.
/// </summary>
/// <remarks>
/// The point is to prove the whole chain end to end without a UI — URL construction, LibVLC, track
/// discovery and, above all, that the connection is handed back. Verifying the release is the reason
/// this command exists: everything else can be checked by pasting a URL into VLC.
/// </remarks>
internal sealed class PlayTestCommandHandler
{
    private readonly IProviderRegistry _providers;
    private readonly StreamHoldTest _holdTest;

    public PlayTestCommandHandler(IProviderRegistry providers, StreamHoldTest holdTest)
    {
        _providers = providers;
        _holdTest = holdTest;
    }

    /// <remarks>
    /// Nothing happens while the stream is held beyond what every hold reports, so no callback is passed:
    /// a channel has no position worth reading and nothing to seek to.
    /// </remarks>
    public Task<int> ExecuteAsync(
        XtreamSource source,
        string streamId,
        int seconds,
        CancellationToken cancellationToken)
    {
        var resolver = _providers.GetStreamUrlResolver(source);

        var channel = new Channel
        {
            SourceId = source.Id,
            ExternalId = streamId,
            Name = $"stream {streamId}",
        };

        var request = resolver.ResolveLive(source, channel);

        return _holdTest.RunAsync(source, request, seconds, whileHolding: null, cancellationToken);
    }
}
