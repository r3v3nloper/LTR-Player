using LTR.Catalogue;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;

namespace LTR.Cli;

/// <summary>
/// Opens one stream, holds it, releases it and reports what happened — for a channel and for a film alike.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is the same for both and the release is the reason either command exists, so it is stated
/// once here. Both had their own copy, and the copies had drifted apart in both directions: the live one
/// printed state transitions and never asked the panel why a stream failed to start, while the film one
/// asked but printed no transitions and never listed the tracks it found. Neither difference was a decision.
/// </para>
/// <para>
/// What genuinely differs is what happens while the stream is held — the film play-test seeks, reads the
/// position and records progress — and that arrives as a callback rather than as flags.
/// </para>
/// </remarks>
internal sealed class StreamHoldTest
{
    private readonly IPlaybackSession _session;
    private readonly IPlaybackTransport _playback;
    private readonly IStreamFailureExplainer _failures;
    private readonly ConnectionReleaseCheck _releaseCheck;

    public StreamHoldTest(
        IPlaybackSession session,
        IPlaybackTransport playback,
        IStreamFailureExplainer failures,
        ConnectionReleaseCheck releaseCheck)
    {
        _session = session;
        _playback = playback;
        _failures = failures;
        _releaseCheck = releaseCheck;
    }

    /// <param name="whileHolding">
    /// Run once the stream is playing and the hold has elapsed, before it is released. Anything that needs a
    /// live stream to be meaningful belongs here, because afterwards the engine reports nothing.
    /// </param>
    /// <returns>A process exit code: zero when the stream played and was released.</returns>
    public async Task<int> RunAsync(
        PlaylistSource source,
        MediaRequest request,
        int seconds,
        Func<CancellationToken, Task>? whileHolding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        Console.WriteLine($"Opening '{request.DisplayName}' as {request.Format}...");

        if (request.StartAt is { } startAt)
        {
            Console.WriteLine($"Starting at {startAt:hh\\:mm\\:ss}.");
        }

        _session.StateChanged += OnStateChanged;

        try
        {
            var state = await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(false);

            if (state != PlaybackState.Playing)
            {
                Console.Error.WriteLine($"Playback did not start; final state was {state}.");
                await ReportReasonAsync(source, cancellationToken).ConfigureAwait(false);

                return 1;
            }

            Console.WriteLine($"Playing. Holding the stream for {seconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

            ReportTracks();

            if (whileHolding is not null)
            {
                await whileHolding(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (PlaybackFailedException exception)
        {
            // Caught here rather than left to the runner, which has no source to ask about. The panel is the
            // only thing that knows whether this was the stream, the connection limit or the subscription.
            Console.Error.WriteLine($"Playback error: {exception.Message}");
            await ReportReasonAsync(source, cancellationToken).ConfigureAwait(false);

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

    /// <summary>Asks the provider why a stream would not open, and says so.</summary>
    private async Task ReportReasonAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        var reason = await _failures.ExplainAsync(source, cancellationToken).ConfigureAwait(false);

        Console.Error.WriteLine($"Reason:  {reason}");
        Console.Error.WriteLine($"         {StreamFailureNotes.Describe(reason)}");
    }

    private void ReportTracks()
    {
        // MPEG-TS announces its tracks only as they are encountered, so this is meaningful just once
        // playback is actually running.
        foreach (var kind in new[] { MediaTrackKind.Video, MediaTrackKind.Audio, MediaTrackKind.Subtitle })
        {
            var tracks = _playback.GetTracks(kind);

            Console.WriteLine(
                tracks.Count == 0
                    ? $"  {kind}: none reported"
                    : $"  {kind}: {string.Join(", ", tracks.Select(track => track.DisplayLabel))}");
        }
    }
}
