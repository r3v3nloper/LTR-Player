using LTR.Core.Content;

namespace LTR.Persistence;

/// <summary>
/// The matching itself, which four reconciliations now share.
/// </summary>
/// <remarks>
/// Worth its own tests because two of its rules are decisions rather than mechanics: what happens when a
/// provider lists the same identifier twice, and that a composite key is what a category is matched by — a
/// panel numbers its identifiers per section, so "58" is a live category and a film category at once.
/// </remarks>
public sealed class CatalogueReconcilerTests
{
    [Fact]
    public void Match_SeparatesWhatIsNewFromWhatIsStoredAndWhatIsGone()
    {
        // Arrange
        var stored = new[] { Channel("101", "Erste"), Channel("102", "Zweite") };
        var fetched = new[] { Channel("101", "Erste HD"), Channel("103", "Dritte") };

        // Act
        var reconciliation = CatalogueReconciler.Match(
            stored,
            fetched,
            channel => channel.ExternalId,
            StringComparer.Ordinal);

        // Assert
        reconciliation.Added.ShouldHaveSingleItem().ExternalId.ShouldBe("103");
        reconciliation.Removed.ShouldHaveSingleItem().ExternalId.ShouldBe("102");

        var (matchedStored, matchedFetched) = reconciliation.Matched.ShouldHaveSingleItem();
        matchedStored.Name.ShouldBe("Erste", "the stored instance is the tracked one");
        matchedFetched.Name.ShouldBe("Erste HD");
    }

    [Fact]
    public void Match_WhenNothingIsStored_ReportsEverythingAsNew()
    {
        // Arrange: the first import of a source, which is most of them.
        var fetched = new[] { Channel("101", "Erste"), Channel("102", "Zweite") };

        // Act
        var reconciliation = CatalogueReconciler.Match(
            [],
            fetched,
            channel => channel.ExternalId,
            StringComparer.Ordinal);

        // Assert
        reconciliation.Added.Count.ShouldBe(2);
        reconciliation.Matched.ShouldBeEmpty();
        reconciliation.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Match_WhenTheProviderListsAnIdentifierTwice_PairsTheFirstAndTreatsTheSecondAsNew()
    {
        // Arrange: a provider fault rather than a reason to fail an import. The unique index on
        // (source, identity) has the final word; this only has to not throw and not lose the stored row.
        var stored = new[] { Channel("101", "Erste") };
        var fetched = new[] { Channel("101", "Erste HD"), Channel("101", "Erste again") };

        // Act
        var reconciliation = CatalogueReconciler.Match(
            stored,
            fetched,
            channel => channel.ExternalId,
            StringComparer.Ordinal);

        // Assert
        reconciliation.Matched.ShouldHaveSingleItem().Fetched.Name.ShouldBe("Erste HD");
        reconciliation.Added.ShouldHaveSingleItem().Name.ShouldBe("Erste again");
        reconciliation.Removed.ShouldBeEmpty("the stored row was matched, so it stays");
    }

    [Fact]
    public void Match_ForCategories_TreatsTheKindAsPartOfTheIdentity()
    {
        // Arrange: the composite key exists for this. A panel numbers its categories per section, so a live
        // category and a film category can both be "58", and matching by the identifier alone would pair
        // them — and delete one of them.
        var stored = new[] { Category("58", ContentKind.Live, "Sport") };
        var fetched = new[] { Category("58", ContentKind.Movie, "Action") };

        // Act
        var reconciliation = CatalogueReconciler.Match(
            stored,
            fetched,
            category => (category.ExternalId, category.Kind));

        // Assert
        reconciliation.Matched.ShouldBeEmpty("different kinds are different categories");
        reconciliation.Added.ShouldHaveSingleItem().Kind.ShouldBe(ContentKind.Movie);
        reconciliation.Removed.ShouldHaveSingleItem().Kind.ShouldBe(ContentKind.Live);
    }

    private static Channel Channel(string externalId, string name)
    {
        return new Channel { SourceId = 1, ExternalId = externalId, Name = name };
    }

    private static Category Category(string externalId, ContentKind kind, string name)
    {
        return new Category { SourceId = 1, ExternalId = externalId, Kind = kind, Name = name };
    }
}
