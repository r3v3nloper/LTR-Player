namespace LTR.Core.Content;

public sealed class ChannelFilterTests
{
    [Fact]
    public void None_AdmitsEverything()
    {
        // Arrange & Act & Assert
        ChannelFilter.None.Matches(Channel("FR: TF1 HD")).ShouldBeTrue();
        ChannelFilter.None.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("tf1", true)]
    [InlineData("TF1", true)]
    [InlineData("  tf1  ", true)]
    [InlineData("f1 h", true)]
    [InlineData("zdf", false)]
    public void SearchText_MatchesAnywhereInTheNameIgnoringCase(string searchText, bool expected)
    {
        // Arrange: users type fragments, not prefixes.
        var filter = new ChannelFilter(SearchText: searchText);

        // Act & Assert
        filter.Matches(Channel("FR: TF1 HD")).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchText_WhenBlank_IsIgnored(string? searchText)
    {
        // Arrange
        var filter = new ChannelFilter(SearchText: searchText);

        // Act & Assert
        filter.Matches(Channel("Anything")).ShouldBeTrue();
        filter.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void CategoryExternalId_RestrictsToThatCategory()
    {
        // Arrange
        var filter = new ChannelFilter(CategoryExternalId: "10");

        // Act & Assert
        filter.Matches(Channel("A", categoryExternalId: "10")).ShouldBeTrue();
        filter.Matches(Channel("B", categoryExternalId: "20")).ShouldBeFalse();
    }

    [Fact]
    public void CategoryExternalId_ExcludesUncategorisedChannels()
    {
        // Arrange: a channel referencing a category the provider omitted has none of its own.
        var filter = new ChannelFilter(CategoryExternalId: "10");

        // Act & Assert
        filter.Matches(Channel("Orphan", categoryExternalId: null)).ShouldBeFalse();
    }

    [Fact]
    public void FavoritesOnly_RestrictsToMarkedChannels()
    {
        // Arrange
        var filter = new ChannelFilter(FavoritesOnly: true);

        // Act & Assert
        filter.Matches(Channel("A", isFavorite: true)).ShouldBeTrue();
        filter.Matches(Channel("B", isFavorite: false)).ShouldBeFalse();
    }

    [Fact]
    public void Criteria_CombineAsAConjunction()
    {
        // Arrange: all three set at once is the case the UI actually produces.
        var filter = new ChannelFilter(SearchText: "tf1", CategoryExternalId: "10", FavoritesOnly: true);

        // Act & Assert
        filter.Matches(Channel("FR: TF1 HD", "10", isFavorite: true)).ShouldBeTrue();
        filter.Matches(Channel("FR: TF1 HD", "10", isFavorite: false)).ShouldBeFalse("not a favourite");
        filter.Matches(Channel("FR: TF1 HD", "20", isFavorite: true)).ShouldBeFalse("wrong category");
        filter.Matches(Channel("FR: ZDF HD", "10", isFavorite: true)).ShouldBeFalse("name does not match");
    }

    [Fact]
    public void IsActive_ReportsWhetherAnyCriterionIsSet()
    {
        // Arrange & Act & Assert
        new ChannelFilter(SearchText: "a").IsActive.ShouldBeTrue();
        new ChannelFilter(CategoryExternalId: "10").IsActive.ShouldBeTrue();
        new ChannelFilter(FavoritesOnly: true).IsActive.ShouldBeTrue();
        new ChannelFilter().IsActive.ShouldBeFalse();
    }

    private static Channel Channel(string name, string? categoryExternalId = null, bool isFavorite = false)
    {
        return new Channel
        {
            SourceId = 1,
            ExternalId = name,
            Name = name,
            CategoryExternalId = categoryExternalId,
            IsFavorite = isFavorite,
        };
    }
}
