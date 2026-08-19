using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// The fixtures the film, series and continue-watching tests share.
/// </summary>
/// <remarks>
/// Shared because all four files that came out of <c>VodSectionTests</c> seed the same subscription: a
/// source that offers films and series, and the three entity shapes to put in it. Reached through
/// <c>using static</c>, so a call site reads as it did in the one file they came from.
/// </remarks>
internal static class VodSectionFixtures
{
    /// <summary>A running time long enough that a position part-way through is unambiguous.</summary>
    public static readonly TimeSpan FilmLength = TimeSpan.FromMinutes(100);

    /// <summary>
    /// Opens the film section with its first film selected and its detail loaded, which is the state every
    /// playback test starts from.
    public static async Task<MainViewModel> OpenFilmAsync(MainViewModelHarness context)
    {
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.SelectedSection = CatalogueSection.Movies;
        viewModel.Movies.SelectedMovie = viewModel.Movies.Movies[0];
        await viewModel.WaitForIdleAsync();

        return viewModel;
    }

    public static XtreamSource CreateSource(
        int id = 1,
        bool supportsVod = true,
        bool supportsSeries = true)
    {
        return new XtreamSourceBuilder()
            .WithId(id)
            .WithName($"Source {id}")
            .WithCredentials("alice", "s3cret")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = supportsVod,
                SupportsSeries = supportsSeries,
                ProbedAtUtc = MainViewModelHarness.Now,
            })
            .Build();
    }

    public static VodItem Movie(int id, string name)
    {
        return new VodItem
        {
            Id = id,
            SourceId = 1,
            ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
            ContainerExtension = "mkv",
        };
    }

    public static Series SeriesEntry(int id, string name)
    {
        return new Series
        {
            Id = id,
            SourceId = 1,
            ExternalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
        };
    }

    public static Episode Episode(string externalId, string title, int number)
    {
        return new Episode
        {
            ExternalId = externalId,
            Title = title,
            Number = number,
            ContainerExtension = "mkv",
        };
    }
}
