namespace LTR.Core.Playback;

public sealed class ResumePolicyTests
{
    private static readonly TimeSpan FilmLength = TimeSpan.FromMinutes(100);

    [Fact]
    public void Classify_BelowTheMinimum_Discards()
    {
        // Arrange: opened, glanced at, abandoned. Not something to offer resuming.
        var position = ResumePolicy.MinimumWatched - TimeSpan.FromSeconds(1);

        // Act
        var outcome = ResumePolicy.Classify(position, FilmLength);

        // Assert
        outcome.ShouldBe(WatchOutcome.Discard);
    }

    [Fact]
    public void Classify_AtTheMinimum_IsResumable()
    {
        // Arrange & Act
        var outcome = ResumePolicy.Classify(ResumePolicy.MinimumWatched, FilmLength);

        // Assert
        outcome.ShouldBe(WatchOutcome.Resumable);
    }

    [Fact]
    public void Classify_PartWay_IsResumable()
    {
        // Arrange & Act
        var outcome = ResumePolicy.Classify(TimeSpan.FromMinutes(40), FilmLength);

        // Assert
        outcome.ShouldBe(WatchOutcome.Resumable);
    }

    [Fact]
    public void Classify_WithinTheTail_IsFinished()
    {
        // Arrange: stopping during the credits is finishing it.
        var position = FilmLength - ResumePolicy.FinishedTail + TimeSpan.FromSeconds(1);

        // Act
        var outcome = ResumePolicy.Classify(position, FilmLength);

        // Assert
        outcome.ShouldBe(WatchOutcome.Finished);
    }

    [Fact]
    public void Classify_ShortEpisodeNearItsEnd_IsFinishedByFraction()
    {
        // Arrange: for a five-minute extra the two-minute tail would be nearly half of it, so the
        // fractional rule is what recognises the end.
        var duration = TimeSpan.FromMinutes(5);
        var position = TimeSpan.FromSeconds(duration.TotalSeconds * 0.99);

        // Act
        var outcome = ResumePolicy.Classify(position, duration);

        // Assert
        outcome.ShouldBe(WatchOutcome.Finished);
    }

    [Fact]
    public void Classify_BeyondTheEnd_IsFinished()
    {
        // Arrange: engines report a position a shade past the duration at the end of a file.
        var position = FilmLength + TimeSpan.FromSeconds(2);

        // Act
        var outcome = ResumePolicy.Classify(position, FilmLength);

        // Assert
        outcome.ShouldBe(WatchOutcome.Finished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Classify_WithUnknownDuration_IsResumableRatherThanFinished(int durationSeconds)
    {
        // Arrange: an unknown length means the end cannot be recognised. Remembering the position is
        // recoverable; declaring something finished that is not loses the viewer's place.
        var duration = TimeSpan.FromSeconds(durationSeconds);

        // Act
        var outcome = ResumePolicy.Classify(TimeSpan.FromMinutes(30), duration);

        // Assert
        outcome.ShouldBe(WatchOutcome.Resumable);
    }

    [Fact]
    public void StartFrom_RewindsForContext()
    {
        // Arrange & Act
        var start = ResumePolicy.StartFrom(TimeSpan.FromMinutes(40));

        // Assert
        start.ShouldBe(TimeSpan.FromMinutes(40) - ResumePolicy.RewindOnResume);
    }

    [Fact]
    public void StartFrom_NeverGoesNegative()
    {
        // Arrange: a stored position shorter than the rewind would otherwise seek before the start,
        // which LibVLC answers by refusing to play at all.
        var start = ResumePolicy.StartFrom(TimeSpan.FromSeconds(3));

        // Act & Assert
        start.ShouldBe(TimeSpan.Zero);
    }
}
