using LTR.Core.Playback;

namespace LTR.Cli;

/// <summary>
/// Covers the wording the CLI prints when a stream would not open.
/// </summary>
/// <remarks>
/// <para>
/// The window has had this guard since M6; the CLI is the front end that existed *for diagnosing* and had
/// none, because there was no test project to put it in. A reason added to
/// <see cref="StreamFailureReason"/> and not worded here reads as the fallback — "the panel gave no usable
/// answer" — which is the one sentence that means the opposite of what happened, and the same way the
/// longest step of a film import came to say "Working...".
/// </para>
/// <para>
/// Deliberately not asserted against the window's wording. The two differ on purpose: the window tells a
/// viewer what to do, this says what the panel reported. What both have to satisfy is that every reason is
/// distinguishable from not knowing.
/// </para>
/// </remarks>
public sealed class StreamFailureNotesTests
{
    [Theory]
    [MemberData(nameof(EveryReasonWorthWording))]
    public void EveryReason_HasWordingOfItsOwn(StreamFailureReason reason)
    {
        // Arrange
        var fallback = StreamFailureNotes.Describe(StreamFailureReason.Unknown);

        // Act
        var note = StreamFailureNotes.Describe(reason);

        // Assert
        note.ShouldNotBeNullOrWhiteSpace();
        note.ShouldNotBe(fallback, $"{reason} needs a sentence of its own");
    }

    /// <remarks>
    /// The connection limit is the one reason a check has to tell apart from a real fault, because running two
    /// play-tests in succession against a one-connection subscription produces it — and reading that as a
    /// broken stream is the mistake `CLAUDE.md` warns about by name. So it must say that a previous run is the
    /// likely cause, not merely differ from the fallback.
    /// </remarks>
    [Fact]
    public void TheConnectionLimit_PointsAtThePreviousPlayTest()
    {
        // Act
        var note = StreamFailureNotes.Describe(StreamFailureReason.ConnectionLimitReached);

        // Assert
        note.ShouldContain("previous play-test");
    }

    /// <remarks>
    /// Where the window would tell a viewer to try another channel, this has to send whoever is diagnosing to
    /// the command that shows the panel's own figures. A note that stopped naming it would leave the reader
    /// with no next step, which is the whole job of this wording.
    /// </remarks>
    [Fact]
    public void WhenNothingIsKnown_ItNamesTheCommandThatWouldSay()
    {
        // Act
        var note = StreamFailureNotes.Describe(StreamFailureReason.Unknown);

        // Assert
        note.ShouldContain("probe");
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
