using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// A catalogue section that answers a search: a category picker, a search box, a bounded page of rows and a
/// line saying what is being shown.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the film and series sections, which had this whole shape twice — including the ordering rule
/// below, whose absence produced the same blank-picker defect in both because the code was in both.
/// </para>
/// <para>
/// Unlike the channel list, a section does not hold its catalogue. The subscription this was built against
/// lists 66,447 films against 17,156 channels, and nobody browses that by scrolling: the store filters and
/// counts, and what is left out is stated rather than silently dropped.
/// </para>
/// <para>
/// Deriving classes supply the three things that genuinely differ — which kind of category to load, how to
/// ask the store, and how to word the count — and nothing else.
/// </para>
/// </remarks>
/// <typeparam name="TRow">The row type this section presents.</typeparam>
public abstract partial class CatalogueSectionViewModel<TRow> : ObservableObject, ICategoryPickerSection
    where TRow : class
{
    /// <summary>
    /// How many results a search shows.
    /// </summary>
    /// <remarks>
    /// Enough that a search for a title lands it on screen, small enough that the list stays instant.
    /// </remarks>
    public const int ResultLimit = 200;

    private readonly ISourceStore _sources;
    private readonly IVodCatalogue _catalogue;

    /// <summary>
    /// The category the picker is on, or null while the picker is being refilled.
    /// </summary>
    /// <remarks>
    /// Nullable because a ComboBox writes null here whenever the bound collection is emptied — the same
    /// instant the ordering rule below is written against. Every read of it has to allow for that, the pin's
    /// command guard included, because that guard is asked on every selection change.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCategoryFavoriteCommand))]
    private CategoryChoice? _selectedCategory = CategoryChoice.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>What the section is showing, or why it is showing nothing.</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <param name="sources">
    /// Only for the category picker. Categories are numbered per section by the panel, so the question
    /// carries its kind and belongs to the source rather than to either catalogue.
    /// </param>
    protected CatalogueSectionViewModel(ISourceStore sources, IVodCatalogue catalogue)
    {
        _sources = sources;
        _catalogue = catalogue;
    }

    public ObservableCollection<CategoryChoice> Categories { get; } = [CategoryChoice.All];

    public ObservableCollection<TRow> Rows { get; } = [];

    /// <summary>Whether the source offers this section at all, which decides whether it can be opened.</summary>
    public bool IsAvailable => Source is not null && SupportsSection(Source);

    /// <summary>The source being shown, or null while the section is detached.</summary>
    protected PlaylistSource? Source { get; private set; }

    protected IVodCatalogue Catalogue => _catalogue;

    /// <summary>
    /// Points the section at a source, loading its categories and a first page of results.
    /// </summary>
    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        // Detached first, and that ordering is deliberate: clearing the criteria below raises the property
        // changes the shell answers with a search, and a section with no source answers nothing. Otherwise
        // switching subscriptions would run three searches to display one.
        Source = null;

        ClearSelection();
        SearchText = string.Empty;

        Rows.Clear();
        CategoryPicker.Fill(Categories, []);

        if (source is null)
        {
            SelectedCategory = CategoryChoice.All;
            Notice = string.Empty;
            OnPropertyChanged(nameof(IsAvailable));

            return;
        }

        // Read through the parameter rather than the property, which is still null: the picker has to be
        // complete before anything selects in it.
        var categories = await _sources
            .GetCategoriesAsync(source.Id, CategoryKind, cancellationToken)
            .ConfigureAwait(true);

        CategoryPicker.Fill(Categories, categories);

        // Selected last, and that is not cosmetic. Emptying the bound collection makes the ComboBox write a
        // null selection back through the binding, so a selection assigned before the picker is refilled is
        // discarded and the control renders blank — while the filter, reading the same null, still admits
        // every category. The list looks right and the picker looks broken.
        SelectedCategory = CategoryChoice.All;

        Source = source;
        OnPropertyChanged(nameof(IsAvailable));

        await SearchAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reruns the search with the current criteria.
    /// </summary>
    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (Source is not { } source)
        {
            return;
        }

        var filter = new CatalogueFilter(SearchText, SelectedCategory?.ExternalId);
        var page = await SearchAsync(source.Id, filter, cancellationToken).ConfigureAwait(true);

        Rows.Clear();

        foreach (var row in page.Items)
        {
            Rows.Add(row);
        }

        Notice = Describe(page, filter);
    }

    /// <summary>
    /// Pins the chosen category to the top of the picker, or lets it fall back.
    /// </summary>
    /// <remarks>
    /// No search follows: a pin says where a category is listed and nothing about what matches it, so the
    /// results stay as they are.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasPinnableCategory))]
    public Task ToggleCategoryFavoriteAsync(CancellationToken cancellationToken)
    {
        return SelectedCategory is { } choice
            ? CategoryPicker.ToggleFavoriteAsync(Categories, choice, _sources, cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>Which kind of category this section's picker lists.</summary>
    protected abstract ContentKind CategoryKind { get; }

    /// <summary>
    /// The plural noun used in the count — "films", "series" — so the wording reads naturally without each
    /// section writing all four sentences.
    /// </summary>
    protected abstract string EntryNoun { get; }

    protected abstract Task<CataloguePage<TRow>> SearchAsync(
        int sourceId,
        CatalogueFilter filter,
        CancellationToken cancellationToken);

    protected abstract bool SupportsSection(PlaylistSource source);

    /// <summary>
    /// Clears whatever the section holds about the current selection, before a new source is adopted.
    /// </summary>
    protected virtual void ClearSelection()
    {
    }

    private bool HasPinnableCategory()
    {
        return SelectedCategory is { IsPinnable: true };
    }

    private string Describe(CataloguePage<TRow> page, CatalogueFilter filter)
    {
        if (page.TotalMatching == 0)
        {
            return filter.IsActive
                ? $"No {EntryNoun} match. Try fewer words, or another category."
                : $"This subscription's {EntryNoun} catalogue is empty. Refresh the source to fetch it.";
        }

        return page.IsTruncated
            ? $"Showing {page.Items.Count} of {page.TotalMatching} {EntryNoun}. "
                + "Narrow the search to see the rest."
            : $"{page.TotalMatching} {EntryNoun}.";
    }
}
