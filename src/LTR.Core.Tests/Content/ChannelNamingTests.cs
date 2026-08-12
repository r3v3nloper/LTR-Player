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
}
