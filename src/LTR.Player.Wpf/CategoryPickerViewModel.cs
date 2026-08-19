using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// One category picker: its entries, which one is chosen, and the star that pins one to the top.
/// </summary>
/// <remarks>
/// <para>
/// One object per section, held by the section and bound to directly, replacing ~25 identical lines in each of
/// two view models and the static helper they shared. The state and the command are together because they were
/// never separable: the guard reads the selection, the pin reorders the collection, and the collection is what
/// the selection points into.
/// </para>
/// <para>
/// <b>What this exists to make impossible.</b> Filling the entries and choosing one is a single operation here
/// (<see cref="ShowAsync"/>), because as two it had an unwritten rule about their order and the rule was
/// violated: emptying a bound collection makes a ComboBox write a null selection back through the binding, so a
/// selection assigned before the refill is discarded — the control renders blank while the filter, reading the
/// same null, still admits every category. The list looks right and the picker looks broken. No caller can get
/// that order wrong now because no caller states it.
/// </para>
/// <para>
/// <see cref="SelectedCategory"/> stays nullable for the same reason, and that is not defensive: the ComboBox
/// really does write null, and <see cref="HasPinnableCategory"/> is asked on every selection change, so a
/// non-null declaration here crashed the window on startup rather than warning about anything.
/// </para>
/// </remarks>
public sealed partial class CategoryPickerViewModel : ObservableObject
{
    private readonly ISourceStore _sources;
    private readonly ContentKind _kind;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCategoryFavoriteCommand))]
    private CategoryChoice? _selectedCategory = CategoryChoice.All;

    /// <param name="kind">
    /// Which kind of category to list. A panel numbers them per section, so <c>58</c> is a live category and a
    /// film category at once and the question has to carry its kind.
    /// </param>
    public CategoryPickerViewModel(ISourceStore sources, ContentKind kind)
    {
        _sources = sources;
        _kind = kind;
    }

    public ObservableCollection<CategoryChoice> Categories { get; } = [CategoryChoice.All];

    /// <summary>How many real categories are listed — the unrestricted entry is not one of them.</summary>
    public int CategoryCount => Categories.Count - 1;

    /// <summary>The provider category to restrict to, or null for no restriction.</summary>
    public string? RestrictedTo => SelectedCategory?.ExternalId;

    /// <summary>
    /// Run when the viewer picks a different category, so the section can show what that admits.
    /// </summary>
    /// <remarks>
    /// A callback assigned by the owner rather than an event it subscribes to, because what follows differs and
    /// only the owner knows which: the channel list refilters in memory, a catalogue section asks the store
    /// again. Inert until assigned, as <see cref="PlaybackCoordinator.ProgressRecorded"/> is.
    /// </remarks>
    public Action SelectionChanged { get; set; } = () => { };

    /// <summary>
    /// Lists <paramref name="source"/>'s categories and selects the unrestricted entry, in that order.
    /// </summary>
    /// <remarks>
    /// Loads them itself rather than being handed them, which is what keeps the order out of the callers'
    /// hands. Both of them had it written out, and both had the same comment explaining it.
    /// </remarks>
    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            Clear();
            return;
        }

        var categories = await _sources
            .GetCategoriesAsync(source.Id, _kind, cancellationToken)
            .ConfigureAwait(true);

        Fill(categories);
    }

    /// <summary>Leaves the picker holding nothing but the unrestricted entry.</summary>
    public void Clear()
    {
        Fill([]);
    }

    private void Fill(IReadOnlyList<Category> categories)
    {
        Categories.Clear();
        Categories.Add(CategoryChoice.All);

        for (var order = 0; order < categories.Count; order++)
        {
            var category = categories[order];

            Categories.Add(new CategoryChoice(
                category.Name,
                category.ExternalId,
                category.Id,
                category.IsFavorite,
                order));
        }

        // Selected last, always, which is the whole point of this being one method.
        SelectedCategory = CategoryChoice.All;

        OnPropertyChanged(nameof(CategoryCount));
    }

    /// <summary>
    /// Pins the chosen category to the top, moves it to where that puts it, and records it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Moved rather than re-added, and the entry itself is never replaced. Both keep the selection pointing at
    /// the same object, so pinning the category being watched does not change what is on screen — which is the
    /// whole reason someone pins the one they are watching.
    /// </para>
    /// <para>
    /// No reload follows either: a pin says where a category is listed and nothing about what matches it, so
    /// the rows the viewer is looking at do not move under them. Where a pinned one goes has to agree with
    /// where the store puts it on the next load, which is why <see cref="CategoryChoice.Rank"/> restates the
    /// store's own ordering rather than each picker sorting to taste.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasPinnableCategory))]
    private async Task ToggleCategoryFavoriteAsync(CancellationToken cancellationToken)
    {
        // The unrestricted entry stands for no category and has no identity to write a pin against. Reaching
        // here with it means the guard is not doing its job, so this refuses rather than writing one.
        if (SelectedCategory is not { IsPinnable: true } choice)
        {
            return;
        }

        choice.IsFavorite = !choice.IsFavorite;
        Reorder();

        await _sources.SetCategoryFavoriteAsync(choice.Id, choice.IsFavorite, cancellationToken)
            .ConfigureAwait(true);
    }

    private bool HasPinnableCategory()
    {
        return SelectedCategory is { IsPinnable: true };
    }

    /// <summary>
    /// Puts the picker back in rank order, one move at a time.
    /// </summary>
    /// <remarks>
    /// A move rather than a clear and refill, because a refill is what empties the ComboBox for an instant and
    /// takes the selection with it. Insertion sort over a couple of hundred entries, of which one has changed
    /// rank, so it settles in a single pass in every case that occurs.
    /// </remarks>
    private void Reorder()
    {
        for (var index = 1; index < Categories.Count; index++)
        {
            var moving = Categories[index];
            var target = index;

            while (target > 1 && Categories[target - 1].Rank.CompareTo(moving.Rank) > 0)
            {
                target--;
            }

            if (target != index)
            {
                Categories.Move(index, target);
            }
        }
    }

    partial void OnSelectedCategoryChanged(CategoryChoice? value)
    {
        OnPropertyChanged(nameof(RestrictedTo));
        SelectionChanged();
    }
}
