namespace LTR.Cli.Commands;

/// <summary>
/// Figures shared by more than one command, so two listings cannot disagree about them.
/// </summary>
internal static class CommandDefaults
{
    /// <summary>How many entries a listing prints when no limit is given.</summary>
    public const int Limit = 40;

    /// <summary>
    /// How long a play-test holds a stream open.
    /// </summary>
    /// <remarks>
    /// Long enough for MPEG-TS to announce its tracks, short enough not to sit on a connection a
    /// one-connection subscription needs back.
    /// </remarks>
    public const int HoldSeconds = 5;
}
