using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Waits for the panel to report a released connection, and says plainly whether it did.
/// </summary>
/// <remarks>
/// <para>
/// This is the actual proof of correct teardown, and the reason the play-test commands exist at all —
/// everything else about a stream can be checked by pasting its address into VLC. It polls rather than
/// asking once, because panels track connections on their own schedule and take seconds to notice a
/// client has gone. Reading that lag as a leak would condemn correct code; not distinguishing the two at
/// all would leave the only question that matters unanswered.
/// </para>
/// <para>
/// Shared by the live and the film play-test because both need it for the same reason, and because
/// running two of them in succession against a one-connection subscription is exactly how a perfectly
/// good stream comes back as "the provider refused the connection".
/// </para>
/// </remarks>
internal sealed class ConnectionReleaseCheck
{
    /// <summary>How many times to ask the panel whether the connection has been released.</summary>
    private const int Attempts = 5;

    private readonly IProviderRegistry _providers;
    private readonly TextWriter _output;

    /// <param name="output">
    /// Where the verdict goes. Injected so that this — the one thing the play-tests exist to establish — can
    /// be asserted rather than only read off a console by whoever happened to run the command.
    /// </param>
    public ConnectionReleaseCheck(IProviderRegistry providers, TextWriter output)
    {
        _providers = providers;
        _output = output;
    }

    /// <summary>
    /// How long to wait between asks.
    /// </summary>
    /// <remarks>
    /// Settable only at construction, and only really for the tests, which set it to zero: five seconds times
    /// four waits is twenty seconds a test would spend sleeping to establish nothing about the waiting. Not a
    /// <see cref="TimeProvider"/>, because <c>TestClock</c> deliberately leaves timers to the base class and
    /// would therefore wait for real — see the note on that class.
    /// </remarks>
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reports whether the panel still counts a connection, waiting for it to notice the release.
    /// </summary>
    /// <remarks>
    /// A source with no account behind it, such as a playlist, has no connection count to read, so there
    /// is nothing to check and nothing to report.
    /// </remarks>
    public async Task ReportAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        var provider = _providers.CreateProvider(source);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

            if (account.MaxConnections == 0 && account.ActiveConnections == 0)
            {
                // Nothing is being counted at all. True of a playlist, and of panels that do not report it.
                return;
            }

            if (account.ActiveConnections == 0)
            {
                _output.WriteLine(
                    attempt == 1
                        ? "The panel reports no open connections. Teardown is clean."
                        : $"The panel reports no open connections after {attempt} checks. Teardown is "
                            + "clean; the panel simply needed a moment to notice.");

                return;
            }

            _output.WriteLine(
                $"  check {attempt}/{Attempts}: the panel still counts "
                + $"{account.ActiveConnections} open connection(s).");

            if (attempt < Attempts)
            {
                await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        _output.WriteLine();
        _output.WriteLine(
            "The connection was still counted as open throughout. Either this player leaked it, or "
            + "another device is using the subscription. Check that nothing else is streaming, then "
            + "re-run probe.");
    }
}
