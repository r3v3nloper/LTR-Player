using System.CommandLine;
using System.Globalization;
using LTR.Catalogue;
using LTR.Cli;
using LTR.Playback;
using LTR.Playback.LibVlc;
using LTR.Providers;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using LTR.Security.Dpapi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// Headless entry point for the core (CLAUDE.md Â§2.12). Everything below the UI is reachable from
// here, which is what makes the player verifiable against a real panel without WPF in the way.
var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Log the requests being made. Addresses are printed with credentials removed.",

    // Recursive, so the flag works after the subcommand name where users naturally put it.
    Recursive = true,
};

// Must be awaited: the container holds IAsyncDisposable singletons, and the synchronous Dispose
// throws rather than disposing them. Getting this wrong means PlaybackSession never runs its
// shutdown release, which is precisely how a provider connection gets left open on exit.
await using var serviceProvider = BuildServiceProvider(args.Contains("--verbose") || args.Contains("-v"));

var sourceOptions = new SourceOptions();
var rootCommand = new RootCommand("Verifies the LTR-Player core against a live Xtream panel.");
rootCommand.Options.Add(verboseOption);

rootCommand.Subcommands.Add(BuildProbeCommand(serviceProvider, sourceOptions));
rootCommand.Subcommands.Add(BuildChannelsCommand(serviceProvider, sourceOptions));
rootCommand.Subcommands.Add(BuildResolveCommand(serviceProvider, sourceOptions));
rootCommand.Subcommands.Add(BuildPlayTestCommand(serviceProvider, sourceOptions));
rootCommand.Subcommands.Add(BuildSourcesCommand(serviceProvider));
rootCommand.Subcommands.Add(BuildGuideCommand(serviceProvider));
rootCommand.Subcommands.Add(BuildVodCommand(serviceProvider));

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

static ServiceProvider BuildServiceProvider(bool verbose)
{
    // Error by default, not Warning: expected conditions such as an offline channel are logged by the
    // core and reported by the command, and printing both duplicates the message.
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(verbose ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Error)
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
        .CreateLogger();

    var services = new ServiceCollection();

    services.AddLogging(logging => logging.ClearProviders().AddSerilog(dispose: true));
    services.AddProviderRegistry();
    services.AddXtreamProvider();
    services.AddM3uProvider();

    // No window exists here, so video output is switched off. It would otherwise open a window of
    // LibVLC's own and fail to allocate Direct3D decoder buffers, which buries the actual result of
    // the test under h264 errors. Audio and stream metadata are unaffected.
    services.AddLibVlcPlayback(options => options.DisableVideoOutput = true);

    // The same database the desktop player uses, resolved from the one place that decides it.
    services.AddCredentialProtection();
    services.AddCatalogue();

    // Singleton like the others now: its dependencies create their own units of work, so it no longer
    // needs a scope of its own.
    services.AddSingleton<SourcesCommandHandler>();
    services.AddSingleton<GuideCommandHandler>();
    services.AddSingleton<ProbeCommandHandler>();
    services.AddSingleton<ChannelsCommandHandler>();
    services.AddSingleton<ResolveCommandHandler>();
    services.AddSingleton<PlayTestCommandHandler>();
    services.AddSingleton<VodCommandHandler>();
    services.AddSingleton<ConnectionReleaseCheck>();

    return services.BuildServiceProvider();
}

