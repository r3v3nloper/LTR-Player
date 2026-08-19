using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// What the transport's previous and next act on.
/// </summary>
/// <remarks>
/// <para>
/// Recorded by <see cref="PlaybackCoordinator"/> as it opens a stream, not read back from the engine, and for
/// two reasons. A stream is opened asynchronously, so an engine asked what is playing answers with the previous
/// item for as long as the next one takes to arrive — three quick presses of next would then all land on the
/// same episode. And an open that failed still leaves the viewer in the middle of that episode.
/// </para>
/// <para>
/// <see cref="Episode"/> is set for a series and null otherwise, which is what makes
/// <see cref="ContentKind.Series"/> answerable without loading anything: the episode is the only one of the
/// three kinds whose neighbour cannot be found from a list already on screen.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the three catalogues the item came from.</param>
/// <param name="Episode">The episode itself, for a series; null for a channel or a film.</param>
public sealed record PlayingItem(ContentKind Kind, Episode? Episode = null);
