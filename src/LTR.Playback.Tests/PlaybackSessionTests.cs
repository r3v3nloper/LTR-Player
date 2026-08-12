using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Playback;

/// <summary>
/// Covers the single-active-stream rule. These are the most important tests in the project: every
/// failure here corresponds to a provider locking the user out of their own subscription.
/// </summary>
public sealed class PlaybackSessionTests
{
    private static readonly TimeSpan ShortStopTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task SwitchToAsync_ReleasesThePreviousStreamBeforeOpeningTheNextOne()
    {
        // Arrange
        var engine = new FakeMediaEngine();
        await using var session = CreateSession(engine);

        // Act
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);
        await session.SwitchToAsync(Request("Zweite"), TestContext.Current.CancellationToken);

        // Assert: the stop for the second switch must precede its play.
        engine.Calls.ShouldBe(["stop", "play:Erste", "stop", "play:Zweite"]);
    }

    [Fact]
    public async Task SwitchToAsync_StopsEvenWhenNothingIsPlayingYet()
    {
        // Arrange: the rule is unconditional, so that a connection leaked by a previous process run
        // or a failed start is always cleared before a new stream is opened.
        var engine = new FakeMediaEngine();
        await using var session = CreateSession(engine);

        // Act
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);

        // Assert
        engine.Calls[0].ShouldBe("stop");
    }

    [Fact]
    public async Task SwitchToAsync_NeverHoldsTwoStreamsAtOnce()
    {
        // Arrange: a slow release is when overlapping would happen if the ordering were not enforced.
        var engine = new FakeMediaEngine { StopDelay = TimeSpan.FromMilliseconds(20) };
        await using var session = CreateSession(engine);

        // Act
        for (var index = 0; index < 5; index++)
        {
            await session.SwitchToAsync(Request($"Channel {index}"), TestContext.Current.CancellationToken);
        }

        // Assert: the fake throws on a second concurrent open, so reaching here proves the ordering.
        engine.HasOpenStream.ShouldBeTrue("exactly the last stream stays open");
        session.Current.ShouldNotBeNull();
        session.Current.DisplayName.ShouldBe("Channel 4");
    }

    [Fact]
    public async Task SwitchToAsync_WhenZappedThrough_OnlyOpensTheStreamTheUserSettledOn()
    {
        // Arrange: holding down the channel key must not open every channel in passing.
        var engine = new FakeMediaEngine { StopDelay = TimeSpan.FromMilliseconds(30) };
        await using var session = CreateSession(engine);

        // Act: fire the switches together, as a burst of key presses would.
        var switches = Enumerable.Range(0, 6)
            .Select(index => session.SwitchToAsync(
                Request($"Channel {index}"),
                TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(switches);

        // Assert: superseded requests step aside, so far fewer plays happen than switches requested.
        var plays = engine.Calls.Count(call => call.StartsWith("play:", StringComparison.Ordinal));
        plays.ShouldBeLessThan(6);
        plays.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task StopAsync_ReleasesTheStreamAndForgetsIt()
    {
        // Arrange
        var engine = new FakeMediaEngine();
        await using var session = CreateSession(engine);
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);

        // Act
        await session.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        engine.HasOpenStream.ShouldBeFalse();
        session.Current.ShouldBeNull();
    }

    [Fact]
    public async Task SwitchToAsync_WhenStartingFails_ReleasesTheConnectionAndReportsTheFailure()
    {
        // Arrange: a failed start may still have opened a connection provider-side.
        var engine = new FakeMediaEngine { PlayException = new InvalidOperationException("codec missing") };
        await using var session = CreateSession(engine);

        // Act
        var act = async () => await session.SwitchToAsync(
            Request("Erste"),
            TestContext.Current.CancellationToken);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        engine.Calls.Count(call => call == "stop").ShouldBe(2, "once before starting, once to clean up");
        session.Current.ShouldBeNull();
    }

    [Fact]
    public async Task SwitchToAsync_WhenTheEngineFailsToRelease_StillOpensTheNextStream()
    {
        // Arrange: a broken stop must not leave the player permanently unable to change channel.
        var engine = new FakeMediaEngine { StopException = new TimeoutException("engine wedged") };
        await using var session = CreateSession(engine);

        // Act
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);

        // Assert
        engine.Calls.ShouldContain("play:Erste");
    }

    [Fact]
    public async Task SwitchToAsync_WhenTheEngineHangsOnRelease_GivesUpAfterTheTimeout()
    {
        // Arrange: a hung engine must not freeze the UI indefinitely.
        var engine = new FakeMediaEngine { StopDelay = TimeSpan.FromSeconds(30) };
        await using var session = CreateSession(engine);

        // Act
        var start = TimeProvider.System.GetTimestamp();
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);
        var elapsed = TimeProvider.System.GetElapsedTime(start);

        // Assert
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        engine.Calls.ShouldContain("play:Erste");
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheStreamAndDisposesTheEngine()
    {
        // Arrange: a process that exits holding a stream is the most common cause of a lockout.
        var engine = new FakeMediaEngine();
        var session = CreateSession(engine);
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);

        // Act
        await session.DisposeAsync();

        // Assert
        engine.HasOpenStream.ShouldBeFalse();
        engine.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsHarmless()
    {
        // Arrange
        var engine = new FakeMediaEngine();
        var session = CreateSession(engine);

        // Act
        await session.DisposeAsync();
        var act = async () => await session.DisposeAsync();

        // Assert
        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task SwitchToAsync_AfterDisposal_IsRejected()
    {
        // Arrange
        var engine = new FakeMediaEngine();
        var session = CreateSession(engine);
        await session.DisposeAsync();

        // Act
        var act = async () => await session.SwitchToAsync(
            Request("Erste"),
            TestContext.Current.CancellationToken);

        // Assert
        await act.ShouldThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StateChanged_IsForwardedFromTheEngine()
    {
        // Arrange
        var engine = new FakeMediaEngine();
        await using var session = CreateSession(engine);
        var observed = new List<PlaybackState>();
        session.StateChanged += (_, e) => observed.Add(e.Current);

        // Act
        await session.SwitchToAsync(Request("Erste"), TestContext.Current.CancellationToken);

        // Assert
        observed.ShouldContain(PlaybackState.Playing);
    }

    private static PlaybackSession CreateSession(FakeMediaEngine engine)
    {
        return new PlaybackSession(engine, NullLogger<PlaybackSession>.Instance, ShortStopTimeout);
    }

    private static MediaRequest Request(string displayName)
    {
        return new MediaRequest(
            new Uri($"http://panel.example/live/u/p/{displayName.GetHashCode(StringComparison.Ordinal)}.ts"),
            "TestAgent/1.0",
            StreamFormat.MpegTs,
            displayName);
    }
}
