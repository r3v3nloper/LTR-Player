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
    /// <summary>How many times to ask the panel whether the connection has been released.</summary>
    private const int ConnectionCheckAttempts = 5;

    private static readonly TimeSpan ConnectionCheckDelay = TimeSpan.FromSeconds(5);

    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly IMediaEngine _engine;

    public PlayTestCommandHandler(
        IProviderRegistry providers,
        IPlaybackSession session,
        IMediaEngine engine)
    {
        _providers = providers;
        _session = session;
        _engine = engine;
    }

    public async Task<int> ExecuteAsync(
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
    /// Waits for the panel to report the connection as closed, and says plainly whether it did.
    /// </summary>
    /// <remarks>
    /// This is the actual proof of correct teardown. It polls rather than asking once, because panels
    /// track connections on their own schedule and take seconds to notice a client has gone. Reading
    /// that lag as a leak would condemn correct code; not distinguishing the two at all would leave
    /// the only question that matters unanswered.
    /// </remarks>
    private async Task ReportRemainingConnectionsAsync(XtreamSource source, CancellationToken cancellationToken)
    {
        var provider = _providers.CreateProvider(source);

        for (var attempt = 1; attempt <= ConnectionCheckAttempts; attempt++)
        {
            var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

            if (account.ActiveConnections == 0)
            {
                Console.WriteLine(
                    attempt == 1
                        ? "The panel reports no open connections. Teardown is clean."
                        : $"The panel reports no open connections after {attempt} checks. Teardown is "
                            + "clean; the panel simply needed a moment to notice.");

                return;
            }

            Console.WriteLine(
                $"  check {attempt}/{ConnectionCheckAttempts}: the panel still counts "
                + $"{account.ActiveConnections} open connection(s).");

            if (attempt < ConnectionCheckAttempts)
            {
                await Task.Delay(ConnectionCheckDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "The connection was still counted as open throughout. Either this player leaked it, or "
            + "another device is using the subscription. Check that nothing else is streaming, then "
            + "re-run probe.");
    }
}
