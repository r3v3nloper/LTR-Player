using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Adding, selecting, refreshing and removing the configured subscriptions.
/// </summary>
public sealed partial class SourceManagementViewModel : ObservableObject
{
    private readonly ICatalogueStore _catalogue;
    private readonly ISourceImportService _import;
    private readonly StatusLine _status;
    private readonly ILogger<SourceManagementViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private NewSourceProtocol _newSourceProtocol = NewSourceProtocol.Xtream;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _panelUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _playlistUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private bool _isBusy;

    /// <summary>Whether the add-a-source form is showing instead of the channel list.</summary>
    [ObservableProperty]
    private bool _isAddingSource = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private PlaylistSource? _selectedSource;

    public SourceManagementViewModel(
        ICatalogueStore catalogue,
        ISourceImportService import,
        StatusLine status,
        ILogger<SourceManagementViewModel> logger)
    {
        _catalogue = catalogue;
        _import = import;
        _status = status;
        _logger = logger;
    }

    /// <summary>
    /// What the rest of the shell has to do when the selected source changes.
    /// </summary>
    /// <remarks>
    /// Assigned once, by the view model composing this one, which is the only thing able to perform
    /// those operations. Inert until then, so nothing here has to check it for null.
    /// </remarks>
    public ISourceCoordinator Coordinator { get; set; } = InertCoordinator.Instance;

    public ObservableCollection<PlaylistSource> Sources { get; } = [];

    /// <summary>
    /// Loads the configured sources, so a restart lands straight in the channel list.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var sources = await _catalogue.GetSourcesAsync(cancellationToken).ConfigureAwait(true);

        Sources.Clear();

        foreach (var source in sources)
        {
            Sources.Add(source);
        }

        PlayerLog.LoadedSources(_logger, Sources.Count);

        if (Sources.Count == 0)
        {
            return;
        }

        IsAddingSource = false;

