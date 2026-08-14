using LTR.Catalogue;
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
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly IStreamFailureExplainer _failures;
    private readonly ConnectionReleaseCheck _releaseCheck;

    public PlayTestCommandHandler(
        IProviderRegistry providers,
        IPlaybackSession session,
        IStreamFailureExplainer failures,
        ConnectionReleaseCheck releaseCheck)
    {
        _providers = providers;
        _session = session;
        _failures = failures;
        _releaseCheck = releaseCheck;
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
        catch (PlaybackFailedException exception)
        {
            // Caught here rather than left to the runner, which has no source to ask about. The panel is the
            // only thing that knows whether this was the channel, the connection limit or the subscription.
            Console.Error.WriteLine($"Playback error: {exception.Message}");

            var reason = await _failures.ExplainAsync(source, cancellationToken).ConfigureAwait(false);

            Console.Error.WriteLine($"Reason:  {reason}");
            Console.Error.WriteLine($"         {StreamFailureNotes.Describe(reason)}");

            return 1;
        }
        finally
        {
            _session.StateChanged -= OnStateChanged;

            // Not passed the caller's token: releasing must happen even when the run was interrupted.
            await _session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("Stream released.");
        await _releaseCheck.ReportAsync(source, cancellationToken).ConfigureAwait(false);

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
            var tracks = _session.GetTracks(kind);

            Console.WriteLine(
                tracks.Count == 0
                    ? $"  {kind}: none reported"
                    : $"  {kind}: {string.Join(", ", tracks.Select(track => track.DisplayLabel))}");
        }
    }
}
