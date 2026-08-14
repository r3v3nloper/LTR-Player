namespace LTR.Player.Wpf;

/// <summary>
/// Renders a running time the way a viewer reads one.
/// </summary>
/// <remarks>
/// Films, episodes and resume positions all show a duration, and the three had spelled it differently the
/// moment they were written separately. Hours are omitted below an hour, because "0:07:30" for a
/// seven-minute extra reads as a fault.
/// </remarks>
internal static class DurationText
{
    public static string Format(TimeSpan value)
    {
        var rounded = TimeSpan.FromSeconds(Math.Floor(value.TotalSeconds));

        return rounded < TimeSpan.FromHours(1)
            ? rounded.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : rounded.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
