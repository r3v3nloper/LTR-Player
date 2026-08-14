namespace LTR.Core.Content;

public sealed class EpisodeNamingTests
{
    [Theory]
    [InlineData(1, 1, "S01E01")]
    [InlineData(2, 5, "S02E05")]
    [InlineData(0, 3, "S00E03")]
    [InlineData(12, 145, "S12E145")]
    public void Label_PadsToTwoDigitsWithoutTruncating(int season, int episode, string expected)
    {
        // Arrange & Act
        var label = EpisodeNaming.Label(season, episode);

        // Assert
        label.ShouldBe(expected);
    }

    [Fact]
    public void Describe_AppendsTheEpisodeTitle()
    {
        // Arrange & Act
        var described = EpisodeNaming.Describe(2, 5, "Fly");

        // Assert
        described.ShouldBe("S02E05 · Fly");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_WithoutATitle_IsJustTheLabel(string? title)
    {
        // Arrange & Act
        var described = EpisodeNaming.Describe(2, 5, title);

        // Assert
        described.ShouldBe("S02E05");
    }

    [Fact]
    public void Describe_WhenTheTitleIsTheLabel_DoesNotRepeatIt()
    {
        // Arrange: panels routinely set the episode title to the label itself.
        var described = EpisodeNaming.Describe(2, 5, "s02e05");

        // Act & Assert
        described.ShouldBe("S02E05");
    }
}
