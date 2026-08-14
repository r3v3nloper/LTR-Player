using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging;

namespace LTR.Catalogue;

/// <summary>
/// Remembers where the viewer got to in a film or episode, and writes it down when playback ends.
/// </summary>
/// <remarks>
/// <para>
/// It exists because of one awkward fact: by the time playback has stopped, the engine no longer has a
/// position to report. A recorder that only looked when asked to save would always save nothing. So the
/// position is sampled while playback runs and the last sample is what gets written.
/// </para>
/// <para>
/// In the catalogue layer rather than in the window, although the window is what drives it. Nothing here
/// touches WPF, and the same three steps — follow, sample, write — are what the headless play-test needs to
/// exercise the continue-watching list; it had a second copy of the classification before this moved. A
/// planned web frontend would need the third.
/// </para>
/// <para>
/// Stateful and single-item by design, and registered as a singleton for the same reason
/// <see cref="LTR.Playback.IPlaybackSession"/> is: one stream is open at a time, so one thing is being
/// watched at a time. It is not thread-safe, and does not need to be — it is driven from whichever single
/// thread owns playback.
/// </para>
/// <para>
/// Nothing here is recorded for live television. A channel has no position, no length and nothing to
/// resume, and <see cref="Track"/> is simply not called for one.
/// </para>
/// </remarks>
public sealed class WatchProgressRecorder
{
    private readonly ICatalogueStore _catalogue;
    private readonly ILogger<WatchProgressRecorder> _logger;

    private ContentKind? _kind;
    private int _itemId;
    private TimeSpan _lastPosition;
    private TimeSpan _lastDuration;

    public WatchProgressRecorder(ICatalogueStore catalogue, ILogger<WatchProgressRecorder> logger)
    {
        _catalogue = catalogue;
        _logger = logger;
    }

    /// <summary>Whether something resumable is being followed.</summary>
    public bool IsTracking => _kind is not null;

    /// <summary>
    /// Starts following a film or episode.
    /// </summary>
    /// <param name="startedAt">
    /// Where playback was asked to begin. Kept as the initial sample so that a viewer who resumes at forty
    /// minutes and closes the player before the first sample arrives does not have their place reset to the
    /// beginning — which is what an initial sample of zero would do. It also covers a deep seek that takes
    /// longer than the engine takes to report anything at all.
    /// </param>
    public void Track(ContentKind kind, int itemId, TimeSpan startedAt)
    {
        _kind = kind;
        _itemId = itemId;
        _lastPosition = startedAt;
        _lastDuration = TimeSpan.Zero;
    }

    /// <summary>Stops following, without writing anything.</summary>
    public void Forget()
    {
        _kind = null;
        _itemId = 0;
        _lastPosition = TimeSpan.Zero;
        _lastDuration = TimeSpan.Zero;
    }

    /// <summary>
    /// Takes a sample of where playback has reached.
    /// </summary>
    /// <remarks>
    /// Absent values are ignored rather than recorded as zero. A film reports no position for its first
    /// moments and none at all once it has stopped, and treating either as "back at the beginning" would
    /// throw away the place the viewer actually reached.
    /// </remarks>
    public void Observe(TimeSpan? position, TimeSpan? duration)
    {
        if (position is { } sampled && sampled > TimeSpan.Zero)
        {
            _lastPosition = sampled;
        }

        if (duration is { } length && length > TimeSpan.Zero)
        {
            _lastDuration = length;
        }
    }

    /// <summary>
    /// Writes down what was followed, stops following it, and reports what verdict was reached.
    /// </summary>
    /// <remarks>
    /// Deliberately swallows every failure. This runs while playback is being released — on the way out of
    /// the window, among other times — and a lost resume position is a far smaller matter than a dialog
    /// during shutdown or a provider connection left open because a database write threw.
    /// </remarks>
    /// <returns>
    /// The verdict recorded, or <see langword="null"/> when nothing was being followed. Returned rather than
    /// kept, so a caller that wants to report it — the headless play-test does — need not ask twice.
    /// </returns>
    public async Task<WatchOutcome?> RecordAsync(CancellationToken cancellationToken)
    {
        if (_kind is not { } kind)
        {
            return null;
        }

        var itemId = _itemId;
        var position = _lastPosition;
        var outcome = ResumePolicy.Classify(position, _lastDuration);

        Forget();

        try
        {
            if (kind == ContentKind.Movie)
            {
                await _catalogue.RecordMovieProgressAsync(itemId, outcome, position, cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                await _catalogue.RecordEpisodeProgressAsync(itemId, outcome, position, cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            CatalogueLog.ProgressNotRecorded(_logger, exception, kind.ToString(), itemId);
        }

        return outcome;
    }
}
