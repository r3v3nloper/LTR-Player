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
/// <param name="CategoryCount">Categories stored.</param>
public sealed record SourceImportResult(
    ProviderAccount Account,
    int SourceId,
    int ChannelCount,
    int CategoryCount)
{
    public bool Succeeded => Account.IsUsable;

    public static SourceImportResult Rejected(ProviderAccount account)
    {
        return new SourceImportResult(account, SourceId: 0, ChannelCount: 0, CategoryCount: 0);
    }
}
