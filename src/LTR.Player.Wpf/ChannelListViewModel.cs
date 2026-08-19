using System.Collections;
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
    private readonly ILiveCatalogue _liveCatalogue;
    private readonly ISourceStore _sources;
    private readonly IGuideCatalogue _guide;
    private readonly TimeProvider _timeProvider;
    private readonly StatusLine _status;
    private readonly ILogger<ChannelListViewModel> _logger;
    private readonly List<ChannelItemViewModel> _channels = [];

    /// <summary>
    /// The same view <see cref="ChannelView"/> exposes, held in its concrete form.
    /// </summary>
    /// <remarks>
    /// <see cref="ICollectionView"/> offers no way to reach a row by position, so zapping would have to
    /// enumerate. <see cref="CollectionView"/> answers <c>Count</c>, <c>IndexOf</c> and <c>GetItemAt</c> over
    /// the filtered contents, which is what a key press needs and all it needs.
    /// </remarks>
    private readonly CollectionView _channelView;

    /// <summary>The source the list is currently showing, needed to ask for its guide again.</summary>
    private PlaylistSource? _source;

    /// <summary>
    /// The filter the current view is using. Rebuilt once per refresh rather than per row.
    /// </summary>
    private CatalogueFilter _activeFilter = CatalogueFilter.None;

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

    /// <remarks>
    /// Three faces of the store, because this list genuinely does three things: it presents the channels, it
    /// offers the categories the source declares, and it decorates each row with what is on now. The guide is
    /// separate from the channels for a reason worth keeping in view — the two are published by different
    /// parties and imported on different schedules, and most of a real subscription's channels have no guide
    /// entry at all.
    /// </remarks>
    public ChannelListViewModel(
        ILiveCatalogue liveCatalogue,
        ISourceStore sources,
        IGuideCatalogue guide,
        TimeProvider timeProvider,
        StatusLine status,
        ILogger<ChannelListViewModel> logger)
    {
        _liveCatalogue = liveCatalogue;
        _sources = sources;
        _guide = guide;
        _timeProvider = timeProvider;
        _status = status;
        _logger = logger;

        // A CollectionViewSource over a List<T> produces a ListCollectionView, which is a CollectionView.
        _channelView = (CollectionView)new CollectionViewSource { Source = _channels }.View;
        _channelView.Filter = MatchesCurrentFilter;

        ChannelView = _channelView;

        // Refilters in memory rather than asking the store again, which is the difference between this section
        // and the two that page: it holds its whole catalogue.
        Picker = new CategoryPickerViewModel(sources, ContentKind.Live)
        {
            SelectionChanged = RefreshChannelView,
        };
    }

    /// <summary>The category picker, which the markup binds to directly.</summary>
    public CategoryPickerViewModel Picker { get; }

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
            Picker.Clear();
            Replace([]);

            return;
        }

        var storedChannels = await _liveCatalogue.GetLiveChannelsAsync(source.Id, cancellationToken)
            .ConfigureAwait(true);

        // The picker loads and selects in one operation, in the picker — see CategoryPickerViewModel for the
        // order that used to be the caller's to get right.
        await Picker.ShowAsync(source, cancellationToken).ConfigureAwait(true);

        Replace(storedChannels);

        var favorites = _channels.Count(channel => channel.IsFavorite);
        PlayerLog.LoadedCatalogue(_logger, source.Name, _channels.Count, Picker.CategoryCount, favorites);

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
        var slices = await _guide.GetNowAndNextAsync(_source.Id, now, cancellationToken)
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

    /// <summary>
    /// Moves the selection one channel along, and reports whether it moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through the filtered view rather than the backing list, so zapping walks the channels the viewer can
    /// see. A category or a search having narrowed the list is exactly the set they mean by "the next
    /// channel".
    /// </para>
    /// <para>
    /// It stops at the ends rather than wrapping. Wrapping from the last channel to the first is
    /// indistinguishable from a zap that did nothing except by watching the picture, and a list this long
    /// makes that guess expensive: an unwanted wrap costs a stream open, which costs the account's one
    /// connection.
    /// </para>
    /// <para>
    /// Asked by index rather than enumerated. A collection view answers <c>IndexOf</c> and <c>GetItemAt</c>
    /// over its filtered contents directly, while copying it out — which is what this did — allocated a list
    /// of up to seventeen thousand rows on every key press. Note that it is <see cref="CollectionView"/> and
    /// not <see cref="IList"/> that offers them: the view does not implement <c>IList</c>, and assuming it
    /// did is what the zap tests caught.
    /// </para>
    /// </remarks>
    public bool SelectAdjacent(int offset)
    {
        if (_channelView.Count == 0)
        {
            return false;
        }

        // Nothing selected yet: the first channel is what "next" means, and the last is what "previous"
        // does — both land the viewer somewhere rather than refusing.
        var current = SelectedChannel is null
            ? (offset > 0 ? -1 : _channelView.Count)
            : _channelView.IndexOf(SelectedChannel);

        var target = current + offset;

        if (target < 0 || target >= _channelView.Count)
        {
            return false;
        }

        SelectedChannel = _channelView.GetItemAt(target) as ChannelItemViewModel;

        return SelectedChannel is not null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedChannel))]
    private async Task ToggleFavoriteAsync(CancellationToken cancellationToken)
    {
        if (SelectedChannel is not { } channel)
        {
            return;
        }

        channel.IsFavorite = !channel.IsFavorite;

        await _liveCatalogue.SetFavoriteAsync(channel.Id, channel.IsFavorite, cancellationToken)
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

    private void Replace(IReadOnlyList<Channel> channels)
    {
        _channels.Clear();
        _channels.AddRange(channels.Select(channel => new ChannelItemViewModel(channel)));

        HasGuide = false;

        // Reset rather than preserved: a row from the previous source means nothing here, and the row objects
        // themselves have just been replaced. The picker resets its own selection.
        SelectedChannel = null;
        RefreshChannelView();
    }

    partial void OnChannelFilterTextChanged(string value)
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

        _activeFilter = new CatalogueFilter(ChannelFilterText, Picker.RestrictedTo, ShowFavoritesOnly);
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
    /// <para>
    /// The filter is deliberately not constructed here. This runs once per channel per refresh, so at
    /// a realistic catalogue size building it inside meant tens of thousands of allocations for every
    /// keystroke in the search box.
    /// </para>
    /// <para>
    /// Tested against the row rather than the entity behind it, which is what allows the row to own its
    /// favourite state instead of mirroring it into the entity to keep the two agreeing.
    /// </para>
    /// </remarks>
    private bool MatchesCurrentFilter(object item)
    {
        if (item is not ChannelItemViewModel channel)
        {
            return false;
        }

        return _activeFilter.Matches(channel.Name, channel.CategoryExternalId, channel.IsFavorite);
    }
}
