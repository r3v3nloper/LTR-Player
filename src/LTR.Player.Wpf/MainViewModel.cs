using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// Drives the main window: source setup, the channel list and starting playback.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IContentProviderFactory _providerFactory;
    private readonly IProviderCapabilityProbe _capabilityProbe;
    private readonly IEnumerable<IStreamUrlResolver> _resolvers;
    private readonly IPlaybackSession _session;
    private readonly ILogger<MainViewModel> _logger;
    private readonly List<Channel> _channels = [];

    private PlaylistSource? _source;

    // Each field the Connect command guards on must re-evaluate that guard when it changes. Without
    // NotifyCanExecuteChangedFor the generated command evaluates CanConnect once, at construction
    // time when every field is still empty, and the button stays disabled no matter what is typed.
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
    private string _status = "Add a subscription to begin.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasSource;

    [ObservableProperty]
    private string _channelFilter = string.Empty;

    [ObservableProperty]
    private Channel? _selectedChannel;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    public MainViewModel(
        IServiceScopeFactory scopeFactory,
        IContentProviderFactory providerFactory,
        IProviderCapabilityProbe capabilityProbe,
        IEnumerable<IStreamUrlResolver> resolvers,
        IPlaybackSession session,
        ILogger<MainViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _providerFactory = providerFactory;
        _capabilityProbe = capabilityProbe;
        _resolvers = resolvers;
        _session = session;
        _logger = logger;

        ChannelView = new CollectionViewSource { Source = _channels }.View;
        ChannelView.Filter = MatchesFilter;

        _session.StateChanged += OnPlaybackStateChanged;
    }

    /// <summary>
    /// Filtered view over the loaded channels, and the only collection the UI binds to.
    /// </summary>
    /// <remarks>
    /// The backing store is a plain list, deliberately not an observable collection. A real
    /// subscription lists tens of thousands of channels, and adding them one at a time to an
    /// observable collection raises one change notification each — every one of them walked through
    /// the view and the list box, freezing the UI thread for seconds. The list is replaced wholesale
    /// and the view refreshed once, which is a single reset regardless of size. Virtualisation does
    /// not help here: it governs rendering, not population.
    /// </remarks>
    public ICollectionView ChannelView { get; }

    /// <summary>
    /// Loads an already configured source, so a restart lands straight in the channel list.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        var sources = await context.GetSourcesAsync(cancellationToken).ConfigureAwait(true);

        // Single-source for now; M2 introduces switching between several.
        if (sources.Count == 0)
        {
            return;
        }

        _source = sources[0];

        HasSource = true;
        PanelUrl = _source.Endpoint.AbsoluteUri;
        await LoadChannelsFromStoreAsync(context, cancellationToken).ConfigureAwait(true);
    }

    partial void OnChannelFilterChanged(string value)
    {
        ChannelView.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            if (!Uri.TryCreate(PanelUrl, UriKind.Absolute, out var baseUrl))
            {
                Status = "That is not a valid address. Expected something like http://host:8080";
                return;
            }

            var source = new XtreamSource
            {
                Name = baseUrl.Host,
                BaseUrl = baseUrl,
                Username = Username,
                Password = Password,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            Status = "Checking the subscription...";
            var provider = _providerFactory.Create(source);
            var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(true);

            if (!account.IsUsable)
            {
                Status = DescribeUnusableAccount(account);
                return;
            }

            Status = "Reading what the panel supports...";
            source.Capabilities = await _capabilityProbe.ProbeAsync(source, cancellationToken)
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

            _source = source;
            HasSource = true;
            Password = string.Empty;

            await LoadChannelsFromStoreAsync(context, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(PanelUrl)
            && !string.IsNullOrWhiteSpace(Username)
            && !string.IsNullOrWhiteSpace(Password);
    }

    /// <remarks>
    /// Concurrent execution is allowed deliberately. The generated command would otherwise report
    /// CanExecute as false while a stream is still opening, so zapping away from a slow channel would
    /// be silently ignored — and the playback session's supersession handling, which exists precisely
    /// to make rapid channel changes safe, would never be reachable from the UI.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlaySelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedChannel is null || _source is null)
        {
            return;
        }

        var resolver = _resolvers.FirstOrDefault(candidate => candidate.Supports(_source));

        if (resolver is null)
        {
            Status = "No resolver handles this source type.";
            return;
        }

        var request = resolver.ResolveLive(_source, SelectedChannel);
        NowPlaying = SelectedChannel.Name;

        try
        {
            await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(true);
        }
        catch (PlaybackFailedException exception)
        {
            // Expected in daily use: providers take channels offline without notice.
            PlayerLog.ChannelUnplayable(_logger, exception, SelectedChannel.Name);
            Status = $"{SelectedChannel.Name} could not be played. The channel may be offline.";
            NowPlaying = string.Empty;
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
            AccountStatus.AuthenticationFailed => "The panel rejected these credentials.",
            AccountStatus.Expired => "This subscription has expired.",
            AccountStatus.Banned => "This subscription has been disabled by the provider.",
            _ => "The panel replied, but reported a status this player does not recognise.",
        };
    }

    private async Task LoadChannelsFromStoreAsync(LtrDbContext context, CancellationToken cancellationToken)
    {
        if (_source is null)
        {
            return;
        }

        var stored = await context.GetLiveChannelsAsync(_source.Id, cancellationToken).ConfigureAwait(true);

        _channels.Clear();
        _channels.AddRange(stored);
        ChannelView.Refresh();

        Status = $"{_channels.Count} channels. Pick one to start playback.";
    }

    private bool MatchesFilter(object item)
    {
        if (string.IsNullOrWhiteSpace(ChannelFilter))
        {
            return true;
        }

        return item is Channel channel
            && channel.Name.Contains(ChannelFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.Current == PlaybackState.Playing)
        {
            Status = $"Playing {NowPlaying}";
        }
    }
}
