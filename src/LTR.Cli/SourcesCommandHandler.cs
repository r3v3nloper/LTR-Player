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
    private readonly ISourceStore _sources;
    private readonly ILiveCatalogue _channels;
    private readonly ISourceImportService _import;

    public SourcesCommandHandler(
        ISourceStore sources,
        ILiveCatalogue channels,
        ISourceImportService import)
    {
        _sources = sources;
        _channels = channels;
        _import = import;
    }

    public async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Database   {LtrDatabaseLocation.DatabaseFile}");
        Console.WriteLine();

        var sources = await _sources.GetSourcesAsync(cancellationToken).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            Console.WriteLine("No sources are configured.");
            return 0;
        }

        Console.WriteLine($"{"id",-4} {"name",-28} {"protocol",-10} {"channels",-9} {"favourites",-11} refreshed");

        foreach (var source in sources)
        {
            var channels = await _channels.GetLiveChannelsAsync(source.Id, cancellationToken)
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

    /// <summary>
    /// Re-imports a stored source's catalogue, whatever its protocol.
    /// </summary>
    /// <remarks>
    /// The one way to import an Xtream catalogue without the window, which is what makes the film and
    /// series sections verifiable headlessly at all (§2.12). Safe to expose for Xtream even though adding
    /// one is not: the credentials are already stored, so nothing appears in shell history.
    /// </remarks>
    public async Task<int> RefreshAsync(int sourceId, CancellationToken cancellationToken)
    {
        var sources = await _sources.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.FirstOrDefault(candidate => candidate.Id == sourceId);

        if (source is null)
        {
            Console.Error.WriteLine($"No source with id {sourceId}. Run 'sources list' to see what there is.");
            return 1;
        }

        Console.WriteLine($"Refreshing '{source.Name}'.");

        var progress = new Progress<SourceImportStage>(stage => Console.WriteLine($"  {stage}..."));
        var result = await _import.RefreshAsync(source, progress, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(
                $"The subscription could not be used: {result.Account.Status}. Nothing was changed.");
            return 1;
        }

        Console.WriteLine(
            $"{result.ChannelCount} channels, {result.MovieCount} films, {result.SeriesCount} series, "
            + $"{result.CategoryCount} categories.");

        if (!result.HasVod)
        {
            Console.WriteLine(
                "No films or series were stored. Either the panel offers none, or the probe found the "
                + "endpoints absent — 'probe' reports which.");
        }

        return 0;
    }

    public async Task<int> RemoveAsync(int sourceId, CancellationToken cancellationToken)
    {
        await _sources.DeleteSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);

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
