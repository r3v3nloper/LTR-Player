using LTR.Core.Content;

namespace LTR.Cli;

/// <summary>
/// How a film, a series or a stored position is worded, shared by the four commands that print them.
/// </summary>
/// <remarks>
/// Together in one place because the same figure appears in several listings and a position that reads
/// "at 00:40:00" in one and "2400" in another is how a check stops being a check. <see cref="ConsoleText"/>
/// holds what is not specific to video on demand.
/// </remarks>
internal static class VodText
{
    public static string Kind(ContentKind kind)
    {
        return kind == ContentKind.Movie ? "film" : "episode";
    }

    public static string Entry(ContinueWatchingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.IsNullOrEmpty(entry.Subtitle) ? entry.Title : $"{entry.Title} · {entry.Subtitle}";
    }

    /// <summary>
    /// Says whether a film's detail is stored and, when it is not, when the panel was last asked.
    /// </summary>
    /// <remarks>
    /// The asking is worth printing because it is what decides whether opening the film costs a request:
    /// a panel that answers with nothing is taken at its word for a day. Without this line, "not available"
    /// looks identical whether it has been asked once or on every viewing since the catalogue was imported.
    /// </remarks>
    public static string DetailState(VodItem movie)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (movie.HasDetail)
        {
            return "fetched";
        }

        return movie.DetailAttemptedUtc is { } attempted
            ? $"not available (asked {ConsoleText.FormatUtc(attempted)})"
            : "not available (never asked)";
    }

    public static string Resume(int? resumePositionSeconds, bool isWatched)
    {
        if (resumePositionSeconds is { } seconds)
        {
            return $"at {Duration(seconds)}";
        }

        return isWatched ? "watched" : "-";
    }

    public static string Duration(int? seconds)
    {
        return seconds is > 0 ? Time(TimeSpan.FromSeconds(seconds.Value)) : "-";
    }

    /// <summary>
    /// A moment on a stream, or <c>unknown</c> — which is a result and not a formatting failure: a film
    /// reporting no position cannot be resumed, and that is what the play-test is looking for.
    /// </summary>
    public static string Time(TimeSpan? value)
    {
        return value is { } time ? $"{time:hh\\:mm\\:ss}" : "unknown";
    }
}
