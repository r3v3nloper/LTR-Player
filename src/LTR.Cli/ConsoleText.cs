using System.Globalization;

namespace LTR.Cli;

/// <summary>
/// Formatting shared by the commands that print tables.
/// </summary>
/// <remarks>
/// Three handlers had grown their own identical <c>Truncate</c> and two had near-identical timestamp
/// formatting, which is the third repetition §2.16 asks for. Invariant culture throughout, so output can be
/// compared between machines and pasted into a report.
/// </remarks>
internal static class ConsoleText
{
    private const char Ellipsis = '…';

    /// <summary>
    /// Shortens a value to fit a column, marking that something was cut.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength - 1), stackalloc[] { Ellipsis });
    }

    /// <summary>
    /// Renders an instant in UTC, or reports that there is none.
    /// </summary>
    /// <remarks>
    /// The zone is spelled out because every timestamp the player stores is UTC while every timestamp it
    /// shows in the window is local, and a bare figure on a console leaves the reader guessing which.
    /// </remarks>
    public static string FormatUtc(DateTimeOffset? instant)
    {
        return instant is null
            ? "never"
            : instant.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    /// <summary>Renders a time of day in UTC, for a column where the date is already established.</summary>
    public static string FormatUtcTimeOfDay(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
