using System.Windows;
using System.Windows.Controls;
using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Player.Wpf.Views;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Builds the shared category picker over each real section and checks that its bindings found anything.
/// </summary>
/// <remarks>
/// The picker's markup names no section — its data context is whichever one it is placed in, which is what
/// lets one view serve live television, films and series. `ICategoryPickerSection` states the shape it
/// expects, but an interface only binds the code: the markup still resolves by name, so a member renamed on
/// one side and not the other leaves the picker bound to nothing in one section, and WPF reports that to a
/// trace listener rather than to the log. This is the assertion that the names still meet.
/// </remarks>
public sealed class CategoryPickerViewTests
{
    [Fact]
    public void ThePickerFindsWhatItBindsTo_InEverySectionItIsPlacedIn()
    {
        // Arrange & Act
        var report = VisualTreeHarness.OnStaThread(() =>
        {
            var context = new MainViewModelHarness();
            context.Store.Sources.Add(new XtreamSourceBuilder()
                .WithId(1)
                .WithCapabilities(new ProviderCapabilities
                {
                    SupportsLive = true,
                    SupportsVod = true,
                    SupportsSeries = true,
                })
                .Build());

            context.Store.Categories.Add(new Category
            {
                Id = 1,
                SourceId = 1,
                ExternalId = "10",
                Name = "DE Deutschland",
                Kind = ContentKind.Live,
            });

            var viewModel = context.Build();
            viewModel.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

            // Every section the picker is placed in by the three section views.
            return new[]
            {
                Inspect("live", viewModel.Channels.Picker),
                Inspect("films", viewModel.Movies.Picker),
                Inspect("series", viewModel.SeriesCatalogue.Picker),
            };
        });

        // Assert
        foreach (var (section, boundItems, boundSelection, boundCommand) in report)
        {
            boundItems.ShouldBeTrue($"the {section} picker found its categories");
            boundSelection.ShouldBeTrue($"the {section} picker found its selection");
            boundCommand.ShouldBeTrue($"the {section} star found its command");
        }
    }

    /// <summary>
    /// Puts the real picker over one section and reports which of its three bindings resolved.
    /// </summary>
    /// <remarks>
    /// Laid out rather than shown, as the guide's view test does: a binding resolves during measure, and
    /// nothing here needs a window.
    /// </remarks>
    private static (string Section, bool Items, bool Selection, bool Command) Inspect(
        string section,
        CategoryPickerViewModel subject)
    {
        var view = new CategoryPickerView { DataContext = subject };

        view.Measure(new Size(400, 60));
        view.Arrange(new Rect(0, 0, 400, 60));
        view.UpdateLayout();

        var picker = VisualTreeHarness.Descendants<ComboBox>(view).First();
        var star = VisualTreeHarness.Descendants<Button>(view).First();

        return (
            section,
            ReferenceEquals(picker.ItemsSource, subject.Categories),
            ReferenceEquals(picker.SelectedItem, subject.SelectedCategory),
            ReferenceEquals(star.Command, subject.ToggleCategoryFavoriteCommand));
    }
}