static Command BuildSourcesCommand(IServiceProvider services)
{
    var command = new Command("sources", "Lists, adds or removes sources in the local catalogue.");

    var listCommand = new Command("list", "Shows the configured sources and which database holds them.");
    listCommand.SetAction((_, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<SourcesCommandHandler>(services, handler => handler.ListAsync(cancellationToken))));

    var addressArgument = new Argument<string>("address")
    {
        Description = "Playlist URL, or the full path to a local .m3u file.",
    };

    var nameOption = new Option<string?>("--name")
    {
        Description = "Display name for the source. Defaults to the host or file name.",
    };

    var addCommand = new Command("add-playlist", "Adds an M3U playlist and imports its catalogue.");
    addCommand.Arguments.Add(addressArgument);
    addCommand.Options.Add(nameOption);
    addCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<SourcesCommandHandler>(services, handler => handler.AddPlaylistAsync(
            parseResult.GetValue(addressArgument) ?? string.Empty,
            parseResult.GetValue(nameOption),
            cancellationToken))));

    var idArgument = new Argument<int>("id") { Description = "Source id, as shown by 'sources list'." };

    var removeCommand = new Command("remove", "Removes a source together with its catalogue.");
    removeCommand.Arguments.Add(idArgument);
    removeCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<SourcesCommandHandler>(services, handler => handler.RemoveAsync(
            parseResult.GetValue(idArgument),
            cancellationToken))));

    var refreshCommand = new Command(
        "refresh",
        "Re-imports a stored source's channels, films and series.");

    refreshCommand.Arguments.Add(idArgument);
    refreshCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<SourcesCommandHandler>(services, handler => handler.RefreshAsync(
            parseResult.GetValue(idArgument),
            cancellationToken))));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(addCommand);
    command.Subcommands.Add(refreshCommand);
    command.Subcommands.Add(removeCommand);

    return command;
}

/// <summary>
/// The film and series commands, all of which work against a source already in the catalogue.
/// </summary>
static Command BuildVodCommand(IServiceProvider services)
{
    var sourceIdOption = new Option<int>("--source-id")
    {
        Description = "Source id, as shown by 'sources list'.",
        Required = true,
    };

    var filterOption = new Option<string?>("--filter", "-f")
    {
        Description = "Show only entries whose name contains this text.",
    };

    var limitOption = new Option<int>("--limit")
    {
        Description = "Maximum number of entries to print.",
        DefaultValueFactory = _ => 40,
    };

    var command = new Command("vod", "Inspects and plays a stored source's films and series.");

    var listCommand = new Command("list", "Lists the stored films.");
    listCommand.Options.Add(sourceIdOption);
    listCommand.Options.Add(filterOption);
    listCommand.Options.Add(limitOption);
    listCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.ListMoviesAsync(
            parseResult.GetValue(sourceIdOption),
            parseResult.GetValue(filterOption),
            parseResult.GetValue(limitOption),
            cancellationToken))));

    var seriesCommand = new Command("series", "Lists the stored series.");
    seriesCommand.Options.Add(sourceIdOption);
    seriesCommand.Options.Add(filterOption);
    seriesCommand.Options.Add(limitOption);
    seriesCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.ListSeriesAsync(
            parseResult.GetValue(sourceIdOption),
            parseResult.GetValue(filterOption),
            parseResult.GetValue(limitOption),
            cancellationToken))));

    var seriesIdOption = new Option<int>("--series-id")
    {
        Description = "Local series id, as shown by 'vod series'. Not the panel's own id.",
        Required = true,
    };

    var episodesCommand = new Command(
        "episodes",
        "Shows a series' seasons and episodes, fetching them if the stored copy is stale.");

    episodesCommand.Options.Add(sourceIdOption);
    episodesCommand.Options.Add(seriesIdOption);
    episodesCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.ShowSeriesAsync(
            parseResult.GetValue(sourceIdOption),
            parseResult.GetValue(seriesIdOption),
            cancellationToken))));

    var movieIdOption = new Option<int>("--movie-id")
    {
        Description = "Local film id, as shown by 'vod list'.",
        Required = true,
    };

    var showCommand = new Command("show", "Shows one film's detail, fetching it if it has never been read.");
    showCommand.Options.Add(sourceIdOption);
    showCommand.Options.Add(movieIdOption);
    showCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.ShowMovieAsync(
            parseResult.GetValue(sourceIdOption),
            parseResult.GetValue(movieIdOption),
            cancellationToken))));

    var continueCommand = new Command("continue", "Lists what is part-watched, most recent first.");
    continueCommand.Options.Add(sourceIdOption);
    continueCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.ContinueWatchingAsync(
            parseResult.GetValue(sourceIdOption),
            cancellationToken))));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(seriesCommand);
    command.Subcommands.Add(episodesCommand);
    command.Subcommands.Add(showCommand);
    command.Subcommands.Add(continueCommand);
    command.Subcommands.Add(BuildVodPlayTestCommand(services, sourceIdOption));

    return command;
}

