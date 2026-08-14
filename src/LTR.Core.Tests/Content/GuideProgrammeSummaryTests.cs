namespace LTR.Core.Content;

public sealed class GuideProgrammeSummaryTests
{
    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Duration_IsTheSpanBetweenItsEnds()
    {
        // Arrange & Act
        var programme = new GuideProgrammeSummary("Tagesschau", SixPm, SixPm.AddMinutes(15));

        // Assert
        programme.Duration.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(30, 0.5)]
    [InlineData(60, 1.0)]
    public void ProgressAt_ReportsHowFarThroughTheProgrammeAnInstantIs(int minutesIn, double expected)
    {
        // Arrange
        var programme = new GuideProgrammeSummary("Film", SixPm, SixPm.AddHours(1));

        // Act
        var progress = programme.ProgressAt(SixPm.AddMinutes(minutesIn));

        // Assert
        progress.ShouldBe(expected, tolerance: 0.001);
    }

    [Fact]
    public void ProgressAt_OutsideTheProgramme_IsClamped()
    {
        // Arrange: the clock moves on between a reread and the next, so an instant past the end does occur.
        var programme = new GuideProgrammeSummary("Film", SixPm, SixPm.AddHours(1));

        // Act & Assert
        programme.ProgressAt(SixPm.AddMinutes(-5)).ShouldBe(0);
        programme.ProgressAt(SixPm.AddHours(2)).ShouldBe(1);
    }

    [Fact]
    public void ProgressAt_ForAZeroLengthProgramme_IsZeroRatherThanUndefined()
    {
        // Arrange: the guide import rejects one, but dividing by it would take the window down rather than
        // draw an odd bar.
        var programme = new GuideProgrammeSummary("Impossible", SixPm, SixPm);

        // Act & Assert
        programme.ProgressAt(SixPm).ShouldBe(0);
    }
}
