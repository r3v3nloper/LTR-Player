using System.Globalization;

namespace LTR.Core.Content;

/// <summary>
/// States how an episode is labelled, in one place.
/// </summary>
/// <remarks>
/// The label appears in the episode list, on the continue-watching row and in the on-screen display, and
/// the three were spelling it differently the moment they were written separately.
/// </remarks>
public static class EpisodeNaming
{
    /// <summary>
    /// The conventional short form, such as <c>S02E05</c>.
    /// </summary>
    /// <remarks>
    /// Padded to two digits because that is what viewers are used to seeing, and invariant so it reads
    /// the same whatever locale the machine runs under.
    /// </remarks>
    public static string Label(int seasonNumber, int episodeNumber)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"S{seasonNumber:00}E{episodeNumber:00}");
    }

    /// <summary>
    /// The label followed by the episode's own title, where it has one that adds anything.
    /// </summary>
    /// <remarks>
    /// Providers frequently set the title to the label, to the series name, or to nothing at all, and
    /// repeating it would read as "S02E05 — S02E05".
    /// </remarks>
    public static string Describe(int seasonNumber, int episodeNumber, string? title)
    {
        var label = Label(seasonNumber, episodeNumber);

        if (string.IsNullOrWhiteSpace(title))
        {
            return label;
        }

        var trimmed = title.Trim();

        return trimmed.Equals(label, StringComparison.OrdinalIgnoreCase) ? label : $"{label} · {trimmed}";
    }
}