static Command BuildVodPlayTestCommand(IServiceProvider services, Option<int> sourceIdOption)
{
    // Optional here, unlike in the listing commands, because exactly one of the two must be given and
    // System.CommandLine cannot express that; the handler says so plainly instead.
    var movieIdOption = new Option<int?>("--movie-id")
    {
        Description = "Local film id, as shown by 'vod list'.",
    };

    var episodeIdOption = new Option<int?>("--episode-id")
    {
        Description = "Local episode id, as shown by 'vod episodes'.",
    };

    var secondsOption = new Option<int>("--seconds")
    {
        Description = "How long to hold the stream open.",
        DefaultValueFactory = _ => 5,
    };

    var startAtOption = new Option<int>("--start-at")
    {
        Description = "Start this many seconds in, which is how resuming is verified.",
    };

    var command = new Command(
        "play-test",
        "Opens a stored film or episode headlessly, reports its position, then releases it.");

    command.Options.Add(sourceIdOption);
    command.Options.Add(movieIdOption);
    command.Options.Add(episodeIdOption);
    command.Options.Add(secondsOption);
    command.Options.Add(startAtOption);

    command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<VodCommandHandler>(services, handler => handler.PlayTestAsync(
            parseResult.GetValue(sourceIdOption),
            parseResult.GetValue(movieIdOption),
            parseResult.GetValue(episodeIdOption),
            parseResult.GetValue(secondsOption),
            parseResult.GetValue(startAtOption),
            cancellationToken))));

    return command;
}

static Command BuildGuideCommand(IServiceProvider services)
{
    var idOption = new Option<int>("--source-id")
    {
        Description = "Source id, as shown by 'sources list'.",
        Required = true,
    };

    var forceOption = new Option<bool>("--force")
    {
        Description = "Fetch the guide even when the stored one is still fresh.",
    };

    var command = new Command("guide", "Imports and inspects a stored source's programme guide.");

    var importCommand = new Command(
        "import",
        "Downloads the source's XMLTV guide, stores it and matches it to the channel list.");

    importCommand.Options.Add(idOption);
    importCommand.Options.Add(forceOption);
    importCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<GuideCommandHandler>(services, handler => handler.ImportAsync(
            parseResult.GetValue(idOption),
            parseResult.GetValue(forceOption),
            cancellationToken))));

    var showCommand = new Command("show", "Reports the stored guide's coverage and match rate.");
    showCommand.Options.Add(idOption);
    showCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithCatalogue<GuideCommandHandler>(services, handler => handler.ShowAsync(
            parseResult.GetValue(idOption),
            cancellationToken))));

    command.Subcommands.Add(importCommand);
    command.Subcommands.Add(showCommand);

    return command;
}

