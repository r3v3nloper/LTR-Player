namespace LTR.Player.Wpf;

/// <summary>
/// A labelled tick across the top of the timeline.
/// </summary>
/// <param name="Position">Pixels from the left edge of the window.</param>
/// <param name="Label">The time, in the viewer's own zone.</param>
public sealed record GuideTimeMarker(double Position, string Label);
