namespace LTR.Core.Playback;

/// <summary>
/// What should become of a film or episode that stopped playing.
/// </summary>
public enum WatchOutcome
{
    /// <summary>Too little was watched to be worth remembering, and any stored position is cleared.</summary>
    Discard = 0,

    /// <summary>Part-watched: the position is stored and the item offers to resume.</summary>
    Resumable = 1,

    /// <summary>Watched to the end, which clears the position and takes it off the list.</summary>
    Finished = 2,
}
