using System.CommandLine;
using System.Globalization;
using LTR.Cli;
using LTR.Core;
using LTR.Core.Security;
using LTR.Persistence;
using Microsoft.EntityFrameworkCore;
using LTR.Playback;
using LTR.Playback.LibVlc;
using LTR.Providers;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// Headless entry point for the core (CLAUDE.md §2.12). Everything below the UI is reachable from
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
    services.AddDbContext<LtrDbContext>(options => options.UseSqlite(LtrDatabaseLocation.ConnectionString));
    services.AddSingleton<ICredentialProtector, PassThroughCredentialProtector>();

    services.AddScoped<SourcesCommandHandler>();
    services.AddSingleton<ProbeCommandHandler>();
    services.AddSingleton<ChannelsCommandHandler>();
    services.AddSingleton<ResolveCommandHandler>();
    services.AddSingleton<PlayTestCommandHandler>();

    return services.BuildServiceProvider();
}

static Command BuildSourcesCommand(IServiceProvider services)
{
    var command = new Command("sources", "Lists, adds or removes sources in the local catalogue.");

    var listCommand = new Command("list", "Shows the configured sources and which database holds them.");
    listCommand.SetAction((_, cancellationToken) => CommandRunner.RunAsync(() =>
        WithScope(services, handler => handler.ListAsync(cancellationToken))));

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
        WithScope(services, handler => handler.AddPlaylistAsync(
            parseResult.GetValue(addressArgument) ?? string.Empty,
            parseResult.GetValue(nameOption),
            cancellationToken))));

    var idArgument = new Argument<int>("id") { Description = "Source id, as shown by 'sources list'." };

    var removeCommand = new Command("remove", "Removes a source together with its catalogue.");
    removeCommand.Arguments.Add(idArgument);
    removeCommand.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
        WithScope(services, handler => handler.RemoveAsync(parseResult.GetValue(idArgument), cancellationToken))));

    command.Subcommands.Add(listCommand);
    command.Subcommands.Add(addCommand);
    command.Subcommands.Add(removeCommand);

    return command;
}

/// <summary>
/// Runs a database command inside its own scope, because the context is scoped and these commands are
/// the only ones that touch it.
/// </summary>
static async Task<int> WithScope(IServiceProvider services, Func<SourcesCommandHandler, Task<int>> action)
{
    await using var scope = services.CreateAsyncScope();
    return await action(scope.ServiceProvider.GetRequiredService<SourcesCommandHandler>()).ConfigureAwait(false);
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
