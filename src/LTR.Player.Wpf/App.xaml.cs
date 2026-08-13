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
        var upgraded = services.PrepareCatalogueAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (upgraded > 0)
        {
            PlayerLog.CredentialsUpgraded(services.GetRequiredService<ILogger<App>>(), upgraded);
        }
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
