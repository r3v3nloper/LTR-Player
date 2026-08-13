using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Playback;
using LTR.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Drives the main window.
/// </summary>
/// <remarks>
/// This now carries three responsibilities — managing sources, presenting the channel list, and
/// starting playback — and is a candidate for splitting along those lines. It is left whole for the
/// moment because the three share the selected-source state that would otherwise have to be threaded
/// between them, and the filtering rules, which are the part with real logic, already live in
/// <see cref="ChannelFilter"/> where they can be tested.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<ChannelItemViewModel> _channels = [];

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
    private string _status = "Add a subscription to begin.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSourceCommand))]
    private bool _isBusy;

    /// <summary>Whether the add-a-source form is showing instead of the channel list.</summary>
    [ObservableProperty]
    private bool _isAddingSource = true;

    [ObservableProperty]
    private PlaylistSource? _selectedSource;

    [ObservableProperty]
    private CategoryChoice _selectedCategory = CategoryChoice.All;

    [ObservableProperty]
    private string _channelFilterText = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    private ChannelItemViewModel? _selectedChannel;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    public MainViewModel(
        IServiceScopeFactory scopeFactory,
        IProviderRegistry providers,
        IPlaybackSession session,
        ILogger<MainViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _providers = providers;
        _session = session;
        _logger = logger;

        ChannelView = new CollectionViewSource { Source = _channels }.View;
        ChannelView.Filter = MatchesCurrentFilter;

        _session.StateChanged += OnPlaybackStateChanged;
    }

    public ObservableCollection<PlaylistSource> Sources { get; } = [];

    public ObservableCollection<CategoryChoice> Categories { get; } = [CategoryChoice.All];

    /// <summary>
    /// Filtered view over the loaded channels, and the only collection the UI binds to.
    /// </summary>
    /// <remarks>
    /// The backing store is a plain list, deliberately not an observable collection. A real
    /// subscription lists tens of thousands of channels, and adding them one at a time to an observable
    /// collection raises one change notification each, freezing the UI thread for seconds. The list is
    /// replaced wholesale and the view refreshed once. Virtualisation does not help here: it governs
    /// rendering, not population.
    /// </remarks>
    public ICollectionView ChannelView { get; }

    /// <summary>
    /// Loads the configured sources, so a restart lands straight in the channel list.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        var sources = await context.GetSourcesAsync(cancellationToken).ConfigureAwait(true);

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

    /// <summary>
    /// Hands the provider connection back before the window goes away.
    /// </summary>
    /// <remarks>
    /// Not a command, because it is not a user action and must not be cancellable: a subscription
    /// permitting a single connection is unusable for minutes if the player exits still holding one.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        await _session.StopAsync(CancellationToken.None).ConfigureAwait(true);
        NowPlaying = string.Empty;
    }

    partial void OnChannelFilterTextChanged(string value)
    {
        ChannelView.Refresh();
    }

    partial void OnSelectedCategoryChanged(CategoryChoice value)
    {
        ChannelView.Refresh();
    }

    partial void OnShowFavoritesOnlyChanged(bool value)
    {
        ChannelView.Refresh();
    }

    partial void OnSelectedSourceChanged(PlaylistSource? value)
    {
        if (value is null)
        {
            return;
        }

        _ = LoadCatalogueAsync(value, CancellationToken.None);
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

            Status = "Checking the subscription...";
            var provider = _providers.CreateProvider(source);
            var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(true);

            if (!account.IsUsable)
            {
                Status = DescribeUnusableAccount(account);
                return;
            }

            Status = "Reading what the source supports...";
            source.Capabilities = await _providers.GetCapabilityProbe(source)
                .ProbeAsync(source, cancellationToken)
                .ConfigureAwait(true);

            Status = "Loading the channel list...";
            var categories = await provider.FetchCategoriesAsync(ContentKind.Live, cancellationToken)
                .ConfigureAwait(true);
            var channels = await provider.FetchLiveChannelsAsync(cancellationToken).ConfigureAwait(true);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

            var sourceId = await context.AddSourceAsync(source, cancellationToken).ConfigureAwait(true);
            await context.ReconcileLiveCatalogueAsync(
                    sourceId,
                    categories,
                    channels,
                    DateTimeOffset.UtcNow,
                    cancellationToken)
                .ConfigureAwait(true);

            ClearNewSourceForm();
            Sources.Add(source);
            IsAddingSource = false;
            SelectedSource = source;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
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
            Status = "Refreshing the catalogue...";

            var provider = _providers.CreateProvider(source);
            var categories = await provider.FetchCategoriesAsync(ContentKind.Live, cancellationToken)
                .ConfigureAwait(true);
            var channels = await provider.FetchLiveChannelsAsync(cancellationToken).ConfigureAwait(true);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

            await context.ReconcileLiveCatalogueAsync(
                    source.Id,
                    categories,
                    channels,
                    DateTimeOffset.UtcNow,
                    cancellationToken)
                .ConfigureAwait(true);

            await LoadCatalogueAsync(source, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Refresh cancelled.";
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
            // Stopped first: the stream in flight belongs to the source about to disappear.
            await _session.StopAsync(CancellationToken.None).ConfigureAwait(true);
            NowPlaying = string.Empty;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
            await context.DeleteSourceAsync(source.Id, cancellationToken).ConfigureAwait(true);

            Sources.Remove(source);
            SelectedSource = Sources.Count > 0 ? Sources[0] : null;

            if (SelectedSource is null)
            {
                _channels.Clear();
                ChannelView.Refresh();
                IsAddingSource = true;
                Status = "Add a subscription to begin.";
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

    [RelayCommand(CanExecute = nameof(HasSelectedChannel))]
    private async Task ToggleFavoriteAsync(CancellationToken cancellationToken)
    {
        if (SelectedChannel is not { } channel)
        {
            return;
        }

        channel.IsFavorite = !channel.IsFavorite;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        await context.SetFavoriteAsync(channel.Id, channel.IsFavorite, cancellationToken).ConfigureAwait(true);

        // Only the filtered view needs rebuilding, and only when the change can move the row out of it.
        if (ShowFavoritesOnly)
        {
            ChannelView.Refresh();
        }
    }

    private bool HasSelectedChannel()
    {
        return SelectedChannel is not null;
    }

    /// <remarks>
    /// Concurrent execution is allowed deliberately. The generated command would otherwise report
    /// CanExecute as false while a stream is still opening, so zapping away from a slow channel would
    /// be silently ignored — and the playback session's supersession handling, which exists precisely
    /// to make rapid channel changes safe, would never be reachable from the UI.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedChannel))]
    private async Task PlaySelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedChannel is not { } item || SelectedSource is not { } source)
        {
            return;
        }

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, item.Channel);
        NowPlaying = item.Name;

        try
        {
            await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(true);
        }
        catch (PlaybackFailedException exception)
        {
            // Expected in daily use: providers take channels offline without notice.
            PlayerLog.ChannelUnplayable(_logger, exception, item.Name);
            Status = $"{item.Name} could not be played. The channel may be offline.";
            NowPlaying = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Zapping onwards cancels the open that was still in flight. That is the intended
            // behaviour of a channel change, not a failure — and left unhandled it surfaces as an
            // error dialog for an ordinary key press.
        }
    }

    [RelayCommand]
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        await _session.StopAsync(cancellationToken).ConfigureAwait(true);
        NowPlaying = string.Empty;
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
            if (!TryParseSourceAddress(PlaylistUrl, out var playlistUrl))
            {
                Status = "That is not a valid playlist address. Expected a URL or a file path.";
                return null;
            }

            return new M3uSource
            {
                Name = DescribeAddress(playlistUrl),
                PlaylistUrl = playlistUrl,
                CreatedUtc = DateTimeOffset.UtcNow,
            };
        }

        if (!Uri.TryCreate(PanelUrl.Trim(), UriKind.Absolute, out var baseUrl))
        {
            Status = "That is not a valid address. Expected something like http://host:8080";
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

    /// <summary>
    /// Accepts either a URL or a local path, since a playlist arrives as both.
    /// </summary>
    private static bool TryParseSourceAddress(string value, out Uri address)
    {
        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            address = parsed;
            return true;
        }

        // A bare Windows path is not an absolute URI, but it is what a user pastes.
        if (Path.IsPathFullyQualified(trimmed) && File.Exists(trimmed))
        {
            address = new Uri(trimmed);
            return true;
        }

        address = null!;
        return false;
    }

    private static string DescribeAddress(Uri address)
    {
        return address.IsFile ? Path.GetFileName(address.LocalPath) : address.Host;
    }

    private void ClearNewSourceForm()
    {
        PanelUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        PlaylistUrl = string.Empty;
    }

    private async Task LoadCatalogueAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        var storedChannels = await context.GetLiveChannelsAsync(source.Id, cancellationToken)
            .ConfigureAwait(true);
        var storedCategories = await context.GetLiveCategoriesAsync(source.Id, cancellationToken)
            .ConfigureAwait(true);

        _channels.Clear();
        _channels.AddRange(storedChannels.Select(channel => new ChannelItemViewModel(channel)));

        Categories.Clear();
        Categories.Add(CategoryChoice.All);

        foreach (var category in storedCategories)
        {
            Categories.Add(new CategoryChoice(category.Name, category.ExternalId));
        }

        // Reset rather than preserved: a category from the previous source means nothing here.
        SelectedCategory = CategoryChoice.All;
        ChannelView.Refresh();

        var favorites = _channels.Count(channel => channel.IsFavorite);
        PlayerLog.LoadedCatalogue(_logger, source.Name, _channels.Count, storedCategories.Count, favorites);

        Status = favorites > 0
            ? $"{_channels.Count} channels, {favorites} favourites."
            : $"{_channels.Count} channels. Pick one to start playback.";
    }

    private bool MatchesCurrentFilter(object item)
    {
        if (item is not ChannelItemViewModel channel)
        {
            return false;
        }

        var filter = new ChannelFilter(ChannelFilterText, SelectedCategory?.ExternalId, ShowFavoritesOnly);
        return filter.Matches(channel.Channel);
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.Current == PlaybackState.Playing)
        {
            Status = $"Playing {NowPlaying}";
        }
    }
}
