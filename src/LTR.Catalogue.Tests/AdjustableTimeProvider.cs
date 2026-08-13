namespace LTR.Catalogue;

/// <summary>
/// A clock a test can move.
/// </summary>
/// <remarks>
/// Written here rather than taken from <c>Microsoft.Extensions.TimeProvider.Testing</c>, which would be a
/// package for the sake of two methods (§2.15). Only <see cref="GetUtcNow"/> is used by anything under
/// test; timers are left to the base class, which throws for them, so a future use is a visible failure
/// rather than a silent one.
/// </remarks>
internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public AdjustableTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    public void Advance(TimeSpan interval)
    {
        _utcNow += interval;
    }
}
