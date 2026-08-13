namespace LTR.Core.Content;

/// <summary>
/// Cases taken from a real provider's channel list.
/// </summary>
public sealed class ChannelNamingTests
{
    [Theory]
    [InlineData("FR: ----- FRANCE -----")]
    [InlineData("##### SPORT #####")]
    [InlineData("===== VOD =====")]
    [InlineData("▬▬▬▬ NEWS ▬▬▬▬")]
    [InlineData("****")]
    [InlineData("---")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSeparatorLabel_RecognisesDecorativeRows(string? name)
    {
        // Arrange & Act
        var isSeparator = ChannelNaming.IsSeparatorLabel(name);

        // Assert
        isSeparator.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FR: TF1 HD")]
    [InlineData("FR: TF1+1 FHD")]
    [InlineData("FR: TF1 4K ( LIVE EVENTS )")]
    [InlineData("FR: E! ENTERTAINMENT HD")]
    [InlineData("DE: SAT.1 HD")]
    [InlineData("Sky Sport Bundesliga 1 - HD")]
    [InlineData("Etc...")]
    [InlineData("A&E")]
    [InlineData("13ème RUE")]
    [InlineData("قناة الجزيرة")]
    public void IsSeparatorLabel_LeavesGenuineChannelsAlone(string name)
    {
        // Arrange & Act
        var isSeparator = ChannelNaming.IsSeparatorLabel(name);

        // Assert
        isSeparator.ShouldBeFalse();
    }

    [Fact]
    public void IsSeparatorLabel_DoesNotMistakeAnEllipsisForDecoration()
    {
        // Arrange: three repetitions is why the threshold is four. A truncated or stylised name must
        // survive, whereas a drawn rule must not.
        // Act & Assert
        ChannelNaming.IsSeparatorLabel("To Be Continued...").ShouldBeFalse();
        ChannelNaming.IsSeparatorLabel("News ---- Live").ShouldBeTrue();
    }

    /// <summary>
    /// The names on either side of a guide match are written by different parties. These are the
    /// differences that have to be discarded for the two to meet.
    /// </summary>
    [Theory]
    [InlineData("FR: TF1 HD", "tf1")]
    [InlineData("TF1", "tf1")]
    [InlineData("[DE] Sat.1 FHD", "sat1")]
    [InlineData("UK | Sky Sports Main Event 4K", "skysportsmainevent")]
    [InlineData("DE - ARD", "ard")]
    [InlineData("Eurosport 1 HEVC", "eurosport1")]
    [InlineData("  ARD   ", "ard")]
    public void ToGuideMatchKey_DiscardsRegionTagsAndQualityMarkers(string name, string expected)
    {
        // Arrange & Act
        var key = ChannelNaming.ToGuideMatchKey(name);

        // Assert
        key.ShouldBe(expected);
    }

    /// <summary>
    /// A timeshift channel shows something else than the channel it is named after, so collapsing the two
    /// would attach the wrong programme to it — worse than attaching none.
    /// </summary>
    [Fact]
    public void ToGuideMatchKey_KeepsATimeshiftMarker()
    {
        // Arrange & Act
        var timeshift = ChannelNaming.ToGuideMatchKey("FR: TF1 +1 HD");
        var original = ChannelNaming.ToGuideMatchKey("FR: TF1 HD");

        // Assert
        timeshift.ShouldBe("tf1+1");
        timeshift.ShouldNotBe(original);
    }

    /// <summary>
    /// The stripping is narrow on purpose. A name that is nothing but markers, or whose leading word is
    /// part of the name, has to keep enough to match on.
    /// </summary>
    [Theory]
    [InlineData("HD", "hd")]
    [InlineData("4K", "4k")]
    [InlineData("Eurosport 1", "eurosport1")]
    [InlineData("HDTV Kanal", "hdtvkanal")]
    public void ToGuideMatchKey_NeverStripsAwayEverything(string name, string expected)
    {
        // Arrange & Act
        var key = ChannelNaming.ToGuideMatchKey(name);

        // Assert
        key.ShouldBe(expected);
    }

    /// <summary>
    /// Identity and guide matching pull in opposite directions, which is why they are two methods:
    /// identity must keep every distinction the provider makes, matching must discard the cosmetic ones.
    /// </summary>
    [Fact]
    public void ToIdentityKey_KeepsWhatGuideMatchingDiscards()
    {
        // Arrange & Act
        var identity = ChannelNaming.ToIdentityKey("FR: TF1 HD");
        var match = ChannelNaming.ToGuideMatchKey("FR: TF1 HD");

        // Assert
        identity.ShouldBe("frtf1hd");
        match.ShouldBe("tf1");
    }
}
