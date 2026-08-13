using LTR.Catalogue;
using LTR.Core.Sources;
using LTR.Persistence;

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
    private readonly ICatalogueStore _catalogue;
    private readonly ISourceImportService _import;

    public SourcesCommandHandler(ICatalogueStore catalogue, ISourceImportService import)
    {
        _catalogue = catalogue;
        _import = import;
    }

    public async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Database   {LtrDatabaseLocation.DatabaseFile}");
        Console.WriteLine();

        var sources = await _catalogue.GetSourcesAsync(cancellationToken).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            Console.WriteLine("No sources are configured.");
            return 0;
        }

        Console.WriteLine($"{"id",-4} {"name",-28} {"protocol",-10} {"channels",-9} {"favourites",-11} refreshed");

        foreach (var source in sources)
        {
            var channels = await _catalogue.GetLiveChannelsAsync(source.Id, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(
                $"{source.Id,-4} {ConsoleText.Truncate(source.Name, 28),-28} {DescribeProtocol(source),-10} "
                + $"{channels.Count,-9} {channels.Count(channel => channel.IsFavorite),-11} "
                + $"{ConsoleText.FormatUtc(source.LastRefreshedUtc)}");
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

        var progress = new Progress<SourceImportStage>(stage => Console.WriteLine($"  {stage}..."));
        var result = await _import.ImportAsync(source, progress, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine("The playlist could not be retrieved.");
            return 1;
        }

        Console.WriteLine(
            $"Added '{source.Name}' as source {result.SourceId}: {result.ChannelCount} channels, "
            + $"{result.CategoryCount} categories.");

        return 0;
    }

    public async Task<int> RemoveAsync(int sourceId, CancellationToken cancellationToken)
    {
        await _catalogue.DeleteSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);

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
}
