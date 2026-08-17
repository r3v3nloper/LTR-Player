using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// A stored source's live channels: what was imported, and the address one of them plays from.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>vod list</c> for live television, and the only way to reach a playlist source's
/// addresses at all. A panel's address is composed from credentials, so <c>resolve</c> can build one without
/// anything stored; a playlist's address arrives *inside* the playlist and exists only in the catalogue.
/// </para>
/// <para>
/// Listing and resolving live together because resolving needs the same channel lookup the listing prints
/// the ids for, and because those ids are only useful as a pair — nothing else in the CLI knows them.
/// </para>
/// </remarks>
internal sealed class LiveCommandHandler
{
    private readonly StoredSourceLookup _sources;
    private readonly ILiveCatalogue _catalogue;
    private readonly IProviderRegistry _providers;
    private readonly ResolvedAddressReport _report;

    public LiveCommandHandler(
        StoredSourceLookup sources,
        ILiveCatalogue catalogue,
        IProviderRegistry providers,
        ResolvedAddressReport report)
    {
        _sources = sources;
        _catalogue = catalogue;
        _providers = providers;
        _report = report;
    }

    public async Task<int> ListAsync(
        int sourceId,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var channels = await _catalogue
            .GetLiveChannelsAsync(source.Id, cancellationToken)
            .ConfigureAwait(false);

        var matching = Narrow(channels, filter);

        Console.WriteLine($"Source     {source.Name}");
        Console.WriteLine($"Channels   {channels.Count} stored, {matching.Count} matching");

        if (matching.Count == 0)
        {
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"{"id",-7} {"name",-44} {"provider id",-14} {"guide",-6} fav");

        foreach (var channel in matching.Take(limit > 0 ? limit : Commands.CommandDefaults.Limit))
        {
            Console.WriteLine(
                $"{channel.Id,-7} {ConsoleText.Truncate(channel.Name, 44),-44} "
                + $"{ConsoleText.Truncate(channel.ExternalId, 14),-14} "
                + $"{(string.IsNullOrEmpty(channel.EpgChannelId) ? "-" : "yes"),-6} "
                + $"{(channel.IsFavorite ? "yes" : "-")}");
        }

        return 0;
    }

    /// <summary>
    /// Prints the address one stored channel plays from.
    /// </summary>
    /// <remarks>
    /// Takes the local id from <c>live list</c> rather than the provider's own, because a playlist issues no
    /// stream ids: its identity is derived from the guide id or the name, and the address is a field.
    /// </remarks>
    public async Task<int> ResolveAsync(
        int sourceId,
        int channelId,
        bool revealCredentials,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var channels = await _catalogue
            .GetLiveChannelsAsync(source.Id, cancellationToken)
            .ConfigureAwait(false);

        if (channels.FirstOrDefault(candidate => candidate.Id == channelId) is not { } channel)
        {
            Console.Error.WriteLine(
                $"No channel with id {channelId} in this source. Run 'live list --source-id {sourceId}'.");
            return 1;
        }

        Console.WriteLine($"Channel     {channel.Name}");

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, channel);

        _report.Print(request, source, revealCredentials);

        return 0;
    }

    /// <remarks>
    /// Filtered through <see cref="CatalogueFilter"/>, so the command line matches what the window matches.
    /// </remarks>
    private static List<Channel> Narrow(IReadOnlyList<Channel> channels, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [.. channels];
        }

        var criteria = new CatalogueFilter(SearchText: filter);

        return [.. channels.Where(channel => criteria.Matches(channel.Name, channel.CategoryExternalId))];
    }
}
