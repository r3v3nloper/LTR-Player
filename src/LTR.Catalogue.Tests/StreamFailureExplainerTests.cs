using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Catalogue;

/// <summary>
/// Covers turning a refused stream into a reason the viewer can act on.
/// </summary>
/// <remarks>
/// The classification is the whole substance: an offline channel, a subscription with every connection in
/// use, and one that expired last night are indistinguishable from inside the engine and want three
/// different things from the viewer. Getting it wrong is worse than saying nothing, because it sends people
/// to a remedy that cannot work.
/// </remarks>
public sealed class StreamFailureExplainerTests
{
    [Fact]
    public async Task AnAccountInOrder_LeavesTheChannelToBlame()
    {
        // Arrange
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            Account = Account(AccountStatus.Active, maxConnections: 2, activeConnections: 1),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ChannelUnavailable);
    }

    [Fact]
    public async Task EveryConnectionInUse_IsReportedAsTheLimit()
    {
        // Arrange: the failure this player is most often blamed for, and the one that clears on its own.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            Account = Account(AccountStatus.Active, maxConnections: 1, activeConnections: 1),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ConnectionLimitReached);
    }

    [Fact]
    public async Task APanelThatReportsNoLimit_IsNotAccusedOfHavingReachedOne()
    {
        // Arrange: plenty of panels report zero for both figures, meaning "not counted" rather than "none
        // allowed". Reading that as a limit would blame the connection count on every offline channel.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            Account = Account(AccountStatus.Active, maxConnections: 0, activeConnections: 0),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ChannelUnavailable);
    }

    [Theory]
    [InlineData(AccountStatus.Expired, StreamFailureReason.SubscriptionExpired)]
    [InlineData(AccountStatus.Banned, StreamFailureReason.SubscriptionDisabled)]
    [InlineData(AccountStatus.AuthenticationFailed, StreamFailureReason.CredentialsRejected)]
    [InlineData(AccountStatus.Unknown, StreamFailureReason.Unknown)]
    public async Task TheAccountsOwnStatus_IsPassedOn(AccountStatus status, StreamFailureReason expected)
    {
        // Arrange
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            Account = Account(status, maxConnections: 1, activeConnections: 0),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(expected);
    }

    /// <remarks>
    /// An expired subscription reports nonsense connection counts, and "every connection is in use" would
    /// send the viewer hunting for a second device that is not the problem.
    /// </remarks>
    [Fact]
    public async Task AnExpiredSubscription_IsNotReportedAsAConnectionLimit()
    {
        // Arrange
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            Account = Account(AccountStatus.Expired, maxConnections: 1, activeConnections: 1),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.SubscriptionExpired);
    }

    [Fact]
    public async Task APlaylistSource_IsNotAsked()
    {
        // Arrange: a playlist has no account to report on, and asking re-downloads the whole document to
        // learn nothing about the question.
        var registry = new FakeProviderRegistry(CreateM3uSource());

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ChannelUnavailable);
        registry.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AProviderThatCannotBeReached_IsReportedAsSuch()
    {
        // Arrange: as likely to be this machine's connection as the provider's, and the wording says so.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            AuthenticateException = new HttpRequestException("no route to host"),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ProviderUnreachable);
    }

    /// <remarks>
    /// An explanation that throws would replace the playback failure with its own, which is how a channel
    /// being off the air turns into an error dialog about JSON.
    /// </remarks>
    [Fact]
    public async Task AnExplanationThatFails_DegradesToUnknown()
    {
        // Arrange: what a panel serving an HTML error page at HTTP 200 produces, one layer down.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            AuthenticateException = new InvalidOperationException("the panel replied with HTML"),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.Unknown);
    }

    [Fact]
    public async Task ARequestTimeout_IsAReachabilityProblemRatherThanACancellation()
    {
        // Arrange: HttpClient reports its own timeout as a cancellation, indistinguishable from the caller
        // giving up except by asking the token. Treating it as the caller's would report nothing at all.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            AuthenticateException = new TaskCanceledException("the request timed out"),
        };

        // Act
        var reason = await Explain(registry);

        // Assert
        reason.ShouldBe(StreamFailureReason.ProviderUnreachable);
    }

    [Fact]
    public async Task RealCancellation_IsNotSwallowed()
    {
        // Arrange: the window closing must not be reported to the viewer as a provider problem.
        var registry = new FakeProviderRegistry(CreateXtreamSource())
        {
            AuthenticateException = new OperationCanceledException(),
        };

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var explainer = new StreamFailureExplainer(
            registry,
            NullLogger<StreamFailureExplainer>.Instance);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => explainer.ExplainAsync(registry.Source, cancelled.Token));
    }

    private static async Task<StreamFailureReason> Explain(FakeProviderRegistry registry)
    {
        var explainer = new StreamFailureExplainer(
            registry,
            NullLogger<StreamFailureExplainer>.Instance);

        return await explainer.ExplainAsync(registry.Source, TestContext.Current.CancellationToken);
    }

    private static ProviderAccount Account(
        AccountStatus status,
        int maxConnections,
        int activeConnections)
    {
        return new ProviderAccount(
            status,
            ExpiresAtUtc: null,
            IsTrial: false,
            maxConnections,
            activeConnections,
            AllowedFormats: [StreamFormat.MpegTs]);
    }

    private static XtreamSource CreateXtreamSource()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Panel",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
        };
    }

    private static M3uSource CreateM3uSource()
    {
        return new M3uSource
        {
            Id = 2,
            Name = "Playlist",
            PlaylistUrl = new Uri("http://panel.example/get.php?type=m3u_plus", UriKind.Absolute),
        };
    }
}
