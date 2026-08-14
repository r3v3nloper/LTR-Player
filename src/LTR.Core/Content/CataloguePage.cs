namespace LTR.Core.Content;

/// <summary>
/// A bounded slice of a catalogue section, together with how much it was taken from.
/// </summary>
/// <remarks>
/// <para>
/// Exists because a real subscription's film catalogue is far larger than its channel list — sixty-six
/// thousand films against seventeen thousand channels, for the subscription this was built against — and
/// nobody browses that by scrolling. The list therefore answers a search rather than presenting
/// everything, and it has to be able to say so: a screen showing two hundred results out of nine hundred
/// with no indication is a screen that looks like it has lost the rest.
/// </para>
/// <para>
/// <see cref="TotalMatching"/> is what the filter matched, not what the catalogue holds.
/// </para>
/// </remarks>
public sealed record CataloguePage<T>(IReadOnlyList<T> Items, int TotalMatching)
{
    /// <summary>Whether matches were left out, which is what the caller has to state on screen.</summary>
    public bool IsTruncated => Items.Count < TotalMatching;
}

/// <summary>
/// Creates empty pages, which a generic type cannot expose as a static member of its own.
/// </summary>
public static class CataloguePage
{
    public static CataloguePage<T> Empty<T>()
    {
        return new CataloguePage<T>([], 0);
    }
}
