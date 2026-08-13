namespace LTR.Providers.M3u;

/// <summary>
/// M3U-Plus has no specification, so these cases come from the shapes providers actually emit.
/// </summary>
public sealed class M3uPlusParserTests
{
    [Fact]
    public async Task ParseAsync_ReadsAFullyAttributedEntry()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="tf1.fr" tvg-name="TF1" tvg-logo="http://host/tf1.png" group-title="FR FRANCE",FR: TF1 HD
            http://host:8080/live/user/pass/1158.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.DisplayName.ShouldBe("FR: TF1 HD");
        entry.Url.AbsoluteUri.ShouldBe("http://host:8080/live/user/pass/1158.ts");
        entry.TvgId.ShouldBe("tf1.fr");
        entry.TvgName.ShouldBe("TF1");
        entry.LogoUrl.ShouldBe("http://host/tf1.png");
        entry.GroupTitle.ShouldBe("FR FRANCE");
        result.SkippedEntryCount.ShouldBe(0);
    }

    [Fact]
    public async Task ParseAsync_WhenAnAttributeValueContainsAComma_StillSplitsTheNameCorrectly()
    {
        // Arrange: the separator is the first comma outside quotes. Splitting on the first comma
        // outright would put half the group title into the channel name.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="x" group-title="Sport, News und mehr",DE: SKY SPORT 1
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.GroupTitle.ShouldBe("Sport, News und mehr");
        entry.DisplayName.ShouldBe("DE: SKY SPORT 1");
    }

    [Fact]
    public async Task ParseAsync_WhenTheNameContainsCommas_KeepsAllOfThem()
    {
        // Arrange: everything after the separator belongs to the name, commas included.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="x",Film: Zurück in die Zukunft, Teil 2
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().DisplayName.ShouldBe("Film: Zurück in die Zukunft, Teil 2");
    }

    [Fact]
    public async Task ParseAsync_AcceptsAttributesInAnyOrder()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="Sport" tvg-logo="http://host/l.png" tvg-id="a" tvg-chno="42",Channel
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.TvgId.ShouldBe("a");
        entry.GroupTitle.ShouldBe("Sport");
        entry.ChannelNumber.ShouldBe(42);
    }

    [Fact]
    public async Task ParseAsync_AcceptsUnquotedAttributeValues()
    {
        // Arrange: hand-written playlists and several panel exports omit the quotes.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id=tf1.fr group-title=Sport,TF1
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.TvgId.ShouldBe("tf1.fr");
        entry.GroupTitle.ShouldBe("Sport");
        entry.DisplayName.ShouldBe("TF1");
    }

    [Fact]
    public async Task ParseAsync_WithoutAnyAttributes_StillYieldsTheEntry()
    {
        // Arrange: plain extended M3U, which is what a hand-written list looks like.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,Just A Name
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.DisplayName.ShouldBe("Just A Name");
        entry.TvgId.ShouldBeNull();
        entry.GroupTitle.ShouldBeNull();
    }

    [Fact]
    public async Task ParseAsync_HonoursAnExtGrpLineWhenNoGroupTitleWasGiven()
    {
        // Arrange: the older convention for stating a group.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="a",Channel
            #EXTGRP:Documentaries
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().GroupTitle.ShouldBe("Documentaries");
    }

    [Fact]
    public async Task ParseAsync_PrefersGroupTitleOverExtGrp()
    {
        // Arrange: the inline attribute is the more specific declaration of the two.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="Sport",Channel
            #EXTGRP:Something Else
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().GroupTitle.ShouldBe("Sport");
    }

    [Fact]
    public async Task ParseAsync_IgnoresDirectivesItDoesNotApply()
    {
        // Arrange: playback hints sit between the declaration and its address.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="a",Channel
            #EXTVLCOPT:http-user-agent=VLC/3.0.21
            #EXTVLCOPT:network-caching=1000
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().Url.AbsoluteUri.ShouldBe("http://host/1.ts");
        result.SkippedEntryCount.ShouldBe(0);
    }

    [Fact]
    public async Task ParseAsync_ReadsTheGuideUrlFromTheHeader()
    {
        // Arrange: a plain playlist has no programme data, so this is the only route to a guide.
        const string playlist = """
            #EXTM3U x-tvg-url="http://host/xmltv.php?username=u&password=p"
            #EXTINF:-1,Channel
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.EpgUrl.ShouldNotBeNull();
        result.EpgUrl.AbsoluteUri.ShouldBe("http://host/xmltv.php?username=u&password=p");
    }

    [Fact]
    public async Task ParseAsync_WhenSeveralGuideUrlsAreListed_TakesTheFirstUsableOne()
    {
        // Arrange
        const string playlist = """
            #EXTM3U x-tvg-url="not a url,http://host/guide.xml"
            #EXTINF:-1,Channel
            http://host/1.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.EpgUrl!.AbsoluteUri.ShouldBe("http://host/guide.xml");
    }

    [Fact]
    public async Task ParseAsync_SkipsADeclarationWhoseAddressNeverArrives()
    {
        // Arrange: truncated exports end this way, and one bad entry must not lose the rest.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,First
            #EXTINF:-1,Second
            http://host/2.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().DisplayName.ShouldBe("Second");
        result.SkippedEntryCount.ShouldBe(1);
    }

    [Fact]
    public async Task ParseAsync_SkipsAnEntryWithAnUnusableAddress()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,Broken
            this is not a url
            #EXTINF:-1,Fine
            http://host/2.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().DisplayName.ShouldBe("Fine");
        result.SkippedEntryCount.ShouldBe(1);
    }

    [Fact]
    public async Task ParseAsync_SkipsAnExtInfLineWithNoDisplayName()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="a"
            http://host/1.ts
            #EXTINF:-1,Fine
            http://host/2.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldHaveSingleItem().DisplayName.ShouldBe("Fine");
        result.SkippedEntryCount.ShouldBe(2, "the declaration and its orphaned address");
    }

    [Fact]
    public async Task ParseAsync_ToleratesABomBlankLinesAndCarriageReturns()
    {
        // Arrange: exactly what a playlist downloaded on Windows tends to look like.
        const string playlist = "﻿#EXTM3U\r\n\r\n#EXTINF:-1 tvg-id=\"a\",Channel\r\n\r\nhttp://host/1.ts\r\n";

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        var entry = result.Entries.ShouldHaveSingleItem();
        entry.TvgId.ShouldBe("a");
        entry.DisplayName.ShouldBe("Channel");
    }

    [Fact]
    public async Task ParseAsync_ToleratesAnUnterminatedQuote()
    {
        // Arrange: a truncated attribute must not swallow the parser.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="unterminated,Channel
            http://host/1.ts
            """;

        // Act
        var act = async () => await ParseAsync(playlist);

        // Assert
        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task ParseAsync_WithNoEntries_YieldsAnEmptyPlaylist()
    {
        // Arrange
        const string playlist = "#EXTM3U\n";

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.ShouldBeEmpty();
        result.SkippedEntryCount.ShouldBe(0);
        result.EpgUrl.ShouldBeNull();
    }

    [Fact]
    public async Task ParseAsync_PreservesTheDeclaredOrder()
    {
        // Arrange: providers order their lists deliberately.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,First
            http://host/1.ts
            #EXTINF:-1,Second
            http://host/2.ts
            #EXTINF:-1,Third
            http://host/3.ts
            """;

        // Act
        var result = await ParseAsync(playlist);

        // Assert
        result.Entries.Select(entry => entry.DisplayName).ShouldBe(["First", "Second", "Third"]);
    }

    private static async Task<M3uPlaylist> ParseAsync(string playlist)
    {
        using var reader = new StringReader(playlist);
        return await M3uPlusParser.ParseAsync(reader, TestContext.Current.CancellationToken);
    }
}
