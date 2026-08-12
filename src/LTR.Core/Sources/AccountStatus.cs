namespace LTR.Core.Sources;

/// <summary>
/// Serviceability of a subscription as reported by the provider.
/// </summary>
public enum AccountStatus
{
    /// <summary>The provider replied, but with a status this player does not recognise.</summary>
    Unknown = 0,

    Active = 1,

    Expired = 2,

    Banned = 3,

    /// <summary>Credentials were rejected outright.</summary>
    AuthenticationFailed = 4,
}
