using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using LTR.Catalogue;
using LTR.Playback;
using LTR.Providers;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using LTR.Security.Dpapi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace LTR.Player.Wpf;

/// <summary>
/// Composition root of the desktop player.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Invariant formatting, so a log file reads the same whatever locale the machine runs under.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()

            // Entity Framework logs every statement it executes at Information, and against this schema
            // that is most of the file — a single source switch writes the channel query, the category
            // query, now-and-next, films, series and continue-watching, each several lines of SQL. The log
            // is the diagnostic trail for a player whose failures are mostly a provider's doing, and a
            // trail nobody can read is not one. Warning is kept rather than Error on purpose: the
            // split-query complaint that found a real cartesian product arrives at Warning.
            //
            // To get the statements back for a session, lower this line, or use the command line tool,
            // whose --verbose does the same for the same database.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(
                AppPaths.LogFile,
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _services = BuildServices();

        PlayerLog.UsingDatabase(
            _services.GetRequiredService<ILogger<App>>(),
            AppPaths.DatabaseFile);

        MigrateDatabase(_services);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Blocking on DisposeAsync is deliberate. The container holds the playback session, whose
        // disposal releases the provider connection, and WPF gives no asynchronous exit hook. Letting
        // the process exit without that release is what leaves a subscription locked out, so a brief
        // block on shutdown is the right trade.
        if (_services is not null)
        {
            _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _services = null;
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.ClearProviders().AddSerilog(dispose: true));

        services.AddCredentialProtection();
        services.AddCatalogue();
        services.AddProviderRegistry();
        services.AddXtreamProvider();
        services.AddM3uProvider();
        services.AddLibVlcPlayback();

        services.AddSingleton<StatusLine>();
        services.AddSingleton<SourceManagementViewModel>();
        services.AddSingleton<ChannelListViewModel>();
        services.AddSingleton<GuideViewModel>();
        services.AddSingleton<MovieListViewModel>();
        services.AddSingleton<SeriesCatalogueViewModel>();
        services.AddSingleton<ContinueWatchingViewModel>();
        services.AddSingleton<GuideImportCoordinator>();
        services.AddSingleton<PlaybackCoordinator>();

        // Takes the TimeProvider the catalogue registered, as the channel list and the guide already do:
        // a view model that reads the clock is given one rather than reaching for the static property.
        services.AddSingleton<PlayerOverlayViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Brings the local database up to date before any window opens.
    /// </summary>
    /// <remarks>
    /// Migrations rather than <c>EnsureCreated</c>, so an installation upgraded from an earlier build
    /// keeps its configured sources and favourites instead of needing the file deleted.
    /// </remarks>
    private static void MigrateDatabase(IServiceProvider services)
    {
        // Blocking here is acceptable: it migrates a small schema and touches a handful of credential
        // rows, and it runs before the first window opens.
        var preparation = services.PrepareCatalogueAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var logger = services.GetRequiredService<ILogger<App>>();

        if (preparation.UpgradedCredentials > 0)
        {
            PlayerLog.CredentialsUpgraded(logger, preparation.UpgradedCredentials);
        }

        if (preparation.QuarantinedDatabasePath is { } quarantined)
        {
            PlayerLog.CatalogueQuarantined(logger, quarantined);
            ReportQuarantine(quarantined);
        }
    }

    /// <summary>
    /// Tells the user their catalogue was unreadable, because their configured subscriptions went with it.
    /// </summary>
    /// <remarks>
    /// A dialog rather than the status line. Coming up with an empty channel list and no explanation looks
    /// like the player lost their subscription for no reason, which is worse than being interrupted once.
    /// </remarks>
    private static void ReportQuarantine(string quarantinedPath)
    {
        MessageBox.Show(
            "The stored catalogue could not be read and has been set aside, so the player has started with "
            + "an empty one. Your subscriptions will have to be added again.\n\n"
            + $"The unreadable file was kept here, in case it is worth looking at:\n{quarantinedPath}",
            "LTR-Player",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread.");

        MessageBox.Show(
            $"{e.Exception.Message}\n\nDetails were written to the log in:\n{AppPaths.DataDirectory}",
            "LTR-Player",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Handled so a single failed action does not take the whole player down with it.
        e.Handled = true;
    }
}
