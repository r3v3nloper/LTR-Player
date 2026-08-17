using System.Globalization;

namespace LTR.Player.Wpf;

/// <summary>
/// Builds the one-line summary under a film's or a series' name.
/// </summary>
/// <remarks>
/// Shared by both rows rather than written twice, because the interesting part is not the concatenation
/// but the rules: each field appears only where the provider stated it, a rating of zero counts as not
/// stated — panels write one for everything they have no rating for — and the order is fixed so that two
/// lists side by side read the same way.
/// </remarks>
internal static class CatalogueDetailLine
{
    private const string Separator = " · ";

    /// <summary>
    /// One line of whatever was stated. A series passes no <paramref name="duration"/>: it has no single
    /// running time, and the seasons it does have are not known until it is opened.
    /// </summary>
    public static string Build(int? year, double? rating, string? genre, TimeSpan? duration = null)
    {
        return string.Join(Separator, Parts(year, rating, genre, duration));
    }

    private static IEnumerable<string> Parts(int? year, double? rating, string? genre, TimeSpan? duration)
    {
        if (year is { } stated)
        {
            yield return stated.ToString(CultureInfo.CurrentCulture);
        }

        if (duration is { } running)
        {
            yield return DurationText.Format(running);
        }

        if (rating is { } score and > 0)
        {
            yield return score.ToString("0.#", CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            yield return genre;
        }
    }
}
