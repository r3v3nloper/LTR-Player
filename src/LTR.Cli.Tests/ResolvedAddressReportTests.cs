using System.IO;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using LTR.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli;

/// <summary>
/// Covers whether a paid subscription's credentials reach the console.
/// </summary>
/// <remarks>
/// <para>
/// The one security-adjacent decision in the CLI, and it was correct without anything holding it there. The
/// address is the point of both resolve commands, but console output ends up in scrollback, screenshots and
/// bug reports — so the default is masked and <c>--reveal</c> is the only way past it.
/// </para>
/// <para>
/// Through a real container with both protocol packages, not a fake sanitiser. A fake would prove that
/// whatever it was told to return got printed, which is not the question: what is being asserted is that the
/// credentials of a real source, masked by the real sanitiser for its protocol, are gone from the line.
/// </para>
/// </remarks>
public sealed class ResolvedAddressReportTests
{
    [Fact]
    public void ByDefault_ThePanelsCredentialsAreGoneFromTheAddress()
    {
        // Arrange
        using var providers = ProviderLayer();
        var source = PanelSource();

        var request = Request($"http://panel.example:8080/live/{source.Username}/{source.Password}/101.ts");

        // Act
        var report = Print(providers, request, source, revealCredentials: false);

        // Assert
        report.ShouldNotContain("alice");
        report.ShouldNotContain("s3cret");
        report.ShouldContain("Credentials are masked");
        report.ShouldContain("--reveal");
    }

    /// <remarks>
    /// The other half of the gate. A command that masked even when asked not to would be useless for the job
    /// it exists for — pasting a working address into another player — and the note must not then claim a
    /// masking it did not perform.
    /// </remarks>
    [Fact]
    public void WithReveal_TheAddressIsPrintedVerbatimAndNothingClaimsOtherwise()
    {
        // Arrange
        using var providers = ProviderLayer();
        var source = PanelSource();
        var url = $"http://panel.example:8080/live/{source.Username}/{source.Password}/101.ts";

        // Act
        var report = Print(providers, Request(url), source, revealCredentials: true);

        // Assert
        report.ShouldContain(url);
        report.ShouldNotContain("masked");
    }

    /// <summary>
    /// The documented gap has to report itself, rather than claiming a masking that did not happen.
    /// </summary>
    /// <remarks>
    /// A playlist source holds no credentials of its own, so its *path* can only be redacted from the values in
    /// its own playlist address. Given a playlist with no query — a local file — there is nothing on record to
    /// tell a secret segment from a route, and sanitising changes nothing. That once printed
    /// <c>http://provider.invalid/live/alice/s3cret/101.ts</c> in clear under a note saying credentials were
    /// masked, which is the wording this asserts is gone.
    /// </remarks>
    [Fact]
    public void WhenNothingCouldBeIdentifiedAsACredential_ItSaysSoRatherThanClaimingAMasking()
    {
        // Arrange
        using var providers = ProviderLayer();
        var source = new M3uSource { Name = "Playlist", PlaylistUrl = new Uri("file:///c:/tv/list.m3u") };

        // Act
        var report = Print(
            providers,
            Request("http://provider.invalid/live/alice/s3cret/101.ts"),
            source,
            revealCredentials: false);

        // Assert
        report.ShouldContain("Nothing in this address could be identified as a credential");
        report.ShouldContain("Treat it as sensitive");
        report.ShouldNotContain("Credentials are masked");
    }

    /// <remarks>
    /// The format and the agent are why the command is run at all — a stream that will not play is diagnosed
    /// from the shape asked for and the agent it was asked with — so masking must not cost them.
    /// </remarks>
    [Fact]
    public void TheFormatAndAgentAreReported_MaskedOrNot()
    {
        // Arrange
        using var providers = ProviderLayer();

        // Act
        var report = Print(
            providers,
            Request("http://panel.example:8080/live/alice/s3cret/101.ts"),
            PanelSource(),
            revealCredentials: false);

        // Assert
        report.ShouldContain("MpegTs");
        report.ShouldContain("LTR-Player-Test");
    }

    private static string Print(
        ServiceProvider providers,
        MediaRequest request,
        PlaylistSource source,
        bool revealCredentials)
    {
        var output = new StringWriter();
        var report = new ResolvedAddressReport(providers.GetRequiredService<IProviderRegistry>(), output);

        report.Print(request, source, revealCredentials);

        return output.ToString();
    }

    private static MediaRequest Request(string url)
    {
        return new MediaRequest(new Uri(url), "LTR-Player-Test", StreamFormat.MpegTs, "Channel 101");
    }

    /// <remarks>
    /// Both protocol packages, as the applications register them: the rules differ by protocol, and asking the
    /// registry is what the report does.
    /// </remarks>
    private static ServiceProvider ProviderLayer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProviderRegistry();
        services.AddXtreamProvider();
        services.AddM3uProvider();

        return services.BuildServiceProvider();
    }

    private static XtreamSource PanelSource()
    {
        return new XtreamSourceBuilder()
            .WithName("Panel")
            .WithCredentials("alice", "s3cret")
            .Build();
    }
}
