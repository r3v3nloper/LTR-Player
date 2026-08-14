using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Catalogue;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Runs guide imports in the background and reports them, on behalf of the shell.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of the shell view model, which was carrying composition, playback, the film and series
/// sections and this — four reasons to change in one class (backlog rank 10). Everything about an import's
/// lifecycle now lives here: starting one, refusing a second, wording every outcome, and waiting for it on
/// the way out.
/// </para>
/// <para>
/// What happens *after* a successful import stays with the caller, as a continuation it supplies. Reloading
/// the channel list and the timeline needs both of those, and reaching for them from here would put the
/// coordinator back in the business of knowing the whole window.
/// </para>
/// </remarks>
public sealed partial class GuideImportCoordinator : ObservableObject
{
    private readonly IGuideImportService _guideImport;
    private readonly StatusLine _status;
    private readonly ILogger<GuideImportCoordinator> _logger;

    /// <summary>
    /// The import in flight, so a second one is not started alongside it and so shutdown can wait for it
    /// to notice its cancellation.
    /// </summary>
    private Task _importTask = Task.CompletedTask;

    [ObservableProperty]
    private bool _isImporting;

    public GuideImportCoordinator(
        IGuideImportService guideImport,
        StatusLine status,
        ILogger<GuideImportCoordinator> logger)
    {
        _guideImport = guideImport;
        _status = status;
        _logger = logger;
    }

    /// <summary>
    /// The import in flight, or an already completed task.
    /// </summary>
    /// <remarks>
    /// Exposed because a background task nothing can observe is also a background task nothing can shut
    /// down or test.
    /// </remarks>
    public Task Completion => _importTask;

    /// <summary>
    /// Starts an import unless one is already running.
    /// </summary>
    /// <param name="onImported">
    /// Run after a successful import, on the UI thread. Reloading what the import changed belongs to the
    /// caller, which is what keeps this class from needing to know the rest of the window.
    /// </param>
    /// <remarks>
    /// Not awaited by its caller, which is the point: an import takes minutes and the window has to stay
    /// usable throughout, including for playback. What keeps that from being fire-and-forget in the bad
    /// sense is that the task is kept, every failure is caught and reported here, and the token cancels it.
    /// </remarks>
    public void Start(
        PlaylistSource source,
        bool onlyWhenStale,
        Func<Task> onImported,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onImported);

        if (IsImporting)
        {
            return;
        }

        IsImporting = true;
        _importTask = RunAsync(source, onlyWhenStale, onImported, cancellationToken);
    }

    /// <summary>
    /// Waits for an import in flight to finish, so the container that owns its database can be disposed.
    /// </summary>
    public async Task DrainAsync()
    {
        try
        {
            await _importTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Already reported by the import itself; failing to shut down over it would be worse.
            PlayerLog.GuideImportFailed(_logger, exception, string.Empty);
        }
    }

    private async Task RunAsync(
        PlaylistSource source,
        bool onlyWhenStale,
        Func<Task> onImported,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<GuideImportStage>(stage => _status.Text = Describe(stage));

        try
        {
            var result = onlyWhenStale
                ? await _guideImport
                    .ImportIfStaleAsync(source, progress, cancellationToken)
                    .ConfigureAwait(true)
                : await _guideImport
                    .ImportAsync(source, progress, cancellationToken)
                    .ConfigureAwait(true);

            _status.Text = Describe(result, source);

            if (result.Succeeded)
            {
                await onImported().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Either the window is closing or the source was removed. Neither is worth reporting.
        }
        catch (Exception exception)
        {
            PlayerLog.GuideImportFailed(_logger, exception, source.Name);
            _status.Text = "The programme guide could not be loaded. Details are in the log.";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private static string Describe(GuideImportStage stage)
    {
        return stage switch
        {
            GuideImportStage.Locating => "Looking for the programme guide...",
            GuideImportStage.Reading => "Reading the programme guide...",
            GuideImportStage.Matching => "Matching the guide to the channel list...",
            GuideImportStage.Pruning => "Tidying up the guide...",
            _ => "Working...",
        };
    }

    private static string Describe(GuideImportResult result, PlaylistSource source)
    {
        return result.Outcome switch
        {
            GuideImportOutcome.Imported when result.MatchedChannelCount == 0 =>
                "The guide loaded but matched none of the channels. Its channel names do not resemble "
                + "this subscription's.",
            GuideImportOutcome.Imported =>
                $"Guide loaded: {result.ProgrammeCount} programmes on {result.MatchedChannelCount} channels.",
            GuideImportOutcome.NoGuideAvailable => $"{source.Name} offers no programme guide.",
            GuideImportOutcome.Empty =>
                "The guide address answered with something that is not a programme guide.",
            _ => "The stored programme guide is already up to date.",
        };
    }
}
