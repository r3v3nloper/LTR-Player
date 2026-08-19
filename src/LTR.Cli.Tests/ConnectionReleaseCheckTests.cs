using System.IO;
using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Cli;

/// <summary>
/// Covers the verdict the play-tests exist to produce.
/// </summary>
/// <remarks>
/// <para>
/// `CLAUDE.md` calls this the real test: everything else about a stream can be checked by pasting its address
/// into VLC, and whether the connection came back cannot. Which made it the one decision in the CLI worth
/// holding, and — until the writer was injected — the one nothing could reach.
/// </para>
/// <para>
/// The distinction each case is about is the same one: a panel that takes a few seconds to notice a client has
/// gone looks exactly like a panel still holding the connection. Reading the lag as a leak condemns correct
/// code; reading a leak as lag is how a leak ships.
/// </para>
/// </remarks>
public sealed class ConnectionReleaseCheckTests
{
    [Fact]
    public async Task WhenTheConnectionIsAlreadyGone_ItReportsCleanTeardownAndAsksOnce()
    {
        // Arrange
        var panel = new AnsweringPanel(AnsweringPanel.Counting(active: 0));
        var (check, output) = Checking(panel);

        // Act
        await check.ReportAsync(Source(), TestContext.Current.CancellationToken);

        // Assert
        output.ToString().ShouldContain("Teardown is clean");
        panel.AskCount.ShouldBe(1, "a clean answer ends the polling");
    }

    /// <remarks>
    /// The ordinary case against a real panel, and the reason this polls at all. It has to read as clean —
    /// with the delay accounted for, so nobody reads the extra checks as a fault.
    /// </remarks>
    [Fact]
    public async Task WhenThePanelNeedsAMomentToNotice_ItStillReportsCleanTeardown()
    {
        // Arrange
        var panel = new AnsweringPanel(
            AnsweringPanel.Counting(active: 1),
            AnsweringPanel.Counting(active: 1),
            AnsweringPanel.Counting(active: 0));

        var (check, output) = Checking(panel);

        // Act
        await check.ReportAsync(Source(), TestContext.Current.CancellationToken);

        // Assert
        var report = output.ToString();
        report.ShouldContain("Teardown is clean");
        report.ShouldContain("needed a moment to notice");
        panel.AskCount.ShouldBe(3);
    }

    /// <remarks>
    /// The one verdict that means this application has a defect, so it must not be reachable by lag alone: it
    /// is only printed once every attempt has been spent. It also has to name the other possibility, because a
    /// second device on the same subscription produces exactly this and is not the player's fault.
    /// </remarks>
    [Fact]
    public async Task WhenTheConnectionIsCountedThroughout_ItSaysSoAndNamesBothCauses()
    {
        // Arrange
        var panel = new AnsweringPanel(AnsweringPanel.Counting(active: 1));
        var (check, output) = Checking(panel);

        // Act
        await check.ReportAsync(Source(), TestContext.Current.CancellationToken);

        // Assert
        var report = output.ToString();
        report.ShouldContain("still counted as open throughout");
        report.ShouldContain("leaked");
        report.ShouldContain("another device");
        report.ShouldNotContain("clean");
        panel.AskCount.ShouldBe(5, "every attempt is spent before a leak is declared");
    }

    /// <remarks>
    /// Silence is the correct output here, not a reassurance. A playlist has no account behind it and many
    /// panels report no counts at all; either way nothing was established, and "teardown is clean" would be a
    /// claim this check did not make.
    /// </remarks>
    [Fact]
    public async Task WhenThePanelCountsNothingAtAll_ItClaimsNothing()
    {
        // Arrange
        var panel = new AnsweringPanel(AnsweringPanel.CountingNothing());
        var (check, output) = Checking(panel);

        // Act
        await check.ReportAsync(Source(), TestContext.Current.CancellationToken);

        // Assert
        output.ToString().ShouldBeEmpty();
        panel.AskCount.ShouldBe(1);
    }

    /// <remarks>
    /// The progress lines are what a person watching a twenty-second wait reads, and they have to say which
    /// check they are and how many there will be — a bare "still counted" three times over reads as a hang.
    /// </remarks>
    [Fact]
    public async Task WhileItWaits_ItSaysWhichCheckItIsOn()
    {
        // Arrange
        var panel = new AnsweringPanel(
            AnsweringPanel.Counting(active: 1),
            AnsweringPanel.Counting(active: 0));

        var (check, output) = Checking(panel);

        // Act
        await check.ReportAsync(Source(), TestContext.Current.CancellationToken);

        // Assert
        output.ToString().ShouldContain("check 1/5");
    }

    /// <remarks>
    /// <see cref="ConnectionReleaseCheck.PollDelay"/> is zeroed, which is the only reason these five tests take
    /// milliseconds: the real cadence would spend twenty seconds per exhausted case proving nothing about the
    /// waiting.
    /// </remarks>
    private static (ConnectionReleaseCheck Check, StringWriter Output) Checking(AnsweringPanel panel)
    {
        var output = new StringWriter();

        var check = new ConnectionReleaseCheck(panel, output)
        {
            PollDelay = TimeSpan.Zero,
        };

        return (check, output);
    }

    private static XtreamSource Source()
    {
        return new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();
    }
}
