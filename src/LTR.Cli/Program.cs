using System.CommandLine;
using System.Globalization;
using LTR.Catalogue;
using LTR.Cli;
using LTR.Cli.Commands;
using LTR.Playback;
using LTR.Playback.LibVlc;
using LTR.Providers;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using LTR.Security.Dpapi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// Headless entry point for the core (CLAUDE.md §2.12). Everything below the UI is reachable from
// here, which is what makes the player verifiable against a real panel without WPF in the way.
//
// Composition only: each command states its own options and action in a class of its own, under Commands/.
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
var catalogue = new CatalogueCommandRunner(serviceProvider);

var rootCommand = new RootCommand("Verifies the LTR-Player core against a live Xtream panel.");
rootCommand.Options.Add(verboseOption);

// Commands addressing a panel by URL first, then the four working against the stored catalogue.
rootCommand.Subcommands.Add(new ProbeCommand(serviceProvider, sourceOptions).Build());
rootCommand.Subcommands.Add(new ChannelsCommand(serviceProvider, sourceOptions).Build());
rootCommand.Subcommands.Add(new ResolveCommand(serviceProvider, sourceOptions).Build());
rootCommand.Subcommands.Add(new LivePlayTestCommand(serviceProvider, sourceOptions).Build());
rootCommand.Subcommands.Add(new SourcesCommand(catalogue).Build());
rootCommand.Subcommands.Add(new GuideCommand(catalogue).Build());
rootCommand.Subcommands.Add(new LiveCommand(catalogue).Build());
rootCommand.Subcommands.Add(new VodCommand(catalogue).Build());

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

    // Singletons: their dependencies create their own units of work, so none needs a scope of its own.
    // Resolved when a command runs rather than while the tree is built, or listing a source would
    // construct LibVLC.
    services.AddSingleton<SourcesCommandHandler>();
    services.AddSingleton<GuideCommandHandler>();
    services.AddSingleton<ProbeCommandHandler>();
    services.AddSingleton<ChannelsCommandHandler>();
    services.AddSingleton<ResolveCommandHandler>();
    services.AddSingleton<PlayTestCommandHandler>();

    // The four the 'vod' command dispatches to, which were one class taking ten dependencies.
    services.AddSingleton<VodListingCommandHandler>();
    services.AddSingleton<VodDetailCommandHandler>();
    services.AddSingleton<WatchProgressCommandHandler>();
    services.AddSingleton<VodPlayTestCommandHandler>();

    services.AddSingleton<LiveCommandHandler>();

    // The console as a dependency, for the two collaborators whose *output is the result*: one decides
    // whether a subscription's credentials are printed, the other whether teardown was clean. Reaching for
    // Console directly is what left both untestable. Everything else here still prints its own listings.
    services.AddSingleton(Console.Out);

    services.AddSingleton<StoredSourceLookup>();
    services.AddSingleton<ResolvedAddressReport>();
    services.AddSingleton<ConnectionReleaseCheck>();
    services.AddSingleton<StreamHoldTest>();

    return services.BuildServiceProvider();
}
