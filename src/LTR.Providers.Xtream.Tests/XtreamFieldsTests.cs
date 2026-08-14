namespace LTR.Providers.Xtream;

public sealed class XtreamFieldsTests
{
    [Theory]
    [InlineData("http://cover.example/a.jpg", "http://cover.example/a.jpg")]
    [InlineData("https://cover.example/a.jpg", "https://cover.example/a.jpg")]
    [InlineData("  http://cover.example/a.jpg  ", "http://cover.example/a.jpg")]
    public void ImageUrl_KeepsAbsoluteHttpAddresses(string value, string expected)
    {
        // Arrange & Act
        var url = XtreamFields.ImageUrl(value);

        // Assert
        url.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("/images/local.jpg")]
    [InlineData("C:\\panel\\covers\\a.jpg")]
    [InlineData("ftp://cover.example/a.jpg")]
    public void ImageUrl_DiscardsEverythingTheUiCouldNotLoad(string? value)
    {
        // Arrange: panels put empty strings, local paths and the literal text "null" in these fields.
        // Filtering here is what keeps the image loading in the UI free of guards.
        var url = XtreamFields.ImageUrl(value);

        // Act & Assert
        url.ShouldBeNull();
    }

    [Theory]
    [InlineData("2019", 2019)]
    [InlineData("2019-05-01", 2019)]
    [InlineData("01.05.2019", 2019)]
    [InlineData("May 2019", 2019)]
    public void Year_ReadsTheYearOutOfWhateverTheDateLooksLike(string releaseDate, int expected)
    {
        // Arrange & Act
        var year = XtreamFields.Year(releaseDate);

        // Assert
        year.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000-00-00")]
    [InlineData("N/A")]
    [InlineData("20190501")]
    public void Year_WhenThereIsNoPlausibleYear_ReportsNone(string? releaseDate)
    {
        // Arrange: 0000-00-00 is what panels write for "unknown", and an eight-digit run is a packed
        // date whose leading four digits are not to be read as a year on their own.
        var year = XtreamFields.Year(releaseDate);

        // Act & Assert
        year.ShouldBeNull();
    }

    [Fact]
    public void Instant_TreatsZeroAsAbsent()
    {
        // Arrange: zero is what panels write for "unknown", and 1 January 1970 is a worse answer.
        XtreamFields.Instant(0).ShouldBeNull();
        XtreamFields.Instant(null).ShouldBeNull();

        // Act & Assert
        XtreamFields.Instant(1_600_000_000).ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000));
    }

    [Fact]
    public void DurationSeconds_PrefersTheMeasuredSecondsOverTheStatedMinutes()
    {
        // Arrange: the seconds figure comes from the panel having opened the file; the minutes figure is
        // metadata somebody typed.
        var duration = XtreamFields.DurationSeconds(seconds: 5_400, minutes: 95);

        // Act & Assert
        duration.ShouldBe(5_400);
    }

    [Fact]
    public void DurationSeconds_FallsBackToMinutes()
    {
        // Arrange & Act
        var duration = XtreamFields.DurationSeconds(seconds: 0, minutes: 95);

        // Assert
        duration.ShouldBe(95 * 60);
    }

    [Fact]
    public void DurationSeconds_WhenNeitherIsStated_ReportsNone()
    {
        // Arrange & Act & Assert
        XtreamFields.DurationSeconds(seconds: null, minutes: null).ShouldBeNull();
        XtreamFields.DurationSeconds(seconds: 0, minutes: 0).ShouldBeNull();
    }

    [Fact]
    public void Either_TakesWhicheverSpellingWasPopulated()
    {
        // Arrange: the synopsis is "plot" on some panels and "description" on others, and a few send
        // both with one of them empty.
        XtreamFields.Either("a plot", null).ShouldBe("a plot");
        XtreamFields.Either("", "a description").ShouldBe("a description");
        XtreamFields.Either("   ", "a description").ShouldBe("a description");

        // Act & Assert
        XtreamFields.Either(null, null).ShouldBeNull();
    }
}
