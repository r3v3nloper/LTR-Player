using System.Globalization;
using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using Microsoft.EntityFrameworkCore;

namespace LTR.Cli;

/// <summary>
/// Lists and adds the sources stored in the local catalogue database.
/// </summary>
/// <remarks>
/// Exists because the database was previously reachable only from the desktop player, which made two
/// questions unanswerable from a script: which sources are configured, and which database file holds
/// them. It also lets a source be seeded without the UI, which is what makes the stored catalogue
/// testable at all.
/// </remarks>
internal sealed class SourcesCommandHandler
{
    private readonly LtrDbContext _context;
    private readonly IProviderRegistry _providers;

    public SourcesCommandHandler(LtrDbContext context, IProviderRegistry providers)
    {
        _context = context;
        _providers = providers;
    }

    public async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Database   {LtrDatabaseLocation.DatabaseFile}");
        Console.WriteLine();

        var sources = await _context.GetSourcesAsync(cancellationToken).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            Console.WriteLine("No sources are configured.");
            return 0;
        }

        Console.WriteLine($"{"id",-4} {"name",-28} {"protocol",-10} {"channels",-9} {"favourites",-11} refreshed");

        foreach (var source in sources)
        {
            var channels = await _context.GetLiveChannelsAsync(source.Id, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"{source.Id,-4} {Truncate(source.Name, 28),-28} {DescribeProtocol(source),-10} "
                + $"{channels.Count,-9} {channels.Count(channel => channel.IsFavorite),-11} "
                + $"{Describe(source.LastRefreshedUtc)}");
        }

        return 0;
    }

    /// <summary>
    /// Adds a playlist source and imports its catalogue.
    /// </summary>
    /// <remarks>
    /// Restricted to M3U on purpose. A playlist needs no credentials, so it can be added from a script
    /// without a password appearing in shell history — and adding an Xtream account belongs in the UI
    /// where the password can be typed into a masked field.
    /// </remarks>
    public async Task<int> AddPlaylistAsync(string address, string? name, CancellationToken cancellationToken)
    {
        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        if (!SourceAddress.TryParse(address, out var playlistUrl))
        {
            Console.Error.WriteLine(
                $"'{address}' is neither an http address nor an existing file. A panel host on its own "
                + "is not enough — write it as http://host:port/...");
            return 1;
        }

        var source = new M3uSource
        {
            Name = name ?? SourceAddress.Describe(playlistUrl),
            PlaylistUrl = playlistUrl,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        var provider = _providers.CreateProvider(source);
        var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        if (!account.IsUsable)
        {
            Console.Error.WriteLine("The playlist could not be retrieved.");
            return 1;
        }

        source.Capabilities = await _providers.GetCapabilityProbe(source)
            .ProbeAsync(source, cancellationToken)
            .ConfigureAwait(false);

        var categories = await provider.FetchCategoriesAsync(ContentKind.Live, cancellationToken)
            .ConfigureAwait(false);
        var channels = await provider.FetchLiveChannelsAsync(cancellationToken).ConfigureAwait(false);

        var sourceId = await _context.AddSourceAsync(source, cancellationToken).ConfigureAwait(false);

        await _context.ReconcileLiveCatalogueAsync(
                sourceId,
                categories,
                channels,
                DateTimeOffset.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"Added '{source.Name}' as source {sourceId}: {channels.Count} channels, "
            + $"{categories.Count} categories.");

        return 0;
    }

    public async Task<int> RemoveAsync(int sourceId, CancellationToken cancellationToken)
    {
        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await _context.DeleteSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Removed source {sourceId} and its catalogue.");
        return 0;
    }

    private static string DescribeProtocol(PlaylistSource source)
    {
        return source switch
        {
            XtreamSource => "xtream",
            M3uSource => "m3u",
            _ => "unknown",
        };
    }

    private static string Describe(DateTimeOffset? refreshedUtc)
    {
        return refreshedUtc is null
            ? "never"
            : refreshedUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }
}
