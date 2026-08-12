using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Fetches the live catalogue and prints a summary of it.
/// </summary>
internal sealed class ChannelsCommandHandler
{
    private readonly IContentProviderFactory _providerFactory;

    public ChannelsCommandHandler(IContentProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async Task<int> ExecuteAsync(
        XtreamSource source,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        var provider = _providerFactory.Create(source);

        var categories = await provider.FetchCategoriesAsync(ContentKind.Live, cancellationToken)
            .ConfigureAwait(false);

        var channels = await provider.FetchLiveChannelsAsync(cancellationToken).ConfigureAwait(false);

        var categoryNames = categories.ToDictionary(
            category => category.ExternalId,
            category => category.Name,
            StringComparer.Ordinal);

        var matching = string.IsNullOrWhiteSpace(filter)
            ? channels
            : [.. channels.Where(channel => channel.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];

        Console.WriteLine($"{categories.Count} categories, {channels.Count} live channels");

        if (!string.IsNullOrWhiteSpace(filter))
        {
            Console.WriteLine($"{matching.Count} match \"{filter}\"");
        }

        Console.WriteLine();
        Console.WriteLine($"{"stream id",-12} {"channel",-42} {"category",-24} archive");

        foreach (var channel in matching.Take(limit))
        {
            var category = channel.CategoryExternalId is not null
                && categoryNames.TryGetValue(channel.CategoryExternalId, out var name)
                    ? name
                    : "-";

            Console.WriteLine(
                $"{channel.ExternalId,-12} {Truncate(channel.Name, 42),-42} {Truncate(category, 24),-24} "
                + $"{(channel.HasArchive ? $"{channel.ArchiveDurationDays ?? 0}d" : "-")}");
        }

        if (matching.Count > limit)
        {
            Console.WriteLine();
            Console.WriteLine($"... {matching.Count - limit} more; raise --limit to see them.");
        }

        var withoutGuideId = channels.Count(channel => channel.EpgChannelId is null);

        if (withoutGuideId > 0)
        {
            // Directly predicts how much of the guide will be missing, so it is worth surfacing.
            Console.WriteLine();
            Console.WriteLine(
                $"{withoutGuideId} of {channels.Count} channels carry no guide id and will need "
                + "name-based matching against the XMLTV data.");
        }

        return 0;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }
}
