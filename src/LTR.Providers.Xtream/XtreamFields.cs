using System.Globalization;

namespace LTR.Providers.Xtream;

/// <summary>
/// Reads the individual field values a panel emits, in the forms panels actually emit them.
/// </summary>
/// <remarks>
/// Separate from the JSON converters, which settle what type a token becomes; these settle what a value
/// means. Both the live catalogue and the film and series catalogues need the same answers about images,
/// dates and running times, and the first two copies of the image rule had already drifted apart.
/// </remarks>
internal static class XtreamFields
{
    /// <summary>Years outside this range are a parsing accident rather than a release date.</summary>
    private const int EarliestPlausibleYear = 1870;

    private const int LatestPlausibleYear = 2200;

    /// <summary>
    /// Keeps only image values that are absolute HTTP addresses.
    /// </summary>
    /// <remarks>
    /// Panels put all sorts of things in these fields — empty strings, local file paths, the literal
    /// text "null". Filtering here means the UI never has to guard its image loading.
    /// </remarks>
    public static string? ImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Trims a text field, treating whitespace as absent.</summary>
    public static string? Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Picks the first of two spellings of the same field that is actually populated.
    /// </summary>
    /// <remarks>
    /// Panels disagree about names as well as types: the synopsis is <c>plot</c> on some and
    /// <c>description</c> on others, and a few send both with one of them empty.
    /// </remarks>
    public static string? Either(string? preferred, string? fallback)
    {
        return Text(preferred) ?? Text(fallback);
    }

    /// <summary>
    /// Extracts the release year from whatever a panel calls a release date.
    /// </summary>
    /// <remarks>
    /// The field holds <c>2019</c>, <c>2019-05-01</c>, <c>01.05.2019</c> and <c>0000-00-00</c> in
    /// practice. Only the year is displayed anywhere, so the first four-digit run that could be a year
    /// is taken and the rest is not interpreted at all — which is also why an implausible one is
    /// discarded rather than shown.
    /// </remarks>
    public static int? Year(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return null;
        }

        var span = releaseDate.AsSpan();

        for (var start = 0; start + 4 <= span.Length; start++)
        {
            var candidate = span.Slice(start, 4);

            if (!IsAllDigits(candidate))
            {
                continue;
            }

            // Bounded by a non-digit on both sides, so "20190501" does not yield 2019.
            if ((start > 0 && char.IsAsciiDigit(span[start - 1]))
                || (start + 4 < span.Length && char.IsAsciiDigit(span[start + 4])))
            {
                continue;
            }

            var year = int.Parse(candidate, CultureInfo.InvariantCulture);

            if (year is >= EarliestPlausibleYear and <= LatestPlausibleYear)
            {
                return year;
            }
        }

        return null;
    }

    /// <summary>Converts a Unix timestamp to an instant, treating zero as absent.</summary>
    /// <remarks>
    /// Zero is what panels write for "unknown", and 1 January 1970 is a worse answer than none.
    /// </remarks>
    public static DateTimeOffset? Instant(long? unixSeconds)
    {
        return unixSeconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value) : null;
    }

    /// <summary>
    /// Settles a running time from the two units panels report it in.
    /// </summary>
    /// <remarks>
    /// Seconds win where present, because that figure comes from the panel having opened the file, while
    /// the minute figure is metadata someone typed.
    /// </remarks>
    public static int? DurationSeconds(int? seconds, int? minutes)
    {
        if (seconds is > 0)
        {
            return seconds;
        }

        return minutes is > 0 ? minutes * 60 : null;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
