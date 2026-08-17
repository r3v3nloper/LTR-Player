namespace LTR.Persistence;

/// <summary>
/// Works out what a freshly fetched listing means for what is stored: which rows are new, which pair up
/// with one already there, and which the provider has stopped offering.
/// </summary>
/// <remarks>
/// <para>
/// The same four steps were written out for categories, channels, films and series — index the stored rows by
/// key, walk the incoming ones, remember which keys were seen, remove the rest. That is a diff, it touches no
/// database, and having it four times is how one of them ends up subtly different from its siblings.
/// </para>
/// <para>
/// It deliberately decides nothing about *fields*: what a provider owns and what it must not overwrite is
/// stated on the entities themselves, next to the properties it concerns. This is only the matching.
/// </para>
/// </remarks>
internal static class CatalogueReconciler
{
    /// <summary>
    /// Matches a fetched listing against the stored rows by key.
    /// </summary>
    /// <param name="keyOf">
    /// The provider's own identity for a row, within its source. Composite for a category, because a panel
    /// numbers its identifiers per section and the kind is therefore part of the identity.
    /// </param>
    /// <remarks>
    /// A stored row is paired with the *first* incoming row of the same key, and a second one is treated as
    /// new. A provider listing something twice is a provider fault rather than a reason to fail an import —
    /// and the unique index on (source, identity) is what has the final word.
    /// </remarks>
    public static Reconciliation<T> Match<T, TKey>(
        IReadOnlyList<T> stored,
        IReadOnlyList<T> fetched,
        Func<T, TKey> keyOf,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(keyOf);

        var storedByKey = new Dictionary<TKey, T>(comparer);

        foreach (var row in stored)
        {
            storedByKey.TryAdd(keyOf(row), row);
        }

        var seen = new HashSet<TKey>(comparer);
        var added = new List<T>();
        var matched = new List<(T Stored, T Fetched)>();

        foreach (var row in fetched)
        {
            var key = keyOf(row);

            if (seen.Add(key) && storedByKey.TryGetValue(key, out var existing))
            {
                matched.Add((existing, row));
                continue;
            }

            added.Add(row);
        }

        var removed = stored.Where(row => !seen.Contains(keyOf(row))).ToList();

        return new Reconciliation<T>(added, matched, removed);
    }
}

/// <summary>
/// What one listing means for what is stored.
/// </summary>
/// <param name="Added">Rows the provider now offers that are not stored yet.</param>
/// <param name="Matched">
/// Stored rows and the fetched rows they correspond to. The stored instance is the tracked one, so adopting
/// the fetched row's fields onto it is what persists the change.
/// </param>
/// <param name="Removed">Stored rows the provider no longer offers.</param>
internal sealed record Reconciliation<T>(
    IReadOnlyList<T> Added,
    IReadOnlyList<(T Stored, T Fetched)> Matched,
    IReadOnlyList<T> Removed);
