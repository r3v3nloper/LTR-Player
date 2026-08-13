namespace LTR.Player.Wpf;

/// <summary>
/// A clock that does not move, so a test can place programmes around a known moment.
/// </summary>
/// <remarks>
/// Written here rather than taken from <c>Microsoft.Extensions.TimeProvider.Testing</c>, which would be a
/// package for one method (§2.15).
/// </remarks>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}
