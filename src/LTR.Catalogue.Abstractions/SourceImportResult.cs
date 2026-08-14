using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// What an import or refresh achieved.
/// </summary>
/// <param name="Account">
/// The subscription's state as the provider reported it. Carried through rather than reduced to a
/// message, so the caller can word the outcome for its own audience — and so an expired subscription
/// stays distinguishable from rejected credentials.
/// </param>
/// <param name="SourceId">
/// Identity of the stored source. Zero when nothing was stored because the account was unusable.
/// </param>
/// <param name="ChannelCount">Channels stored.</param>
/// <param name="CategoryCount">Categories stored, across every kind the import covered.</param>
/// <param name="MovieCount">Films stored. Zero for a source that offers none.</param>
/// <param name="SeriesCount">Series stored, counted shallowly: their seasons are fetched on demand.</param>
public sealed record SourceImportResult(
    ProviderAccount Account,
    int SourceId,
    int ChannelCount,
    int CategoryCount,
    int MovieCount = 0,
    int SeriesCount = 0)
{
    public bool Succeeded => Account.IsUsable;

    /// <summary>Whether anything beyond live television was stored, which is what decides whether the
    /// film and series sections are worth showing at all.</summary>
    public bool HasVod => MovieCount > 0 || SeriesCount > 0;

    public static SourceImportResult Rejected(ProviderAccount account)
    {
        return new SourceImportResult(account, SourceId: 0, ChannelCount: 0, CategoryCount: 0);
    }
}
