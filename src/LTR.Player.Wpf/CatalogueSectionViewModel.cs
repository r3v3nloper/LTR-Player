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
public abstract partial class CatalogueSectionViewModel<TRow> : ObservableObject
    where TRow : class
{
    /// <summary>
    /// How many results a search shows.
    /// </summary>
    /// <remarks>
    /// Enough that a search for a title lands it on screen, small enough that the list stays instant.
    /// </remarks>
    public const int ResultLimit = 200;

    private readonly IVodCatalogue _catalogue;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>What the section is showing, or why it is showing nothing.</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <param name="sources">
    /// Only for the category picker. Categories are numbered per section by the panel, so the question
    /// carries its kind and belongs to the source rather than to either catalogue.
    /// </param>
    /// <param name="categoryKind">
    /// Which kind of category this section's picker lists. A constructor parameter rather than the abstract
    /// property it replaced, because the picker is built here and reading an override from a base constructor
    /// is the pattern that reads a field the derived class has not assigned yet.
    /// </param>
    protected CatalogueSectionViewModel(
        ISourceStore sources,
        IVodCatalogue catalogue,
        ContentKind categoryKind)
    {
        _catalogue = catalogue;

        Picker = new CategoryPickerViewModel(sources, categoryKind)
        {
            // A different category means different rows, and only the shell can start the search that reads
            // them — so this announces the change and the shell answers it, exactly as the search box does.
            SelectionChanged = () => OnPropertyChanged(nameof(Criteria)),
        };
    }

    /// <summary>The category picker, which the markup binds to directly.</summary>
    public CategoryPickerViewModel Picker { get; }

    public ObservableCollection<TRow> Rows { get; } = [];

    /// <summary>
    /// What the section is filtering by, and the one signal the shell watches to know it has to search again.
    /// </summary>
    /// <remarks>
    /// One property rather than the two the shell used to watch — the search text and the selected category.
    /// It is also what <see cref="SearchAsync"/> asks the store with, so the thing announced and the thing
    /// applied cannot drift apart.
    /// </remarks>
    public CatalogueFilter Criteria => new(SearchText, Picker.RestrictedTo);

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

        if (source is null)
        {
            Picker.Clear();
            Notice = string.Empty;
            OnPropertyChanged(nameof(IsAvailable));

            return;
        }

        // Filling the picker and choosing an entry in it is one operation, in the picker, because as two it had
        // an order that had to be got right and was not. See CategoryPickerViewModel.
        await Picker.ShowAsync(source, cancellationToken).ConfigureAwait(true);

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

        var filter = Criteria;
        var page = await SearchAsync(source.Id, filter, cancellationToken).ConfigureAwait(true);

        Rows.Clear();

        foreach (var row in page.Items)
        {
            Rows.Add(row);
        }

        Notice = Describe(page, filter);
    }

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

    /// <remarks>
    /// The search box's half of <see cref="Criteria"/>. The picker's half arrives through the callback the
    /// constructor assigns, and both announce the same one property so the shell watches one name.
    /// </remarks>
    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(Criteria));
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
