using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Presents one source's channels: the filtered list, the category picker and the favourite marker.
/// </summary>
public sealed partial class ChannelListViewModel : ObservableObject
{
    private readonly ICatalogueStore _catalogue;
    private readonly TimeProvider _timeProvider;
    private readonly StatusLine _status;
    private readonly ILogger<ChannelListViewModel> _logger;
    private readonly List<ChannelItemViewModel> _channels = [];

    /// <summary>The source the list is currently showing, needed to ask for its guide again.</summary>
    private PlaylistSource? _source;

    /// <summary>
    /// The filter the current view is using. Rebuilt once per refresh rather than per row.
    /// </summary>
    private ChannelFilter _activeFilter = ChannelFilter.None;

    [ObservableProperty]
    private CategoryChoice _selectedCategory = CategoryChoice.All;

    [ObservableProperty]
    private string _channelFilterText = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesOnly;

    /// <summary>
    /// Whether any row has programme information, which is what decides whether the list makes room for
    /// it and whether the timeline is worth opening.
    /// </summary>
    [ObservableProperty]
    private bool _hasGuide;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    private ChannelItemViewModel? _selectedChannel;

    public ChannelListViewModel(
        ICatalogueStore catalogue,
        TimeProvider timeProvider,
        StatusLine status,
        ILogger<ChannelListViewModel> logger)
    {
        _catalogue = catalogue;
        _timeProvider = timeProvider;
        _status = status;
        _logger = logger;

        ChannelView = new CollectionViewSource { Source = _channels }.View;
        ChannelView.Filter = MatchesCurrentFilter;
    }

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
    /// Replaces the list with <paramref name="source"/>'s stored catalogue, or empties it when
    /// <paramref name="source"/> is <c>null</c>.
    /// </summary>
    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        _source = source;

        if (source is null)
        {
            Replace([], []);
            return;
        }

        var storedChannels = await _catalogue.GetLiveChannelsAsync(source.Id, cancellationToken)
            .ConfigureAwait(true);
        var storedCategories = await _catalogue.GetLiveCategoriesAsync(source.Id, cancellationToken)
            .ConfigureAwait(true);

        Replace(storedChannels, storedCategories);

        var favorites = _channels.Count(channel => channel.IsFavorite);
        PlayerLog.LoadedCatalogue(_logger, source.Name, _channels.Count, storedCategories.Count, favorites);

        _status.Text = favorites > 0
            ? $"{_channels.Count} channels, {favorites} favourites."
            : $"{_channels.Count} channels. Pick one to start playback.";

        await RefreshGuideAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Rereads what is on now for every row.
    /// </summary>
    /// <remarks>
    /// Called after a catalogue load, after a guide import, and on a timer while the window is open —
    /// "now" moves on its own, so a row left alone would keep showing a programme that has finished.
    /// </remarks>
    public async Task RefreshGuideAsync(CancellationToken cancellationToken)
    {
        if (_source is null || _channels.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var slices = await _catalogue.GetNowAndNextAsync(_source.Id, now, cancellationToken)
            .ConfigureAwait(true);

        // Indexed rather than searched: a source has thousands of rows and as many slices, and pairing
        // them by scanning would be quadratic.
        var slicesByChannelId = slices.ToDictionary(slice => slice.ChannelId);

        foreach (var row in _channels)
        {
            row.ShowGuide(slicesByChannelId.GetValueOrDefault(row.Id), now);
        }

        HasGuide = slices.Count > 0;
    }

    /// <summary>
    /// The channels the filter currently admits, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// Exists so the timeline can show the same selection the list does — the view model composing the two
    /// passes this across, which is what keeps them from referencing each other.
    /// </remarks>
    public IReadOnlyList<Channel> VisibleChannels
    {
        get
        {
            var visible = new List<Channel>();

            foreach (var item in ChannelView)
            {
                if (item is ChannelItemViewModel row)
                {
                    visible.Add(row.Channel);
                }
            }

            return visible;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedChannel))]
    private async Task ToggleFavoriteAsync(CancellationToken cancellationToken)
    {
        if (SelectedChannel is not { } channel)
        {
            return;
        }

        channel.IsFavorite = !channel.IsFavorite;

        await _catalogue.SetFavoriteAsync(channel.Id, channel.IsFavorite, cancellationToken)
            .ConfigureAwait(true);

        // Only the filtered view needs rebuilding, and only when the change can move the row out of it.
        // Un-favouriting while the favourites filter is on legitimately removes the row, and the
        // selection goes with it — that case is handled by the restore only reselecting rows that still
        // qualify.
        if (ShowFavoritesOnly)
        {
            RefreshChannelView();
        }
    }

    private bool HasSelectedChannel()
    {
        return SelectedChannel is not null;
    }

    private void Replace(IReadOnlyList<Channel> channels, IReadOnlyList<Category> categories)
    {
        _channels.Clear();
        _channels.AddRange(channels.Select(channel => new ChannelItemViewModel(channel)));

        HasGuide = false;

        Categories.Clear();
        Categories.Add(CategoryChoice.All);

        foreach (var category in categories)
        {
            Categories.Add(new CategoryChoice(category.Name, category.ExternalId));
        }

        // Both reset rather than preserved: a category and a row from the previous source mean nothing
        // here, and the row objects themselves have just been replaced.
        SelectedChannel = null;
        SelectedCategory = CategoryChoice.All;
        RefreshChannelView();
    }

    partial void OnChannelFilterTextChanged(string value)
    {
        RefreshChannelView();
    }

    partial void OnSelectedCategoryChanged(CategoryChoice value)
    {
        RefreshChannelView();
    }

    partial void OnShowFavoritesOnlyChanged(bool value)
    {
        RefreshChannelView();
    }

    /// <summary>
    /// Reapplies the filter, keeping the current row selected when it still qualifies.
    /// </summary>
    /// <remarks>
    /// Refreshing a collection view raises a reset, and the list box drops its selection in response.
    /// Without restoring it, changing category or typing in the search box silently deselects whatever
    /// the user had picked — disabling the favourite command while a channel is still playing, which is
    /// exactly the state it looked like a bug from.
    /// </remarks>
    private void RefreshChannelView()
    {
        var previouslySelected = SelectedChannel;

        _activeFilter = new ChannelFilter(ChannelFilterText, SelectedCategory?.ExternalId, ShowFavoritesOnly);
        ChannelView.Refresh();

        if (previouslySelected is not null && MatchesCurrentFilter(previouslySelected))
        {
            SelectedChannel = previouslySelected;
        }
    }

    /// <summary>
    /// Tests one row against the filter built for the current refresh.
    /// </summary>
    /// <remarks>
    /// The filter is deliberately not constructed here. This runs once per channel per refresh, so at
    /// a realistic catalogue size building it inside meant tens of thousands of allocations for every
    /// keystroke in the search box.
    /// </remarks>
    private bool MatchesCurrentFilter(object item)
    {
        if (item is not ChannelItemViewModel channel)
        {
            return false;
        }

        return _activeFilter.Matches(channel.Channel);
    }
}
