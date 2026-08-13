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
    /// <remarks>
    /// Ordered cheapest test first. The name comparison is the only one that inspects a string, and
    /// running it after the two flag checks avoids it entirely for most channels once a category or
    /// the favourites filter is in play.
    /// </remarks>
    public bool Matches(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (FavoritesOnly && !channel.IsFavorite)
        {
            return false;
        }

        if (CategoryExternalId is not null
            && !string.Equals(channel.CategoryExternalId, CategoryExternalId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return channel.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
