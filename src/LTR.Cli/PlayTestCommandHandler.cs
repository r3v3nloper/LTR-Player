using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Playback;
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
    private readonly IEnumerable<IStreamUrlResolver> _resolvers;
    private readonly IPlaybackSession _session;
    private readonly IMediaEngine _engine;
    private readonly IContentProviderFactory _providerFactory;

    public PlayTestCommandHandler(
        IEnumerable<IStreamUrlResolver> resolvers,
        IPlaybackSession session,
        IMediaEngine engine,
        IContentProviderFactory providerFactory)
    {
        _resolvers = resolvers;
        _session = session;
        _engine = engine;
        _providerFactory = providerFactory;
    }

    public async Task<int> ExecuteAsync(
        XtreamSource source,
        string streamId,
        int seconds,
        CancellationToken cancellationToken)
    {
        var resolver = _resolvers.FirstOrDefault(candidate => candidate.Supports(source));

        if (resolver is null)
        {
            Console.Error.WriteLine("No resolver handles this source type.");
            return 1;
        }

        var channel = new Channel
        {
            SourceId = source.Id,
            ExternalId = streamId,
            Name = $"stream {streamId}",
        };

        var request = resolver.ResolveLive(source, channel);

        _session.StateChanged += OnStateChanged;

        try
        {
            Console.WriteLine($"Opening stream {streamId} as {request.Format}...");

            var state = await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(false);

            if (state != PlaybackState.Playing)
            {
                Console.Error.WriteLine($"Playback did not start; final state was {state}.");
                return 1;
            }

            Console.WriteLine($"Playing. Holding the stream for {seconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

            ReportTracks();
        }
        finally
        {
            _session.StateChanged -= OnStateChanged;

            // Not passed the caller's token: releasing must happen even when the run was interrupted.
            await _session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("Stream released.");
        await ReportRemainingConnectionsAsync(source, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Console.WriteLine($"  state: {e.Previous} -> {e.Current}{DescribeMessage(e.Message)}");
    }

    private static string DescribeMessage(string? message)
    {
        return string.IsNullOrWhiteSpace(message) ? string.Empty : $" ({message})";
    }

    private void ReportTracks()
    {
        // MPEG-TS announces its tracks only as they are encountered, so this is meaningful just once
        // playback is actually running.
        foreach (var kind in new[] { MediaTrackKind.Video, MediaTrackKind.Audio, MediaTrackKind.Subtitle })
        {
            var tracks = _engine.GetTracks(kind);

            Console.WriteLine(
                tracks.Count == 0
                    ? $"  {kind}: none reported"
                    : $"  {kind}: {string.Join(", ", tracks.Select(track => track.DisplayLabel))}");
        }
    }

    /// <summary>
    /// Asks the panel how many connections it still counts as open.
    /// </summary>
    /// <remarks>
    /// This is the actual proof of correct teardown. A non-zero count moments after releasing means
    /// the connection leaked, which is what eventually locks the account out.
    /// </remarks>
    private async Task ReportRemainingConnectionsAsync(XtreamSource source, CancellationToken cancellationToken)
    {
        var provider = _providerFactory.Create(source);
        var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            account.ActiveConnections == 0
                ? "The panel reports no open connections. Teardown is clean."
                : $"Warning: the panel still counts {account.ActiveConnections} open connection(s). "
                    + "Providers often need a moment to notice; re-run probe to confirm.");
    }
}
