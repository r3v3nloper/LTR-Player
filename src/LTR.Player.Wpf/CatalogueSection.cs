namespace LTR.Player.Wpf;

/// <summary>
/// Which half of the catalogue the left pane is showing.
/// </summary>
/// <remarks>
/// One pane with a selector rather than four panes, because the three lists want the same width and the
/// same search box, and because only one of them can be acted on at a time anyway.
/// </remarks>
public enum CatalogueSection
{
    Live = 0,

    Movies = 1,

    Series = 2,

    /// <summary>What is part-watched, across films and episodes alike.</summary>
    ContinueWatching = 3,
}
