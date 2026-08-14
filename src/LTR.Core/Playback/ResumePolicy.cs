namespace LTR.Core.Playback;

/// <summary>
/// Decides what a stopped film or episode should be remembered as, and where resuming it starts.
/// </summary>
/// <remarks>
/// Stated in the core, free of any engine or storage concern, because these thresholds are the whole of
/// "continue watching" as a feature and they are what a test can pin down. Both applications and the
/// planned web frontend need the same answers.
/// </remarks>
public static class ResumePolicy
{
    /// <summary>
    /// Below this, nothing is remembered.
    /// </summary>
    /// <remarks>
    /// Someone who opens a film and leaves within a minute did not start watching it, and putting it on
    /// the continue-watching list would fill that list with things nobody intends to return to.
    /// </remarks>
    public static readonly TimeSpan MinimumWatched = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How much may be left unwatched and still count as finished.
    /// </summary>
    /// <remarks>
    /// Closing during the credits is finishing it. Without this the item would come back offering to
    /// resume two minutes before its own end.
    /// </remarks>
    public static readonly TimeSpan FinishedTail = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The same judgement as a fraction, for items short enough that a two-minute tail is most of them.
    /// </summary>
    public const double FinishedFraction = 0.98;

    /// <summary>
    /// How far back resuming starts, so the viewer gets a moment of context rather than a cut mid-word.
    /// </summary>
    public static readonly TimeSpan RewindOnResume = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Classifies a position reached within an item of the given length.
    /// </summary>
    /// <param name="duration">
    /// The engine's reported length, which is the reliable one — a provider's stated running time is
    /// frequently absent or wrong. <see cref="TimeSpan.Zero"/> or less means unknown, in which case the
    /// end cannot be recognised and the position is simply remembered.
    /// </param>
    public static WatchOutcome Classify(TimeSpan position, TimeSpan duration)
    {
        if (position < MinimumWatched)
        {
            return WatchOutcome.Discard;
        }

        if (duration <= TimeSpan.Zero)
        {
            return WatchOutcome.Resumable;
        }

        if (position >= duration - FinishedTail || position >= duration * FinishedFraction)
        {
            return WatchOutcome.Finished;
        }

        return WatchOutcome.Resumable;
    }

    /// <summary>
    /// Where playback should actually start, given a remembered position.
    /// </summary>
    public static TimeSpan StartFrom(TimeSpan storedPosition)
    {
        var rewound = storedPosition - RewindOnResume;
        return rewound > TimeSpan.Zero ? rewound : TimeSpan.Zero;
    }
}
