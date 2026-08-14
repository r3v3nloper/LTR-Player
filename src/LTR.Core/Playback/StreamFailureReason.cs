namespace LTR.Core.Playback;

/// <summary>
/// Why a stream would not open.
/// </summary>
/// <remarks>
/// <para>
/// The distinction matters more here than in most applications, because the three common causes need three
/// different things from the viewer and are indistinguishable from the failure itself. A channel the provider
/// has taken offline wants another channel picked. A connection limit wants the other device switched off,
/// and will clear on its own. An expired subscription wants paying for, and nothing the viewer does in this
/// player will help. One message covering all three sends people to the wrong remedy.
/// </para>
/// <para>
/// A classification rather than a sentence, because the front ends word it themselves —
/// <c>SourceImportStage</c> established that pattern, and the test that every value has wording is what stops
/// a new one reading as the fallback.
/// </para>
/// </remarks>
public enum StreamFailureReason
{
    /// <summary>Nothing further is known. The honest answer when the provider cannot be asked.</summary>
    Unknown = 0,

    /// <summary>
    /// The subscription is fine and has a connection free, so the channel itself is the problem.
    /// </summary>
    ChannelUnavailable = 1,

    /// <summary>Every connection the subscription permits is already in use.</summary>
    ConnectionLimitReached = 2,

    SubscriptionExpired = 3,

    /// <summary>The provider has disabled the account.</summary>
    SubscriptionDisabled = 4,

    /// <summary>The stored credentials were rejected.</summary>
    CredentialsRejected = 5,

    /// <summary>The provider could not be reached at all, which is as likely to be the local connection.</summary>
    ProviderUnreachable = 6,
}
