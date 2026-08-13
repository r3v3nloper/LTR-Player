namespace LTR.TestSupport;

/// <summary>
/// A clock a test controls.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than taken from <c>Microsoft.Extensions.TimeProvider.Testing</c>, which would be
/// a package for two methods (§2.15). Only <see cref="GetUtcNow"/> is used by anything under test; timers
/// are left to the base class, so a future use of one is a visible failure rather than a silent wrong
/// answer.
/// </para>
/// <para>
/// Shared by linking this file into each test project that needs it. A project of its own would be more
/// ceremony than one class deserves, and a copy per project is how two copies drift apart.
/// </para>
/// </remarks>
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestClock(DateTimeOffset utcNow)
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
