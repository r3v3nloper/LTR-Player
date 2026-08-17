using System.Globalization;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// The rules of the line under a film's or a series' name, which both rows now share.
/// </summary>
/// <remarks>
/// Bound in three places and untested until the two rows stopped assembling it separately. The rules are
/// worth pinning rather than the formatting: a panel states a subset of these fields for most items, and
/// writes a rating of zero for everything it has no rating for.
/// </remarks>
public sealed class CatalogueDetailLineTests
{
    /// <summary>
    /// A rating is written in the viewer's culture, so an assertion that spells the separator would pass
    /// only where the tests happen to run. Stated here once rather than avoided by forcing a culture: the
    /// culture-sensitivity is deliberate, and `InvariantGlobalization` is off for related reasons.
    /// </summary>
    private static readonly string DecimalSeparator =
        CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

    [Fact]
    public void Build_ForAFilm_ReadsYearThenRunningTimeThenRatingThenGenre()
    {
        // Arrange & Act
        var line = CatalogueDetailLine.Build(1999, 7.5, "Sci-Fi", TimeSpan.FromMinutes(136));

        // Assert
        line.ShouldBe($"1999 · 2:16:00 · 7{DecimalSeparator}5 · Sci-Fi");
    }

    [Fact]
    public void Build_WithoutARunningTime_LeavesNoGapWhereItWouldHaveBeen()
    {
        // Arrange: what a series passes, having no running time of its own.
        // Act
        var line = CatalogueDetailLine.Build(2008, 9.4, "Drama");

        // Assert
        line.ShouldBe($"2008 · 9{DecimalSeparator}4 · Drama");
    }

    [Fact]
    public void Build_WithARatingOfZero_OmitsIt()
    {
        // Arrange: panels write zero for everything they have no rating for, so a displayed 0 would be a
        // statement the provider never made.
        // Act
        var line = CatalogueDetailLine.Build(2015, 0, "Action");

        // Assert
        line.ShouldBe("2015 · Action");
    }

    [Fact]
    public void Build_WithNothingStated_IsEmpty()
    {
        // Arrange: a bare listing entry, which is most of a large catalogue.
        // Act
        var line = CatalogueDetailLine.Build(year: null, rating: null, genre: null);

        // Assert
        line.ShouldBeEmpty();
    }

    [Fact]
    public void Build_WithABlankGenre_OmitsIt()
    {
        // Arrange: an empty string is how a panel says "no genre", and it would otherwise render as a
        // trailing separator.
        // Act
        var line = CatalogueDetailLine.Build(2015, 6.1, "   ");

        // Assert
        line.ShouldBe($"2015 · 6{DecimalSeparator}1");
    }

    [Fact]
    public void Details_OfBothRows_AgreeOnTheFieldsTheyShare()
    {
        // Arrange: the reason the builder exists. Same year, rating and genre through two different rows.
        var movie = new MovieItemViewModel(new VodItem
        {
            SourceId = 1,
            ExternalId = "1",
            Name = "A film",
            Year = 2001,
            Rating = 8.2,
            Genre = "Thriller",
        });

        var series = new SeriesItemViewModel(new Series
        {
            SourceId = 1,
            ExternalId = "1",
            Name = "A series",
            Year = 2001,
            Rating = 8.2,
            Genre = "Thriller",
        });

        // Act & Assert
        movie.Details.ShouldBe(series.Details);
    }
}
