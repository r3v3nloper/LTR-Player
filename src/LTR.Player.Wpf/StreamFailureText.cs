using LTR.Core.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// The sentence shown for each reason a stream would not open.
/// </summary>
/// <remarks>
/// <para>
/// Wording rather than classification, which is <see cref="StreamFailureReason"/>'s job in the core. The
/// split is the same one <c>SourceImportStage</c> uses, and so is the reason: the command line words these
/// differently because it is read by whoever is diagnosing rather than by whoever is watching.
/// </para>
/// <para>
/// Every sentence says what to do about it, or says plainly that there is nothing to do. That is the whole
/// point of the milestone this arrived in — the message it replaced named two causes at once and left the
/// viewer to guess which, and its remedy along with it.
/// </para>
/// </remarks>
internal static class StreamFailureText
{
    /// <summary>
    /// Internal rather than private so every reason can be proved to have wording of its own, which is how
    /// a reason added later fails a test instead of quietly reading as the fallback.
    /// </summary>
    public static string Describe(StreamFailureReason reason)
    {
        return reason switch
        {
            StreamFailureReason.ChannelUnavailable =>
                "The subscription is fine, so the channel itself is off the air. Try another one.",

            StreamFailureReason.ConnectionLimitReached =>
                "Every connection this subscription allows is already in use. Stop playback on your other "
                + "device, or wait a minute for the provider to notice it has closed.",

            StreamFailureReason.SubscriptionExpired =>
                "This subscription has expired. Nothing in the player will help until it is renewed.",

            StreamFailureReason.SubscriptionDisabled =>
                "The provider has disabled this subscription.",

            StreamFailureReason.CredentialsRejected =>
                "The provider rejected the stored details. Remove the subscription and add it again.",

            StreamFailureReason.ProviderUnreachable =>
                "The provider could not be reached at all, so check this machine's own connection first.",

            _ => "The provider gave no reason. The log records what it did say.",
        };
    }
}
