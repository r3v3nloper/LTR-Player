namespace LTR.Player.Wpf;

/// <summary>
/// The stretch of time the guide is drawing, and the arithmetic that turns an instant into a position.
/// </summary>
/// <remarks>
/// <para>
/// Its own type because every row, every programme block and every hour marker needs the same conversion,
/// and having each work it out from a start time and a scale factor is how a timeline ends up
/// half a pixel out of alignment with its own header.
/// </para>
/// <para>
/// Immutable: moving the window produces a new one, which is what makes a redraw a matter of rebuilding
/// the rows rather than mutating them in place.
/// </para>
/// </remarks>
/// <param name="StartUtc">Left edge of the window.</param>
/// <param name="Duration">How much time the window spans.</param>
/// <param name="PixelsPerHour">
/// Horizontal scale. Fixed rather than derived from the available width, so a programme block stays the
/// same size when the panel is resized and the window keeps meaning the same amount of time.
/// </param>
public sealed record GuideTimeline(DateTimeOffset StartUtc, TimeSpan Duration, double PixelsPerHour)
{
    /// <summary>
    /// Four hours at a scale where a half-hour programme is still wide enough to read its title.
    /// </summary>
    public static GuideTimeline Default { get; } =
        new(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(4), PixelsPerHour: 260);

    public DateTimeOffset EndUtc => StartUtc + Duration;

    public double Width => Duration.TotalHours * PixelsPerHour;

    /// <summary>
    /// Places the window so that <paramref name="instant"/> is visible, aligned to the half hour before it.
    /// </summary>
    /// <remarks>
    /// Aligned so the hour markers land on round times. A window starting at 18:07 would label its
    /// columns 18:07, 18:37, 19:07 — technically correct and unreadable.
    /// </remarks>
    public GuideTimeline StartingAt(DateTimeOffset instant)
    {
        var aligned = new DateTimeOffset(
            instant.Year,
            instant.Month,
            instant.Day,
            instant.Hour,
            instant.Minute < 30 ? 0 : 30,
            0,
            instant.Offset);

        return this with { StartUtc = aligned };
    }

    public GuideTimeline ShiftedBy(TimeSpan offset)
    {
        return this with { StartUtc = StartUtc + offset };
    }

    /// <summary>Where an instant falls, in pixels from the left edge.</summary>
    public double PositionOf(DateTimeOffset instant)
    {
        return (instant - StartUtc).TotalHours * PixelsPerHour;
    }

    /// <summary>
    /// The part of a programme that falls inside the window, as a left edge and a width.
    /// </summary>
    /// <remarks>
    /// Clipped to the window rather than left to overflow. The programme running when the window opens
    /// started before it, and drawing it at a negative offset would push its title off the left edge
    /// where the one thing the user wants to read is what is on now.
    /// </remarks>
    public (double Left, double Width) Clip(DateTimeOffset startUtc, DateTimeOffset stopUtc)
    {
        var from = startUtc < StartUtc ? StartUtc : startUtc;
        var to = stopUtc > EndUtc ? EndUtc : stopUtc;

        var left = PositionOf(from);
        var width = PositionOf(to) - left;

        return (left, Math.Max(width, 0));
    }
}
