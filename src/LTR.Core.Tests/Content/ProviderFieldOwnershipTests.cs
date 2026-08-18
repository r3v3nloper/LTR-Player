namespace LTR.Core.Content;

/// <summary>
/// What a refresh may overwrite, and what belongs to the viewer or to a detail call.
/// </summary>
/// <remarks>
/// This is the rule a reconciliation exists to keep, and getting it wrong is quiet: a wiped favourite or an
/// erased synopsis looks like the provider's doing. Stated on the entities and therefore testable here,
/// where before it took a database to see any of it.
/// </remarks>
public sealed class ProviderFieldOwnershipTests
{
    [Fact]
    public void Channel_AdoptProviderFields_KeepsTheFavouriteAndTheGuideLink()
    {
        // Arrange: the two things about a channel the provider does not own. A refresh that took them would
        // empty the favourites list and undo the guide matching.
        var stored = new Channel
        {
            SourceId = 1,
            ExternalId = "101",
            Name = "Erste",
            IsFavorite = true,
            GuideChannelId = 77,
        };

        var fetched = new Channel
        {
            SourceId = 1,
            ExternalId = "101",
            Name = "Erste HD",
            StreamUrl = "http://host/101.ts",
            SortOrder = 5,
        };

        // Act
        stored.AdoptProviderFields(fetched);

        // Assert
        stored.Name.ShouldBe("Erste HD");
        stored.StreamUrl.ShouldBe("http://host/101.ts");
        stored.SortOrder.ShouldBe(5);
        stored.IsFavorite.ShouldBeTrue();
        stored.GuideChannelId.ShouldBe(77);
    }

    [Fact]
    public void Movie_AdoptListingFields_KeepsWhereTheViewerGotTo()
    {
        // Arrange
        var stored = new VodItem
        {
            SourceId = 1,
            ExternalId = "8412",
            Name = "Arrival",
            ResumePositionSeconds = 2_400,
            IsWatched = true,
            HasDetail = true,
        };

        var fetched = new VodItem { SourceId = 1, ExternalId = "8412", Name = "Arrival (2016)" };

        // Act
        stored.AdoptListingFields(fetched);

        // Assert
        stored.Name.ShouldBe("Arrival (2016)");
        stored.ResumePositionSeconds.ShouldBe(2_400);
        stored.IsWatched.ShouldBeTrue();
        stored.HasDetail.ShouldBeTrue("a refresh must not make the player forget it has the detail");
    }

    [Fact]
    public void Movie_AdoptListingFields_WhereTheListingIsSilent_KeepsWhatTheDetailSupplied()
    {
        // Arrange: panels state a synopsis in get_vod_info and not in get_vod_streams, so this is the case
        // that would erase every synopsis the player had fetched, one refresh at a time.
        var stored = new VodItem
        {
            SourceId = 1,
            ExternalId = "8412",
            Name = "Arrival",
            Plot = "Linguist meets heptapods.",
            ContainerExtension = "mkv",
            Year = 2016,
        };

        var fetched = new VodItem { SourceId = 1, ExternalId = "8412", Name = "Arrival" };

        // Act
        stored.AdoptListingFields(fetched);

        // Assert
        stored.Plot.ShouldBe("Linguist meets heptapods.");
        stored.ContainerExtension.ShouldBe("mkv", "a film's address is built from it");
        stored.Year.ShouldBe(2016);
    }

    [Fact]
    public void Movie_AdoptListingFields_WhereTheListingSpeaks_TakesItsWord()
    {
        // Arrange: the other half of the same rule — a listing that does state a field owns it.
        var stored = new VodItem { SourceId = 1, ExternalId = "8412", Name = "Arrival", Year = 2016 };

        var fetched = new VodItem
        {
            SourceId = 1,
            ExternalId = "8412",
            Name = "Arrival",
            Year = 2017,
            Plot = "A corrected synopsis.",
        };

        // Act
        stored.AdoptListingFields(fetched);

        // Assert
        stored.Year.ShouldBe(2017);
        stored.Plot.ShouldBe("A corrected synopsis.");
    }

    [Fact]
    public void Series_AdoptListingFields_AdoptsTheModifiedInstantEvenWhenItIsOlder()
    {
        // Arrange: the one field a refresh must always take. It is what tells stored seasons apart from stale
        // ones, so keeping the newer value would leave a series the provider changed never fetched again.
        var stored = new Series
        {
            SourceId = 1,
            ExternalId = "4321",
            Name = "Breaking Bad",
            LastModifiedUtc = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            Plot = "A chemistry teacher.",
        };

        var fetched = new Series
        {
            SourceId = 1,
            ExternalId = "4321",
            Name = "Breaking Bad",
            LastModifiedUtc = new DateTimeOffset(2020, 2, 5, 0, 0, 0, TimeSpan.Zero),
        };

        // Act
        stored.AdoptListingFields(fetched);

        // Assert
        stored.LastModifiedUtc.ShouldBe(new DateTimeOffset(2020, 2, 5, 0, 0, 0, TimeSpan.Zero));
        stored.Plot.ShouldBe("A chemistry teacher.", "and the synopsis is still the detail call's");
    }

    [Fact]
    public void Series_AdoptListingFields_LeavesTheStoredSeasonsAlone()
    {
        // Arrange: a listing carries no seasons at all, and dropping them would undo the detail fetch.
        var stored = new Series
        {
            SourceId = 1,
            ExternalId = "4321",
            Name = "Breaking Bad",
            Seasons = [new Season { Number = 1, Episodes = [] }],
        };

        // Act
        stored.AdoptListingFields(new Series { SourceId = 1, ExternalId = "4321", Name = "Breaking Bad" });

        // Assert
        stored.Seasons.ShouldHaveSingleItem();
    }

    [Fact]
    public void Category_AdoptProviderFields_TakesTheNameAndThePosition()
    {
        // Arrange
        var stored = new Category
        {
            SourceId = 1,
            ExternalId = "58",
            Name = "Sport",
            Kind = ContentKind.Live,
            SortOrder = 3,
            IsFavorite = true,
        };

        var fetched = new Category
        {
            SourceId = 1,
            ExternalId = "58",
            Name = "Sport HD",
            Kind = ContentKind.Live,
            SortOrder = 1,
        };

        // Act
        stored.AdoptProviderFields(fetched);

        // Assert
        stored.Name.ShouldBe("Sport HD");
        stored.SortOrder.ShouldBe(1);
        stored.Kind.ShouldBe(ContentKind.Live, "the kind is part of what matched them");
        stored.IsFavorite.ShouldBeTrue("a pin is the viewer's, and every import would otherwise clear it");
    }
}
