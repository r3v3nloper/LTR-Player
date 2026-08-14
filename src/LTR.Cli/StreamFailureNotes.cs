using LTR.Core.Playback;

namespace LTR.Cli;

/// <summary>
/// What to print when a stream would not open.
/// </summary>
/// <remarks>
/// Worded for whoever is diagnosing rather than whoever is watching, which is why this is not the window's
/// wording. The window tells a viewer what to do about it; this says what the panel reported, and where the
/// figures behind it can be read.
/// </remarks>
internal static class StreamFailureNotes
{
    public static string Describe(StreamFailureReason reason)
    {
        return reason switch
        {
            StreamFailureReason.ChannelUnavailable =>
                "The panel reports a healthy account with a connection free, so this stream is the problem "
                + "— an offline channel, or an address shape this panel does not serve.",

            StreamFailureReason.ConnectionLimitReached =>
                "The panel counts every permitted connection as in use. If a previous play-test ran moments "
                + "ago this is its connection, not yours: wait for the panel to notice, then retry.",

            StreamFailureReason.SubscriptionExpired => "The subscription has expired.",

            StreamFailureReason.SubscriptionDisabled => "The provider has disabled the subscription.",

            StreamFailureReason.CredentialsRejected => "The panel rejected these credentials.",

            StreamFailureReason.ProviderUnreachable =>
                "The panel could not be reached to ask, so the local connection is as likely a cause.",

            _ => "The panel gave no usable answer when asked why. Run 'probe' for what it does report.",
        };
    }
}
