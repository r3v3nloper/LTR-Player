using LTR.Core.Content;

namespace LTR.Core.Sources;

/// <summary>
/// Snapshot of a subscription's state at the moment it was queried. Deliberately not persisted:
/// connection counts and expiry are live facts that go stale immediately.
/// </summary>
/// <param name="Status">Whether the account can currently be used.</param>
/// <param name="ExpiresAtUtc">When access lapses; <see langword="null"/> for unlimited accounts.</param>
/// <param name="IsTrial">Whether this is a trial subscription.</param>
/// <param name="MaxConnections">
/// How many streams may be open at once. Exceeding it gets the account temporarily locked out,
/// which makes this the most important number the provider reports.
/// </param>
/// <param name="ActiveConnections">
/// How many streams the provider currently counts as open. A non-zero value while nothing is
/// playing means a previous session leaked a connection.
/// </param>
/// <param name="AllowedFormats">Container formats the account is permitted to request.</param>
public sealed record ProviderAccount(
    AccountStatus Status,
    DateTimeOffset? ExpiresAtUtc,
    bool IsTrial,
    int MaxConnections,
    int ActiveConnections,
    IReadOnlyList<StreamFormat> AllowedFormats)
{
    public bool IsUsable => Status == AccountStatus.Active;

    /// <summary>
    /// Whether another stream may be opened without tripping the provider's connection limit.
    /// </summary>
    public bool HasFreeConnection => MaxConnections <= 0 || ActiveConnections < MaxConnections;

    public static ProviderAccount Unauthenticated { get; } = new(
        AccountStatus.AuthenticationFailed,
        ExpiresAtUtc: null,
        IsTrial: false,
        MaxConnections: 0,
        ActiveConnections: 0,
        AllowedFormats: []);
}
