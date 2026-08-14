using LTR.Core.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers the wording of a playback failure.
/// </summary>
/// <remarks>
/// The same guard the import stages have, for the same reason: a reason added later would otherwise read as
/// the fallback, which is exactly how the longest step of a film import came to say "Working...".
/// </remarks>
public sealed class StreamFailureTextTests
{
    [Theory]
    [MemberData(nameof(EveryReasonWorthWording))]
    public void EveryReason_HasWordingOfItsOwn(StreamFailureReason reason)
    {
        // Arrange
        var fallback = StreamFailureText.Describe(StreamFailureReason.Unknown);

        // Act
        var wording = StreamFailureText.Describe(reason);

        // Assert
        wording.ShouldNotBeNullOrWhiteSpace();
        wording.ShouldNotBe(fallback, $"{reason} needs a sentence of its own");
    }

    /// <remarks>
    /// The connection limit and the expiry are the two the milestone was about, so they are asserted
    /// concretely rather than only as "not the fallback": both name what to do, and one says plainly that
    /// there is nothing to do here.
    /// </remarks>
    [Fact]
    public void TheTwoCausesThatWereConflated_NowSayDifferentThings()
    {
        // Act
        var limit = StreamFailureText.Describe(StreamFailureReason.ConnectionLimitReached);
        var expired = StreamFailureText.Describe(StreamFailureReason.SubscriptionExpired);

        // Assert
        limit.ShouldContain("other device");
        expired.ShouldContain("renewed");
    }

    public static TheoryData<StreamFailureReason> EveryReasonWorthWording()
    {
        var data = new TheoryData<StreamFailureReason>();

        foreach (var reason in Enum.GetValues<StreamFailureReason>())
        {
            // Unknown *is* the fallback, so it cannot be asserted to differ from itself.
            if (reason != StreamFailureReason.Unknown)
            {
                data.Add(reason);
            }
        }

        return data;
    }
}
