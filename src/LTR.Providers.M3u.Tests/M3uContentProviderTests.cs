using System.IO;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Providers.M3u;

/// <summary>
/// Exercises the provider through a real playlist file, so the loader and parser are covered along
/// with the mapping rather than replaced by a stand-in.
/// </summary>
public sealed class M3uContentProviderTests
{
    [Fact]
    public async Task FetchLiveChannelsAsync_PrefersTheGuideIdAsIdentity()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="tf1.fr",FR: TF1 HD
            http://host/live/user/pass/1.ts
            """;

        // Act
        var channels = await FetchChannelsAsync(playlist);

        // Assert
        var channel = channels.ShouldHaveSingleItem();
        channel.ExternalId.ShouldBe("tf1.fr");
        channel.StreamUrl.ShouldBe("http://host/live/user/pass/1.ts");
    }

    [Fact]
    public async Task FetchLiveChannelsAsync_WithoutAGuideId_DerivesIdentityFromTheName()
    {
        // Arrange: the identity must not be the URL, because that carries credentials which change
        // when the subscription is renewed — taking the user's favourites with them.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,FR: TF1 HD
            http://host/live/user/pass/1.ts
            """;

        // Act
        var channels = await FetchChannelsAsync(playlist);

        // Assert
        var channel = channels.ShouldHaveSingleItem();
        channel.ExternalId.ShouldBe("frtf1hd");
        channel.ExternalId.ShouldNotContain("pass");
    }

    [Fact]
    public async Task FetchLiveChannelsAsync_WhenTheCredentialsInTheUrlChange_KeepsTheSameIdentity()
    {
        // Arrange: this is the property that lets favourites survive a renewal.
        const string before = """
            #EXTM3U
            #EXTINF:-1,FR: TF1 HD
            http://host/live/olduser/oldpass/1.ts
            """;

        const string after = """
            #EXTM3U
            #EXTINF:-1,FR: TF1 HD
            http://host/live/newuser/newpass/1.ts
            """;

        // Act
        var first = await FetchChannelsAsync(before);
        var second = await FetchChannelsAsync(after);

        // Assert
        second.ShouldHaveSingleItem().ExternalId.ShouldBe(first.ShouldHaveSingleItem().ExternalId);
        second[0].StreamUrl.ShouldNotBe(first[0].StreamUrl, "the address itself is refreshed");
    }

    [Fact]
    public async Task FetchLiveChannelsAsync_WhenTwoEntriesShareAName_GivesThemDistinctIdentities()
    {
        // Arrange: playlists repeat names, and the unique index on (source, identity) would reject the
        // import if they collided.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,Sport
            http://host/1.ts
            #EXTINF:-1,Sport
            http://host/2.ts
            #EXTINF:-1,Sport
            http://host/3.ts
            """;

        // Act
        var channels = await FetchChannelsAsync(playlist);

        // Assert
        channels.Count.ShouldBe(3);
        channels.Select(channel => channel.ExternalId).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task FetchLiveChannelsAsync_DropsSeparatorRows()
    {
        // Arrange: playlists carry the same decorative padding as the Xtream catalogues.
        const string playlist = """
            #EXTM3U
            #EXTINF:-1,##### FRANCE #####
            http://host/0.ts
            #EXTINF:-1,FR: TF1 HD
            http://host/1.ts
            """;

        // Act
        var channels = await FetchChannelsAsync(playlist);

        // Assert
        channels.ShouldHaveSingleItem().Name.ShouldBe("FR: TF1 HD");
    }

    [Fact]
    public async Task FetchCategoriesAsync_DerivesDistinctCategoriesFromGroupTitles()
    {
        // Arrange
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="Sport",A
            http://host/1.ts
            #EXTINF:-1 group-title="News",B
            http://host/2.ts
            #EXTINF:-1 group-title="Sport",C
            http://host/3.ts
            #EXTINF:-1,D
            http://host/4.ts
            """;

        using var file = new TemporaryPlaylist(playlist);
        var provider = CreateProvider(file.Source);

        // Act
        var categories = await provider.FetchCategoriesAsync(
            ContentKind.Live,
            TestContext.Current.CancellationToken);

        // Assert
        categories.Select(category => category.Name).ShouldBe(["Sport", "News"]);
    }

    [Fact]
    public async Task FetchCategoriesAsync_ForVodOrSeries_YieldsNothing()
    {
        // Arrange: a playlist declares live entries only.
        using var file = new TemporaryPlaylist("#EXTM3U\n#EXTINF:-1,A\nhttp://host/1.ts\n");
        var provider = CreateProvider(file.Source);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var movies = await provider.FetchCategoriesAsync(ContentKind.Movie, cancellationToken);
        var series = await provider.FetchCategoriesAsync(ContentKind.Series, cancellationToken);

        // Assert
        movies.ShouldBeEmpty();
        series.ShouldBeEmpty();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePlaylistIsMissing_ReportsFailure()
    {
        // Arrange: fetching the document is the only thing resembling authentication here.
        var source = new M3uSource
        {
            Name = "Missing",
            PlaylistUrl = new Uri(Path.Combine(Path.GetTempPath(), "ltr-does-not-exist.m3u")),
        };

        var provider = CreateProvider(source);

        // Act
        var account = await provider.AuthenticateAsync(TestContext.Current.CancellationToken);

        // Assert
        account.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePlaylistLoads_ReportsAnActiveAccountWithNoConnectionLimit()
    {
        // Arrange: a playlist has no account behind it, so an unreported limit is the honest answer.
        using var file = new TemporaryPlaylist("#EXTM3U\n#EXTINF:-1,A\nhttp://host/1.ts\n");
        var provider = CreateProvider(file.Source);

        // Act
        var account = await provider.AuthenticateAsync(TestContext.Current.CancellationToken);

        // Assert
        account.IsUsable.ShouldBeTrue();
        account.MaxConnections.ShouldBe(0);
        account.HasFreeConnection.ShouldBeTrue();
    }

    private static async Task<IReadOnlyList<Core.Content.Channel>> FetchChannelsAsync(string playlist)
    {
        using var file = new TemporaryPlaylist(playlist);
        var provider = CreateProvider(file.Source);
        return await provider.FetchLiveChannelsAsync(TestContext.Current.CancellationToken);
    }

    private static IContentProvider CreateProvider(M3uSource source)
    {
        var loader = new M3uPlaylistLoader(new HttpClient());
        var factory = new M3uContentProviderFactory(loader, NullLoggerFactory.Instance);
        return factory.Create(source);
    }

    /// <summary>
    /// A playlist written to disk for the duration of one test, reached through the loader's file path.
    /// </summary>
    private sealed class TemporaryPlaylist : IDisposable
    {
        private readonly string _path;

        public TemporaryPlaylist(string content)
        {
            _path = Path.Combine(Path.GetTempPath(), $"ltr-test-{Guid.NewGuid():N}.m3u");
            File.WriteAllText(_path, content);

            Source = new M3uSource
            {
                Id = 1,
                Name = "Test playlist",
                PlaylistUrl = new Uri(_path),
            };
        }

        public M3uSource Source { get; }

        public void Dispose()
        {
            File.Delete(_path);
        }
    }
}
