using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Lists what the viewer is part-way through, across films and episodes alike.
/// </summary>
/// <remarks>
/// Reloaded rather than kept in step: the list changes when playback stops, and recomputing twenty rows
/// from one query is simpler and less fallible than keeping two collections and a database in agreement.
/// </remarks>
public sealed partial class ContinueWatchingViewModel : ObservableObject
{
    /// <summary>
    /// How many entries to show. A continue-watching list is a shortcut back to two or three things, not
    /// a viewing history.
    /// </summary>
    public const int EntryLimit = 24;

    private readonly IVodCatalogue _catalogue;
    private readonly IWatchProgressStore _progress;

    private PlaylistSource? _source;

    [ObservableProperty]
    private ContinueWatchingEntry? _selectedEntry;

    public ContinueWatchingViewModel(IVodCatalogue catalogue, IWatchProgressStore progress)
    {
        _catalogue = catalogue;
        _progress = progress;
    }

    public ObservableCollection<ContinueWatchingEntry> Entries { get; } = [];

    /// <summary>Whether anything is part-watched, which decides whether the section is worth opening.</summary>
    public bool HasEntries => Entries.Count > 0;

    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        _source = source;
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the film an entry refers to, or <see langword="null"/> when it has left the catalogue.
    /// </summary>
    /// <remarks>
    /// Here rather than in the shell because this section owns the store access for its own rows, and the
    /// shell needs the entity itself to build a playback address — an entry carries only what a row shows.
    /// </remarks>
    public Task<VodItem?> FindMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        return _catalogue.GetMovieAsync(movieId, cancellationToken);
    }

    public Task<Episode?> FindEpisodeAsync(int episodeId, CancellationToken cancellationToken)
    {
        return _catalogue.GetEpisodeAsync(episodeId, cancellationToken);
    }

    /// <summary>
    /// Forgets where the viewer got to, taking the entry off the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the film that did not hold their attention. Without this the list keeps offering to resume
    /// something nobody intends to return to, and it is the one list in the window whose whole value is that
    /// everything on it is worth carrying on with.
    /// </para>
    /// <para>
    /// Note what it does *not* do. It does not mark the item watched, because the viewer did not watch it —
    /// a film labelled "Watched" that nobody has seen is a worse lie than a stale resume point — and it does
    /// not record a viewing, which is why it no longer goes through a <see cref="WatchOutcome"/>.
    /// </para>
    /// </remarks>
    public async Task ForgetAsync(ContinueWatchingEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Kind == ContentKind.Movie)
        {
            await _progress
                .ForgetMovieProgressAsync(entry.ItemId, cancellationToken)
                .ConfigureAwait(true);
        }
        else
        {
            await _progress
                .ForgetEpisodeProgressAsync(entry.ItemId, cancellationToken)
                .ConfigureAwait(true);
        }

        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var previouslySelected = SelectedEntry;

        Entries.Clear();

        if (_source is not null)
        {
            var entries = await _progress
                .GetContinueWatchingAsync(_source.Id, EntryLimit, cancellationToken)
                .ConfigureAwait(true);

            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }

        // Restored by identity rather than by reference: the entries are new objects after a reload, and a
        // list box that silently drops its selection disables whatever was about to be played.
        SelectedEntry = Entries.FirstOrDefault(entry =>
            previouslySelected is not null
            && entry.Kind == previouslySelected.Kind
            && entry.ItemId == previouslySelected.ItemId);

        OnPropertyChanged(nameof(HasEntries));
    }
}