/// <summary>
/// Prepares the catalogue, then runs a command against it.
/// </summary>
/// <remarks>
/// Preparation happens here rather than at startup because only these commands touch the database:
/// probing a panel or testing playback should not create a database file as a side effect. Both
/// applications share one, and either may run first, so whichever does has to migrate it and protect
/// any credential still held in plain text.
/// </remarks>
static async Task<int> WithCatalogue<THandler>(IServiceProvider services, Func<THandler, Task<int>> action)
    where THandler : notnull
{
    var preparation = await services.PrepareCatalogueAsync(CancellationToken.None).ConfigureAwait(false);

    if (preparation.UpgradedCredentials > 0)
    {
        Console.WriteLine(
            $"Protected {preparation.UpgradedCredentials} stored credential(s) that were held in plain text.");
    }

    if (preparation.QuarantinedDatabasePath is { } quarantined)
    {
        Console.Error.WriteLine(
            "The catalogue could not be read and was set aside; starting from an empty one. Sources will "
            + "have to be added again.");
        Console.Error.WriteLine($"Unreadable file kept at: {quarantined}");
    }

    return await action(services.GetRequiredService<THandler>()).ConfigureAwait(false);
}

static Command BuildProbeCommand(IServiceProvider services, SourceOptions sourceOptions)
{
    var command = new Command("probe", "Reports the subscription state and what the panel supports.");
    sourceOptions.AddTo(command);

    command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        services.GetRequiredService<ProbeCommandHandler>()
            .ExecuteAsync(sourceOptions.ToSource(parseResult), cancellationToken)));

    return command;
}

static Command BuildChannelsCommand(IServiceProvider services, SourceOptions sourceOptions)
{
    var filterOption = new Option<string?>("--filter", "-f")
    {
        Description = "Show only channels whose name contains this text.",
    };

    var limitOption = new Option<int>("--limit")
    {
        Description = "Maximum number of channels to print.",
        DefaultValueFactory = _ => 40,
    };

    var command = new Command("channels", "Fetches the live catalogue and summarises it.");
    sourceOptions.AddTo(command);
    command.Options.Add(filterOption);
    command.Options.Add(limitOption);

    command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        services.GetRequiredService<ChannelsCommandHandler>()
            .ExecuteAsync(
                sourceOptions.ToSource(parseResult),
                parseResult.GetValue(filterOption),
                parseResult.GetValue(limitOption),
                cancellationToken)));

    return command;
}

static Command BuildResolveCommand(IServiceProvider services, SourceOptions sourceOptions)
{
    var streamIdOption = new Option<string>("--stream-id")
    {
        Description = "Provider stream id, as printed by the channels command.",
        Required = true,
    };

    var revealOption = new Option<bool>("--reveal")
    {
        Description = "Print the address with credentials in clear text.",
    };

    var probeOption = new Option<bool>("--probe")
    {
        Description = "Probe the panel first, so the container matches what it actually serves.",
    };

    var command = new Command("resolve", "Builds the playable address for one channel.");
    sourceOptions.AddTo(command);
    command.Options.Add(streamIdOption);
    command.Options.Add(revealOption);
    command.Options.Add(probeOption);

    command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        services.GetRequiredService<ResolveCommandHandler>()
            .ExecuteAsync(
                sourceOptions.ToSource(parseResult),
                parseResult.GetValue(streamIdOption) ?? string.Empty,
                parseResult.GetValue(revealOption),
                parseResult.GetValue(probeOption),
                cancellationToken)));

    return command;
}

static Command BuildPlayTestCommand(IServiceProvider services, SourceOptions sourceOptions)
{
    var streamIdOption = new Option<string>("--stream-id")
    {
        Description = "Provider stream id, as printed by the channels command.",
        Required = true,
    };

    var secondsOption = new Option<int>("--seconds")
    {
        Description = "How long to hold the stream open.",
        DefaultValueFactory = _ => 5,
    };

    var command = new Command(
        "play-test",
        "Opens a channel headlessly, reports its tracks, then verifies the connection is released.");

    sourceOptions.AddTo(command);
    command.Options.Add(streamIdOption);
    command.Options.Add(secondsOption);

    command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        services.GetRequiredService<PlayTestCommandHandler>()
            .ExecuteAsync(
                sourceOptions.ToSource(parseResult),
                parseResult.GetValue(streamIdOption) ?? string.Empty,
                parseResult.GetValue(secondsOption),
                cancellationToken)));

    return command;
}

