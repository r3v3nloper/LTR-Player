using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers pinning a category to the top of the picker.
/// </summary>
/// <remarks>
/// The feature exists for a fact about real subscriptions: a panel lists a couple of hundred categories in
/// whatever order it holds them, and the two or three somebody watches are as likely to be at the bottom as
/// anywhere. What is worth testing is not the star but the three things around it — that the entry being
/// watched stays selected while it moves, that the pin is written where the next load will read it, and that
/// the entry standing for "no category" cannot be pinned at all.
/// </remarks>
public sealed class CategoryPinTests
{
    [Fact]
    public async Task PinningACategory_MovesItToTheTopAndLeavesItSelected()
    {
        // Arrange: the category being watched sits below two others, as it does in a real panel.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));
        context.Store.Categories.Add(Category(2, "20", "FR France", order: 1));
        context.Store.Categories.Add(Category(3, "30", "DE Deutschland", order: 2));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var channels = viewModel.Channels;
        var watched = channels.Categories.Single(choice => choice.Name == "DE Deutschland");
        channels.SelectedCategory = watched;

        // Act
        await channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Assert: first after the unrestricted entry, still the selection, and still the same object —
        // an entry replaced by a copy is a different item to the picker, and the filter goes with it.
        channels.Categories.Select(choice => choice.Name)
            .ShouldBe(["All categories", "DE Deutschland", "AR Arabic", "FR France"]);

        channels.SelectedCategory.ShouldBeSameAs(watched);
        watched.IsFavorite.ShouldBeTrue();

        context.Store.CategoryFavoriteWrites.ShouldBe([(3, true)]);
    }

    [Fact]
    public async Task APinnedCategory_IsStillAtTheTopWhenTheSourceIsLoadedAgain()
    {
        // Arrange: pinning is worth nothing if it lasts only until the window is closed.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));
        context.Store.Categories.Add(Category(3, "30", "DE Deutschland", order: 2));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.Channels.SelectedCategory =
            viewModel.Channels.Categories.Single(choice => choice.Name == "DE Deutschland");

        await viewModel.Channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Act: a second window over the same catalogue.
        var reopened = context.Build();
        await reopened.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        reopened.Channels.Categories.Select(choice => choice.Name)
            .ShouldBe(["All categories", "DE Deutschland", "AR Arabic"]);

        reopened.Channels.Categories[1].IsFavorite.ShouldBeTrue("the star has to be on it as well");
    }

    [Fact]
    public async Task UnpinningACategory_PutsItBackWhereTheProviderHadIt()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));
        context.Store.Categories.Add(Category(2, "20", "FR France", order: 1));
        context.Store.Categories.Add(Category(3, "30", "DE Deutschland", order: 2));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var channels = viewModel.Channels;
        channels.SelectedCategory = channels.Categories.Single(choice => choice.Name == "DE Deutschland");
        await channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Act
        await channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Assert
        channels.Categories.Select(choice => choice.Name)
            .ShouldBe(["All categories", "AR Arabic", "FR France", "DE Deutschland"]);

        context.Store.CategoryFavoriteWrites.ShouldBe([(3, true), (3, false)]);
    }

    [Fact]
    public async Task TwoPinnedCategories_KeepTheProvidersOrderBetweenThem()
    {
        // Arrange: pinning is not a stack. Two pinned categories stay in the order the panel lists them,
        // so the picker does not reshuffle itself according to what was starred most recently.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));
        context.Store.Categories.Add(Category(2, "20", "FR France", order: 1));
        context.Store.Categories.Add(Category(3, "30", "DE Deutschland", order: 2));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        var channels = viewModel.Channels;

        // Act: the lower one first, so a stack would put it above the other.
        channels.SelectedCategory = channels.Categories.Single(choice => choice.Name == "DE Deutschland");
        await channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        channels.SelectedCategory = channels.Categories.Single(choice => choice.Name == "FR France");
        await channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Assert
        channels.Categories.Select(choice => choice.Name)
            .ShouldBe(["All categories", "FR France", "DE Deutschland", "AR Arabic"]);
    }

    [Fact]
    public async Task TheUnrestrictedEntry_CannotBePinned()
    {
        // Arrange: it stands for no category at all, so there is nothing to write a pin against.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Assert
        viewModel.Channels.SelectedCategory.ShouldBe(CategoryChoice.All);
        viewModel.Channels.ToggleCategoryFavoriteCommand.CanExecute(null).ShouldBeFalse();

        // Act: and choosing a real one offers the star — asserted through the notification rather than the
        // guard, because a guard read directly answers correctly even when nothing announced the change.
        var announced = false;
        viewModel.Channels.ToggleCategoryFavoriteCommand.CanExecuteChanged += (_, _) => announced = true;

        viewModel.Channels.SelectedCategory = viewModel.Channels.Categories[1];

        // Assert
        announced.ShouldBeTrue("the button stays greyed out until something tells it otherwise");
        viewModel.Channels.ToggleCategoryFavoriteCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <remarks>
    /// This crashed the window on startup. A ComboBox pushes a null selection back through the binding the
    /// moment its bound collection is emptied, which is what filling the picker does — and the pin's guard is
    /// asked on every selection change, so it reads the selection during exactly that instant. Every other
    /// reader in both view models already allowed for it; the guard, declared against a non-null property,
    /// did not.
    /// </remarks>
    [Fact]
    public async Task TheGuardSurvivesThePickerWritingBackAnEmptySelection()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "10", "AR Arabic", order: 0));
        context.Store.Categories.Add(Category(7, "58", "Action", order: 0, kind: ContentKind.Movie));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act: what the control does while the list under it is replaced.
        var live = () => viewModel.Channels.SelectedCategory = null;
        var films = () => viewModel.Movies.SelectedCategory = null;

        // Assert
        live.ShouldNotThrow();
        films.ShouldNotThrow();

        viewModel.Channels.ToggleCategoryFavoriteCommand.CanExecute(null).ShouldBeFalse();
        viewModel.Movies.ToggleCategoryFavoriteCommand.CanExecute(null).ShouldBeFalse();

        // And pressing it anyway — as a button already enabled would — writes nothing.
        await viewModel.Channels.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);
        context.Store.CategoryFavoriteWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheFilmSectionPinsItsOwnCategories()
    {
        // Arrange: a panel numbers its categories per section, so the film picker pins film categories —
        // the identity written is the stored one, not the number the panel reused.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());
        context.Store.Categories.Add(Category(1, "58", "DE Deutschland", order: 0));
        context.Store.Categories.Add(Category(7, "58", "Action", order: 1, kind: ContentKind.Movie));

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.Movies.SelectedCategory = viewModel.Movies.Categories.Single(c => c.Name == "Action");
        await viewModel.Movies.ToggleCategoryFavoriteCommand.ExecuteAsync(parameter: null);

        // Assert
        context.Store.CategoryFavoriteWrites.ShouldBe([(7, true)]);
        context.Store.Categories.Single(category => category.Kind == ContentKind.Live).IsFavorite
            .ShouldBeFalse("the live category of the same number is a different category");
    }

    private static Category Category(
        int id,
        string externalId,
        string name,
        int order,
        ContentKind kind = ContentKind.Live)
    {
        return new Category
        {
            Id = id,
            SourceId = 1,
            ExternalId = externalId,
            Name = name,
            Kind = kind,
            SortOrder = order,
        };
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSourceBuilder()
            .WithId(1)
            .WithCredentials("alice", "s3cret")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = true,
                SupportsSeries = true,
            })
            .Build();
    }
}
