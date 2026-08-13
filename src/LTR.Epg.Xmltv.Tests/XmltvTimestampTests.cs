namespace LTR.Epg.Xmltv;

public sealed class XmltvTimestampTests
{
    [Theory]
    [InlineData("20260812183000 +0200", "2026-08-12T16:30:00Z")]
    [InlineData("20260812183000 -0500", "2026-08-12T23:30:00Z")]
    [InlineData("20260812183000 +0000", "2026-08-12T18:30:00Z")]
    [InlineData("20260812183000+0200", "2026-08-12T16:30:00Z")]
    [InlineData("20260812183000 +05:30", "2026-08-12T13:00:00Z")]
    public void TryParse_AppliesTheStatedOffset(string value, string expectedUtc)
    {
        // Arrange, Act
        var parsed = XmltvTimestamp.TryParse(value, out var instant);

        // Assert
        parsed.ShouldBeTrue();
        instant.ShouldBe(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A guide that states no zone is read as UTC. Guessing the machine's own zone would shift the whole
    /// guide by an amount that varies per user, which is harder to diagnose than a stated convention.
    /// </summary>
    [Theory]
    [InlineData("20260812183000", "2026-08-12T18:30:00Z")]
    [InlineData("20260812183000 Z", "2026-08-12T18:30:00Z")]
    [InlineData("20260812183000 UTC", "2026-08-12T18:30:00Z")]
    public void TryParse_TreatsAnAbsentOffsetAsUtc(string value, string expectedUtc)
    {
        // Arrange, Act
        var parsed = XmltvTimestamp.TryParse(value, out var instant);

        // Assert
        parsed.ShouldBeTrue();
        instant.ShouldBe(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every part after the year is optional in the format, and guides do truncate it.
    /// </summary>
    [Theory]
    [InlineData("202608121830 +0200", "2026-08-12T16:30:00Z")]
    [InlineData("2026081218 +0200", "2026-08-12T16:00:00Z")]
    [InlineData("20260812 +0200", "2026-08-11T22:00:00Z")]
    public void TryParse_AcceptsShortenedTimestamps(string value, string expectedUtc)
    {
        // Arrange, Act
        var parsed = XmltvTimestamp.TryParse(value, out var instant);

        // Assert
        parsed.ShouldBeTrue();
        instant.ShouldBe(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("20261399183000")]
    [InlineData("20260812183000 +2500")]
    [InlineData("20260812183000 0200")]
    [InlineData("20260812183000 +02")]
    public void TryParse_RejectsWhatItCannotRead(string? value)
    {
        // Arrange, Act
        var parsed = XmltvTimestamp.TryParse(value, out _);

        // Assert: rejected rather than approximated, so the entry is skipped instead of landing on the
        // wrong day.
        parsed.ShouldBeFalse();
    }
}
