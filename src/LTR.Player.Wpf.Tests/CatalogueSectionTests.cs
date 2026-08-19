using LTR.Core.Content;
using static LTR.Player.Wpf.VodSectionFixtures;

namespace LTR.Player.Wpf;

/// <summary>
/// What the film and series sections share: availability, the picker, and answering a search.
/// </summary>
/// <remarks>
/// Mirrors <see cref="CatalogueSectionViewModel{TRow}"/> rather than either concrete section, and is exercised
/// through the film section because it is the handier of the two: what is asserted belongs to the base, and the
/// series section would answer identically.
/// </remarks>
public sealed class CatalogueSectionTests
{
    [Fact]
    public async Task ShowCatalogue_LoadsFilmsAndSeriesForTheSelectedSource()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.SeriesCatalogue.Add(SeriesEntry(10, "Breaking Bad"));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.Movies.ShouldHaveSingleItem().Name.ShouldBe("Arrival");
        viewModel.SeriesCatalogue.Series.ShouldHaveSingleItem().Name.ShouldBe("Breaking Bad");
    }

    [Fact]
    public async Task ShowCatalogue_ForASourceWithoutFilms_LeavesTheSectionUnavailable()
    {
        // Arrange: a playlist source, which offers live entries and nothing else.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(supportsVod: false, supportsSeries: false));

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.IsAvailable.ShouldBeFalse();
        viewModel.SeriesCatalogue.IsAvailable.ShouldBeFalse();
    }

    /// <summary>
    /// Switching to a subscription that has no films while the film section is open would otherwise leave
    /// the previous subscription's catalogue on screen under the new subscription's name.
    /// </summary>
    [Fact]
    public async Task ShowCatalogue_WhenTheNewSourceLacksTheOpenSection_FallsBackToLive()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource(id: 1));
        context.Store.Sources.Add(CreateSource(id: 2, supportsVod: false, supportsSeries: false));
        context.Store.Movies.Add(Movie(1, "Arrival"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;

        // Act
        viewModel.SourceManagement.SelectedSource = context.Store.Sources[1];
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.SelectedSection.ShouldBe(CatalogueSection.Live);
    }

    /// <summary>
    /// The category shown in the picker and the category the filter uses have to agree. They did not:
    /// emptying the bound collection makes a ComboBox write a null selection back through the binding, so a
    /// selection made before the picker was refilled was discarded — the picker rendered blank while the
    /// list, reading the same null, still showed every category and therefore looked perfectly correct.
    /// </summary>
    [Fact]
    public async Task ShowCatalogue_LeavesEveryCategorySelectedInThePicker()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(
            new Category { SourceId = 1, ExternalId = "58", Name = "Action", Kind = ContentKind.Movie });
        context.Store.Categories.Add(
            new Category { SourceId = 1, ExternalId = "75", Name = "Drama", Kind = ContentKind.Series });

        var viewModel = context.Build();

        // Act
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Movies.Picker.Categories.Count.ShouldBe(2, "the catch-all entry and one film category");
        viewModel.Movies.Picker.SelectedCategory.ShouldBe(CategoryChoice.All);
        viewModel.SeriesCatalogue.Picker.SelectedCategory.ShouldBe(CategoryChoice.All);
    }

    /// <summary>
    /// Choosing a category asks the store again, with nothing else touched.
    /// </summary>
    /// <remarks>
    /// The film section pages rather than filtering in memory, so a category is a *new search* — and the shell
    /// is what runs it, from the one signal the section announces. Nothing covered that: found by mutation while
    /// the picker was being extracted, by deleting the notification and watching all 228 tests pass.
    /// </remarks>
    [Fact]
    public async Task ChoosingAFilmCategory_SearchesAgain()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(
            new Category { SourceId = 1, ExternalId = "58", Name = "Action", Kind = ContentKind.Movie });

        var action = Movie(1, "Arrival");
        action.CategoryExternalId = "58";
        context.Store.Movies.Add(action);
        context.Store.Movies.Add(Movie(2, "The Matrix"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Movies.Movies.Count.ShouldBe(2, "both are shown before a category is chosen");

        // Act
        viewModel.Movies.Picker.SelectedCategory = viewModel.Movies.Picker.Categories
            .Single(category => category.ExternalId == "58");

        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.Movies.Movies.ShouldHaveSingleItem().Name.ShouldBe("Arrival");
    }

    [Fact]
    public async Task Search_NarrowsTheFilmList()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Movies.Add(Movie(1, "Arrival"));
        context.Store.Movies.Add(Movie(2, "The Matrix"));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Movies.SearchText = "matrix";
        await viewModel.WaitForIdleAsync();

        // Assert
        viewModel.Movies.Movies.ShouldHaveSingleItem().Name.ShouldBe("The Matrix");
    }

}
