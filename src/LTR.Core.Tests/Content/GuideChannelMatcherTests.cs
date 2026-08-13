namespace LTR.Core.Content;

/// <summary>
/// The rules that decide whether the guide appears to work at all.
/// </summary>
public sealed class GuideChannelMatcherTests
{
    [Fact]
    public void Match_PrefersTheGuideIdentifierTheChannelStates()
    {
        // Arrange: the name would match the other guide channel, so this also proves precedence.
        var channels = new[] { Channel(1, "TF1", epgChannelId: "tf1.fr") };

        var guideChannels = new[]
        {
            GuideChannel(10, "tf1.fr", "Première chaîne"),
            GuideChannel(11, "something.else", "TF1"),
        };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links[1].ShouldBe(10);
    }

    /// <summary>
    /// On a real subscription most channels carry no guide identifier at all, so this path — not the one
    /// above — is what the guide's usefulness rests on.
    /// </summary>
    [Fact]
    public void Match_FallsBackToTheNameWhenThereIsNoGuideIdentifier()
    {
        // Arrange
        var channels = new[] { Channel(1, "FR: TF1 HD") };
        var guideChannels = new[] { GuideChannel(10, "tf1.fr", "TF1") };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links[1].ShouldBe(10);
    }

    /// <summary>
    /// The name as written beats the name with markers stripped, so a guide that spells a channel exactly
    /// as the provider does is not overruled by a looser match on a different entry.
    /// </summary>
    [Fact]
    public void Match_PrefersAnExactNameOverARelaxedOne()
    {
        // Arrange
        var channels = new[] { Channel(1, "Sky Sport HD") };

        var guideChannels = new[]
        {
            GuideChannel(10, "relaxed", "Sky Sport"),
            GuideChannel(11, "exact", "Sky Sport HD"),
        };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links[1].ShouldBe(11);
    }

    /// <summary>
    /// Half a guide attached to the wrong channel is indistinguishable from a broken player. A channel
    /// with no listing is merely a channel with no listing.
    /// </summary>
    [Fact]
    public void Match_LeavesAChannelUnmatchedWhenTheNameIsAmbiguous()
    {
        // Arrange: two guide channels reduce to the same key, so neither can be the answer.
        var channels = new[] { Channel(1, "TF1") };

        var guideChannels = new[]
        {
            GuideChannel(10, "tf1.a", "FR: TF1 HD"),
            GuideChannel(11, "tf1.b", "TF1 FHD"),
        };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links.ShouldNotContainKey(1);
    }

    [Fact]
    public void Match_LeavesAChannelUnmatchedWhenNothingResembblesIt()
    {
        // Arrange
        var channels = new[] { Channel(1, "Obscure Local Channel") };
        var guideChannels = new[] { GuideChannel(10, "tf1.fr", "TF1") };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links.ShouldBeEmpty();
    }

    /// <summary>
    /// Guides reference channels they never declare, which leaves a guide channel with an identifier and
    /// no name. Those must not all collapse into one match by way of an empty key.
    /// </summary>
    [Fact]
    public void Match_IgnoresGuideChannelsWithNoName()
    {
        // Arrange
        var channels = new[] { Channel(1, "TF1"), Channel(2, "ARD") };

        var guideChannels = new[]
        {
            GuideChannel(10, "undeclared.one", displayName: null),
            GuideChannel(11, "undeclared.two", displayName: null),
        };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links.ShouldBeEmpty();
    }

    /// <summary>
    /// A timeshift channel is a different channel. Attaching the original's programmes to it would be
    /// wrong for every hour of the day.
    /// </summary>
    [Fact]
    public void Match_DoesNotMatchATimeshiftChannelToItsOriginal()
    {
        // Arrange
        var channels = new[] { Channel(1, "FR: TF1 +1 HD") };
        var guideChannels = new[] { GuideChannel(10, "tf1.fr", "TF1") };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links.ShouldBeEmpty();
    }

    /// <summary>
    /// A stale identifier must not win over a name that does match, or a channel whose guide id the
    /// provider mistyped would show nothing where the name alone would have found its listings.
    /// </summary>
    [Fact]
    public void Match_UsesTheNameWhenTheStatedIdentifierIsUnknown()
    {
        // Arrange
        var channels = new[] { Channel(1, "TF1", epgChannelId: "no.such.channel") };
        var guideChannels = new[] { GuideChannel(10, "tf1.fr", "TF1") };

        // Act
        var links = GuideChannelMatcher.Match(channels, guideChannels);

        // Assert
        links[1].ShouldBe(10);
    }

    private static Channel Channel(int id, string name, string? epgChannelId = null)
    {
        return new Channel
        {
            Id = id,
            ExternalId = id.ToString(CultureInfo.InvariantCulture),
            Name = name,
            EpgChannelId = epgChannelId,
        };
    }

    private static GuideChannel GuideChannel(int id, string externalId, string? displayName)
    {
        return new GuideChannel
        {
            Id = id,
            ExternalId = externalId,
            DisplayName = displayName,
        };
    }
}
