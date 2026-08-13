using System.Globalization;

namespace LTR.Epg.Xmltv;

/// <summary>
/// Reads XMLTV's date format.
/// </summary>
/// <remarks>
/// <para>
/// The format is <c>YYYYMMDDhhmmss ZZZZZ</c>, and every part after the year may be missing. Real guides
/// use <c>20260812183000 +0200</c>, <c>20260812183000</c>, <c>20260812183000 +0000</c> and
/// occasionally <c>+0200</c> with no separating space. All of those have to read correctly, because a
/// two-hour error puts the whole guide on the wrong programme.
/// </para>
/// <para>
/// A timestamp with no offset is taken as UTC. The specification calls it the sender's local time, but
/// the sender is unknown here and unknowable from the document, and a wrong guess about the machine's
/// own zone would shift the guide by an unpredictable amount instead of a stated one.
/// </para>
/// </remarks>
internal static class XmltvTimestamp
{
    /// <summary>
    /// Accepted date parts, longest first, so a full timestamp is never matched by a shorter prefix.
    /// </summary>
    private static readonly string[] DateFormats =
    [
        "yyyyMMddHHmmss",
        "yyyyMMddHHmm",
        "yyyyMMddHH",
        "yyyyMMdd",
    ];

    public static bool TryParse(string? value, out DateTimeOffset instantUtc)
    {
        instantUtc = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var (datePart, offsetPart) = Split(value.Trim());

        if (!DateTime.TryParseExact(
                datePart,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            return false;
        }

        if (!TryReadOffset(offsetPart, out var offset))
        {
            return false;
        }

        instantUtc = new DateTimeOffset(localTime, offset).ToUniversalTime();
        return true;
    }

    /// <summary>
    /// Separates the date digits from the zone that may follow them, with or without a space.
    /// </summary>
    private static (string DatePart, string OffsetPart) Split(string value)
    {
        var digits = 0;

        while (digits < value.Length && char.IsAsciiDigit(value[digits]))
        {
            digits++;
        }

        return (value[..digits], value[digits..].Trim());
    }

    private static bool TryReadOffset(string offsetPart, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        if (offsetPart.Length == 0 || offsetPart.Equals("Z", StringComparison.OrdinalIgnoreCase)
            || offsetPart.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || offsetPart.Equals("GMT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sign = offsetPart[0] switch
        {
            '+' => 1,
            '-' => -1,
            _ => 0,
        };

        if (sign == 0)
        {
            return false;
        }

        var magnitude = offsetPart[1..].Replace(":", string.Empty, StringComparison.Ordinal);

        if (magnitude.Length != 4
            || !int.TryParse(magnitude[..2], CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(magnitude[2..], CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        var candidate = sign * new TimeSpan(hours, minutes, 0);

        // DateTimeOffset rejects anything beyond ±14 hours, and a guide stating one is corrupt rather
        // than unusual. Reported as unparseable so the entry is skipped instead of throwing mid-import.
        if (candidate < TimeSpan.FromHours(-14) || candidate > TimeSpan.FromHours(14))
        {
            return false;
        }

        offset = candidate;
        return true;
    }
}
