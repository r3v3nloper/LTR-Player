namespace LTR.Core.Content;

/// <summary>
/// The criteria narrowing a channel list, applied together.
/// </summary>
/// <param name="SearchText">
/// Matched against the channel name, case-insensitively and anywhere in the name. Ignored when blank.
/// </param>
/// <param name="CategoryExternalId">
/// Restricts to one provider category. <see langword="null"/> means every category.
/// </param>
/// <param name="FavoritesOnly">Restricts to channels the user marked.</param>
/// <remarks>
/// A value type rather than logic scattered across the view, because a real subscription lists tens of
/// thousands of channels and these three criteria have to combine correctly and cheaply. Kept in the
/// core so the rules are testable without a window, and reusable by the planned web frontend.
/// </remarks>
public sealed record ChannelFilter(
    string? SearchText = null,
    string? CategoryExternalId = null,
    bool FavoritesOnly = false)
{
    /// <summary>A filter that admits everything.</summary>
    public static ChannelFilter None { get; } = new();

    /// <summary>Whether any criterion is actually set.</summary>
    public bool IsActive =>
        !string.IsNullOrWhiteSpace(SearchText)
        || CategoryExternalId is not null
        || FavoritesOnly;

    /// <summary>
    /// Whether a channel satisfies every criterion.
    /// </summary>
    public bool Matches(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return Matches(channel.Name, channel.CategoryExternalId, channel.IsFavorite);
    }

    /// <summary>
    /// Whether the three things a filter looks at satisfy every criterion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated over values rather than only over an entity so that a caller holding the favourite flag
    /// somewhere else can use the same rules. That is what lets the channel list's row objects own their
    /// own favourite state instead of writing it back into the database entity to keep the filter agreeing
    /// with what the row displays.
    /// </para>
    /// <para>
    /// Ordered cheapest test first. The name comparison is the only one that inspects a string, and
    /// running it after the two flag checks avoids it entirely for most channels once a category or
    /// the favourites filter is in play.
    /// </para>
    /// </remarks>
    public bool Matches(string name, string? categoryExternalId, bool isFavorite)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (FavoritesOnly && !isFavorite)
        {
            return false;
        }

        if (CategoryExternalId is not null
            && !string.Equals(categoryExternalId, CategoryExternalId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
