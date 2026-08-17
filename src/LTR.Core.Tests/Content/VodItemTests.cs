namespace LTR.Core.Content;

/// <summary>
/// The rule that decides whether a film's detail is worth asking the provider for again.
/// </summary>
/// <remarks>
/// Tested here rather than only through the service, because it is a rule about two stored fields and needs
/// neither a database nor a provider to state. What the service adds is *which* answers set the second field.
/// </remarks>
public sealed class VodItemTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeedsDetailFetch_WhenNothingHasBeenAsked_IsTrue()
    {
        // Arrange
        var movie = Movie();

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeTrue();
    }

    [Fact]
    public void NeedsDetailFetch_WhenTheDetailIsStored_IsFalse()
    {
        // Arrange
        var movie = Movie();
        movie.HasDetail = true;

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeFalse();
    }

    [Fact]
    public void NeedsDetailFetch_WhenTheDetailIsStored_StaysFalseHoweverOldTheAttemptIs()
    {
        // Arrange: a stored synopsis does not go stale. Only the *absence* of one is worth revisiting.
        var movie = Movie();
        movie.HasDetail = true;
        movie.DetailAttemptedUtc = Noon.AddYears(-1);

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeFalse();
    }

    [Fact]
    public void NeedsDetailFetch_WhenAskedRecentlyAndThereWasNothing_IsFalse()
    {
        // Arrange: this is the case the column exists for — "asked, and there is nothing", which used to be
        // indistinguishable from "never asked".
        var movie = Movie();
        movie.DetailAttemptedUtc = Noon.AddHours(-1);

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeFalse();
    }

    [Fact]
    public void NeedsDetailFetch_WhenTheRetryIntervalHasElapsed_IsTrue()
    {
        // Arrange
        var movie = Movie();
        movie.DetailAttemptedUtc = Noon - VodItem.DetailRetryInterval;

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeTrue();
    }

    [Fact]
    public void NeedsDetailFetch_ForAnAttemptStampedInTheFuture_IsFalse()
    {
        // Arrange: a database written while the clock was wrong, or one carried between machines. Asking on
        // every viewing until the clock catches up would be the worse reading of it.
        var movie = Movie();
        movie.DetailAttemptedUtc = Noon.AddDays(1);

        // Act & Assert
        movie.NeedsDetailFetch(Noon).ShouldBeFalse();
    }

    private static VodItem Movie()
    {
        return new VodItem { SourceId = 1, ExternalId = "8412", Name = "Arrival" };
    }
}