        // Assigning this triggers the catalogue load through OnSelectedSourceChanged.
        SelectedSource = Sources[0];
    }

    partial void OnSelectedSourceChanged(PlaylistSource? value)
    {
        if (value is null)
        {
            return;
        }

        // A property setter cannot await, so the load is started and left to report its own failure. The
        // coordinator swallows everything, cancellation included — which it has to, precisely because this
        // call is not awaited and an escaping exception would go unobserved. The list would otherwise
        // simply stay empty with nothing said about why.
        _ = Coordinator.ShowCatalogueAsync(value, CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var source = BuildNewSource();

            if (source is null)
            {
                return;
            }

            var result = await _import.ImportAsync(source, CreateProgressReporter(), cancellationToken)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                _status.Text = DescribeUnusableAccount(result.Account);
                return;
            }

            ClearNewSourceForm();
            Sources.Add(source);
            IsAddingSource = false;
            SelectedSource = source;

            // After the selection, so the channel list is already on screen when the guide starts
            // arriving behind it.
            Coordinator.CatalogueImported(source);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect()
    {
        if (IsBusy)
        {
            return false;
        }

        return NewSourceProtocol switch
        {
            NewSourceProtocol.Xtream => !string.IsNullOrWhiteSpace(PanelUrl)
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password),
            NewSourceProtocol.M3uPlaylist => !string.IsNullOrWhiteSpace(PlaylistUrl),
            _ => false,
        };
    }

    /// <summary>
    /// Re-fetches the selected source's catalogue.
    /// </summary>
    /// <remarks>
    /// Goes through the same reconciliation as the initial import, which is what preserves the user's
    /// favourites while refreshing everything the provider owns.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelectedSource))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (SelectedSource is not { } source)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _import.RefreshAsync(source, CreateProgressReporter(), cancellationToken)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                _status.Text = DescribeUnusableAccount(result.Account);
                return;
            }

            // Awaited rather than left to the selection path: the source has not changed, and the busy
            // flag has to stay raised until the rebuilt list is on screen.
            await Coordinator.ShowCatalogueAsync(source, cancellationToken).ConfigureAwait(true);

            // Deliberately not awaited by the coordinator either: a guide download must not keep the
            // refresh busy for the minutes it takes.
            Coordinator.CatalogueImported(source);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Refresh cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnSelectedSource))]
    private async Task RemoveSourceAsync(CancellationToken cancellationToken)
    {
        if (SelectedSource is not { } source)
        {
            return;
        }

        IsBusy = true;

        try
        {
            // Released first: the stream in flight belongs to the source about to disappear.
            await Coordinator.ReleasePlaybackAsync(CancellationToken.None).ConfigureAwait(true);

            await _catalogue.DeleteSourceAsync(source.Id, cancellationToken).ConfigureAwait(true);

            Sources.Remove(source);
            SelectedSource = Sources.Count > 0 ? Sources[0] : null;

            if (SelectedSource is null)
            {
                await Coordinator.ShowCatalogueAsync(null, cancellationToken).ConfigureAwait(true);
                IsAddingSource = true;
                _status.Text = StatusLine.NoSourcesConfigured;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanOperateOnSelectedSource()
    {
        return !IsBusy && SelectedSource is not null;
    }

    [RelayCommand]
    private void ShowAddSource()
    {
        ClearNewSourceForm();
        IsAddingSource = true;
    }

    [RelayCommand]
    private void CancelAddSource()
    {
        ClearNewSourceForm();

        // Only dismissable when there is something to go back to.
        IsAddingSource = Sources.Count == 0;
    }

    /// <summary>
    /// Turns import stages into status text.
    /// </summary>
    /// <remarks>
    /// The service reports stages, not sentences, so the wording stays here where the audience is known.
    /// Progress arrives on whichever thread the service happens to be on, and <see cref="Progress{T}"/>
    /// marshals the callback back to the thread that created it — the UI thread, since the view model is
    /// constructed there.
    /// </remarks>
    private Progress<SourceImportStage> CreateProgressReporter()
    {
        return new Progress<SourceImportStage>(stage => _status.Text = Describe(stage));
    }

    private static string Describe(SourceImportStage stage)
    {
        return stage switch
        {
            SourceImportStage.Authenticating => "Checking the subscription...",
            SourceImportStage.Probing => "Reading what the source supports...",
            SourceImportStage.FetchingCatalogue => "Loading the channel list...",
            SourceImportStage.Storing => "Storing the catalogue...",
            _ => "Working...",
        };
    }

    private static string DescribeUnusableAccount(ProviderAccount account)
    {
        return account.Status switch
        {
            AccountStatus.AuthenticationFailed =>
                "The source rejected these details, or its playlist could not be retrieved.",
            AccountStatus.Expired => "This subscription has expired.",
            AccountStatus.Banned => "This subscription has been disabled by the provider.",
            _ => "The source replied, but reported a status this player does not recognise.",
        };
    }

    private PlaylistSource? BuildNewSource()
    {
        if (NewSourceProtocol == NewSourceProtocol.M3uPlaylist)
        {
            if (!SourceAddress.TryParse(PlaylistUrl, out var playlistUrl))
            {
                _status.Text = "That is not a valid playlist address. Expected a URL or an existing file.";
                return null;
            }

            return new M3uSource
            {
                Name = SourceAddress.Describe(playlistUrl),
                PlaylistUrl = playlistUrl,
                CreatedUtc = DateTimeOffset.UtcNow,
            };
        }

        if (!SourceAddress.TryParseWebAddress(PanelUrl, out var baseUrl))
        {
            _status.Text = "That is not a valid panel address. Expected something like http://host:8080";
            return null;
        }

        return new XtreamSource
        {
            Name = baseUrl.Host,
            BaseUrl = baseUrl,
            Username = Username.Trim(),
            Password = Password,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
    }

    private void ClearNewSourceForm()
    {
        PanelUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        PlaylistUrl = string.Empty;
    }

    /// <summary>
    /// Stands in until a coordinator is supplied, so nothing above has to test it for null.
    /// </summary>
    private sealed class InertCoordinator : ISourceCoordinator
    {
        public static InertCoordinator Instance { get; } = new();

        public Task ShowCatalogueAsync(PlaylistSource? source, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReleasePlaybackAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void CatalogueImported(PlaylistSource source)
        {
        }
    }
}
