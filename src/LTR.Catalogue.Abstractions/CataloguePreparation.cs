namespace LTR.Catalogue;

/// <summary>
/// What preparing the catalogue at startup had to do.
/// </summary>
/// <remarks>
/// Reported rather than logged from inside, because both facts need saying in the caller's own voice: the
/// command line prints a line, the window puts a dialog in front of the user. A quarantined catalogue in
/// particular must not pass unmentioned — the user's configured subscriptions went with it.
/// </remarks>
/// <param name="UpgradedCredentials">Passwords rewritten from an unprotected form.</param>
/// <param name="QuarantinedDatabasePath">
/// Where an unreadable database was moved to, or <see langword="null"/> when the database was fine.
/// </param>
public sealed record CataloguePreparation(int UpgradedCredentials, string? QuarantinedDatabasePath)
{
    public static CataloguePreparation Nothing { get; } = new(0, null);

    public bool WasQuarantined => QuarantinedDatabasePath is not null;
}
